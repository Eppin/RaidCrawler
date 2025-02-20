namespace RaidCrawler.WinForms;

using System.Text;
using NLog;
using NLog.Config;
using NLog.Targets;

public static class LogUtil
{
    static LogUtil()
    {
        var config = new LoggingConfiguration();
        Directory.CreateDirectory("logs");
        var logfile = new FileTarget("logfile")
        {
            FileName = Path.Combine("logs", "RaidCrawler.txt"),
            ConcurrentWrites = true,

            ArchiveEvery = FileArchivePeriod.Day,
            ArchiveNumbering = ArchiveNumberingMode.Date,
            ArchiveFileName = Path.Combine("logs", "RaidCrawler.{#}.txt"),
            ArchiveDateFormat = "yyyy-MM-dd",
            ArchiveAboveSize = 104857600, // 100MB (never)
            MaxArchiveFiles = 14, // 2 weeks
            Encoding = Encoding.Unicode,
            WriteBom = true,
        };
        config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);
        LogManager.Configuration = config;
    }

    private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
    public static void LogText(string message) => Logger.Log(LogLevel.Info, message);

    // hook in here if you want to forward the message elsewhere???
    public static readonly List<Action<string, string>> Forwarders = new();

    public static DateTime LastLogged { get; private set; } = DateTime.Now;

    public static void LogError(string message, string identity)
    {
        Logger.Log(LogLevel.Error, $"{identity} {message}");
        Log(message, identity);
    }

    public static void LogInfo(string message, string identity, bool logAlways = true)
    {
        Logger.Log(LogLevel.Info, $"{identity} {message}");
        Log(message, identity, logAlways);
    }

    private static void Log(string message, string identity, bool logAlways = true)
    {
        foreach (var fwd in Forwarders)
        {
            try
            {
                if (logAlways)
                    fwd(message, identity);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                Logger.Log(LogLevel.Error, $"Failed to forward log from {identity} - {message}");
                Logger.Log(LogLevel.Error, ex);
            }
        }

        LastLogged = DateTime.Now;
    }

    public static void LogSafe(Exception exception, string identity)
    {
        Logger.Log(LogLevel.Error, $"Exception from {identity}:");
        Logger.Log(LogLevel.Error, exception);

        var err = exception.InnerException;
        while (err is not null)
        {
            Logger.Log(LogLevel.Error, err);
            err = err.InnerException;
        }
    }
}
