using PKHeX.Core;
using RaidCrawler.Core.Interfaces;
using RaidCrawler.Core.Structures;
using SysBot.Base;
using System.Net.Sockets;
using System.Text;
using static SysBot.Base.SwitchButton;

namespace RaidCrawler.Core.Connection
{
    public class ConnectionWrapperAsync : Offsets
    {
        private static byte[] Encode(string command, bool crlf = true)
        {
            if (crlf)
                command += "\r\n";
            return Encoding.ASCII.GetBytes(command);
        }

        public async Task DaySkip2(CancellationToken token) => await Connection.SendAsync(Encode($"daySkip2", true), token).ConfigureAwait(false);

        public readonly ISwitchConnectionAsync Connection;
        public bool Connected
        {
            get => Connection is not null && IsConnected;
        }
        private bool IsConnected { get; set; }
        private readonly bool CRLF;
        private readonly Action<string> _statusUpdate;
        private static ulong BaseBlockKeyPointer = 0;

        public ConnectionWrapperAsync(SwitchConnectionConfig config, Action<string> statusUpdate)
        {
            Connection = config.Protocol switch
            {
                SwitchProtocol.USB => new SwitchUSBAsync(config.Port),
                _ => new SwitchSocketAsync(config),
            };

            CRLF = config.Protocol is SwitchProtocol.WiFi;
            _statusUpdate = statusUpdate;
        }

        public async Task<(bool, string)> Connect(CancellationToken token)
        {
            if (Connected)
                return (true, "");

            try
            {
                _statusUpdate("Connecting...");
                Connection.Connect();
                BaseBlockKeyPointer = await Connection
                    .PointerAll(BlockKeyPointer, token)
                    .ConfigureAwait(false);
                IsConnected = true;
                _statusUpdate("Connected!");
                return (true, "");
            }
            catch (SocketException e)
            {
                IsConnected = false;
                return (false, e.Message);
            }
        }

        public async Task<(bool, string)> DisconnectAsync(CancellationToken token)
        {
            if (!Connected)
                return (true, "");

            try
            {
                _statusUpdate("Disconnecting controller...");
                await Connection
                    .SendAsync(SwitchCommand.DetachController(CRLF), token)
                    .ConfigureAwait(false);

                _statusUpdate("Disconnecting...");
                Connection.Disconnect();
                IsConnected = false;
                _statusUpdate("Disconnected!");
                return (true, "");
            }
            catch (SocketException e)
            {
                IsConnected = false;
                return (false, e.Message);
            }
        }

        public async Task<int> GetStoryProgress(CancellationToken token)
        {
            for (int i = DifficultyFlags.Count - 1; i >= 0; i--)
            {
                // See https://github.com/Lincoln-LM/sv-live-map/pull/43
                var block = await ReadSaveBlock(DifficultyFlags[i], 1, token).ConfigureAwait(false);
                if (block[0] == 2)
                    return i + 1;
            }
            return 0;
        }

        private async Task<byte[]> ReadSaveBlock(uint key, int size, CancellationToken token)
        {
            var block_ofs = await SearchSaveKey(key, token).ConfigureAwait(false);
            var data = await Connection
                .ReadBytesAbsoluteAsync(block_ofs + 8, 0x8, token)
                .ConfigureAwait(false);
            block_ofs = BitConverter.ToUInt64(data, 0);

            var block = await Connection
                .ReadBytesAbsoluteAsync(block_ofs, size, token)
                .ConfigureAwait(false);
            return DecryptBlock(key, block);
        }

        private async Task<byte[]> ReadSaveBlockObject(uint key, CancellationToken token)
        {
            var header_ofs = await SearchSaveKey(key, token).ConfigureAwait(false);
            var data = await Connection
                .ReadBytesAbsoluteAsync(header_ofs + 8, 8, token)
                .ConfigureAwait(false);
            header_ofs = BitConverter.ToUInt64(data);

            var header = await Connection
                .ReadBytesAbsoluteAsync(header_ofs, 5, token)
                .ConfigureAwait(false);
            header = DecryptBlock(key, header);

            var size = BitConverter.ToUInt32(header.AsSpan()[1..]);
            var obj = await Connection
                .ReadBytesAbsoluteAsync(header_ofs, (int)size + 5, token)
                .ConfigureAwait(false);
            return DecryptBlock(key, obj)[5..];
        }

        public async Task<byte[]> ReadBlockDefault(
            uint key,
            string? cache,
            bool force,
            CancellationToken token
        )
        {
            var folder = Path.Combine(Directory.GetCurrentDirectory(), "cache");
            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, cache ?? "");
            if (force is false && cache is not null && File.Exists(path))
                return File.ReadAllBytes(path);

            var bin = await ReadSaveBlockObject(key, token).ConfigureAwait(false);
            File.WriteAllBytes(path, bin);
            return bin;
        }

        private async Task<ulong> SearchSaveKey(uint key, CancellationToken token)
        {
            var data = await Connection
                .ReadBytesAbsoluteAsync(BaseBlockKeyPointer + 8, 16, token)
                .ConfigureAwait(false);
            var start = BitConverter.ToUInt64(data.AsSpan()[..8]);
            var end = BitConverter.ToUInt64(data.AsSpan()[8..]);

            while (start < end)
            {
                var block_ct = (end - start) / 48;
                var mid = start + (block_ct >> 1) * 48;

                data = await Connection.ReadBytesAbsoluteAsync(mid, 4, token).ConfigureAwait(false);
                var found = BitConverter.ToUInt32(data);
                if (found == key)
                    return mid;

                if (found >= key)
                    end = mid;
                else
                    start = mid + 48;
            }
            return start;
        }

        private static byte[] DecryptBlock(uint key, byte[] block)
        {
            var rng = new SCXorShift32(key);
            for (int i = 0; i < block.Length; i++)
                block[i] = (byte)(block[i] ^ rng.Next());
            return block;
        }

        private async Task Click(SwitchButton button, int delay, CancellationToken token)
        {
            await Connection
                .SendAsync(SwitchCommand.Click(button, CRLF), token)
                .ConfigureAwait(false);
            await Task.Delay(delay, token).ConfigureAwait(false);
        }

        private async Task Touch(int x, int y, int hold, int delay, CancellationToken token)
        {
            var command = Encoding.ASCII.GetBytes(
                $"touchHold {x} {y} {hold}{(CRLF ? "\r\n" : "")}"
            );
            await Connection.SendAsync(command, token).ConfigureAwait(false);
            await Task.Delay(delay, token).ConfigureAwait(false);
        }

        private async Task SetStick(
            SwitchStick stick,
            short x,
            short y,
            int hold,
            int delay,
            CancellationToken token
        )
        {
            await Connection
                .SendAsync(SwitchCommand.SetStick(stick, x, y, CRLF), token)
                .ConfigureAwait(false);
            await Task.Delay(hold, token).ConfigureAwait(false);
            await Connection
                .SendAsync(SwitchCommand.SetStick(stick, 0, 0, CRLF), token)
                .ConfigureAwait(false);
            await Task.Delay(delay, token).ConfigureAwait(false);
        }

        private async Task PressAndHold(
            SwitchButton b,
            int hold,
            int delay,
            CancellationToken token
        )
        {
            await Connection.SendAsync(SwitchCommand.Hold(b, CRLF), token).ConfigureAwait(false);
            await Task.Delay(hold, token).ConfigureAwait(false);
            await Connection.SendAsync(SwitchCommand.Release(b, CRLF), token).ConfigureAwait(false);
            await Task.Delay(delay, token).ConfigureAwait(false);
        }

        // Thank you to Anubis for sharing CloseGame(), StartGame(), and SaveGame()!
        public async Task AdvanceDate(IDateAdvanceConfig config, CancellationToken token, Action<int>? action = null)
        {
            _statusUpdate("Changing date...");
            var BaseDelay = config.BaseDelay;

            _statusUpdate($"Setting \"Date and Time\"... ({config.TimeSetDelay} msec delay)");

            await DaySkip2(token).ConfigureAwait(false);
            await Task.Delay(config.TimeSetDelay);

            // Avoid disconnects on emuNAND
            var later = DateTime.Now.AddMilliseconds(config.TimeSetDelay);
            _statusUpdate($"Press \"B\" till {later}");
            while (DateTime.Now <= later)
                await Click(B, 200, token);

            _statusUpdate("Still in the game...");
        }

        public async Task CloseGame(CancellationToken token)
        {
            // Close out of the game
            _statusUpdate("Closing the game!");
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(HOME, 2_000, token).ConfigureAwait(false);
            await Click(X, 1_000, token).ConfigureAwait(false);


            await Click(A, 200, token).ConfigureAwait(false);

            // Avoid disconnects on emuNAND
            var later = DateTime.Now.AddMilliseconds(5_400);
            _statusUpdate($"Press \"B\" till {later}");
            while (DateTime.Now <= later)
                await Click(B, 200, token).ConfigureAwait(false);

            _statusUpdate("Closed out of the game!");
        }

        public async Task StartGame(IDateAdvanceConfig config, CancellationToken token)
        {
            // Open game.
            _statusUpdate("Starting the game!");
            await Click(A, 1_000, token).ConfigureAwait(false);

            var later = DateTime.Now.AddMilliseconds(3_000);
            _statusUpdate($"Press \"X\" till {later}");
            while (DateTime.Now <= later)
                await Click(X, 200, token).ConfigureAwait(false);

            later = DateTime.Now.AddMilliseconds(7_000);
            _statusUpdate($"Press \"A\" till {later}");
            while (DateTime.Now <= later)
                await Click(A, 200, token).ConfigureAwait(false);

            // Switch Logo and game load screen
            // Avoid disconnects on emuNAND
            later = DateTime.Now.AddMilliseconds(17_000);
            _statusUpdate($"Press \"B\" till {later}");
            while (DateTime.Now <= later)
                await Click(B, 200, token).ConfigureAwait(false);

            for (int i = 0; i < 40; i++)
                await Click(A, 500, token).ConfigureAwait(false);

            // Particularly slow switches need more time to load the overworld
            await Task.Delay(config.ExtraOverworldWait, token).ConfigureAwait(false);

            _statusUpdate("Back in the overworld! Refreshing the base block key pointer...");
            BaseBlockKeyPointer = await Connection
                .PointerAll(BlockKeyPointer, token)
                .ConfigureAwait(false);
        }

        public async Task SaveGame(IDateAdvanceConfig config, CancellationToken token)
        {
            _statusUpdate("Saving the game...");
            // B out in case we're in some menu.
            for (int i = 0; i < 4; i++)
                await Click(B, 0_500, token).ConfigureAwait(false);

            // Open the menu and save.
            await Click(X, 1_000, token).ConfigureAwait(false);
            await Click(R, 1_000, token).ConfigureAwait(false);
            await Click(A, 1_000, token).ConfigureAwait(false);
            await Click(A, 1_000, token).ConfigureAwait(false);
            await Click(A, 3_000 + config.SaveGameDelay, token).ConfigureAwait(false);

            // Return to overworld.
            for (int i = 0; i < 4; i++)
                await Click(B, 0_500, token).ConfigureAwait(false);
            _statusUpdate("Game saved!");
        }

        private static void UpdateProgressBar(Action<int>? action, int steps)
        {
            if (action is null)
                return;

            action.Invoke(steps);
        }
    }
}
