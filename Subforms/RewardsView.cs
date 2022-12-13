﻿using RaidCrawler.Structures;
using PKHeX.Drawing;

namespace RaidCrawler.Subforms
{
    public partial class RewardsView : Form
    {
        public RewardsView(List<(int, int, int)> rewards)
        {
            InitializeComponent();
            Bitmap rare = PKHeX.Drawing.PokeSprite.Properties.Resources.rare_icon;
            PictureBox[] pictures = new PictureBox[rewards.Count];
            Label[] labels = new Label[rewards.Count];
            for (int i = 0; i < rewards.Count; i++)
            {
                pictures[i] = new PictureBox();
                pictures[i].Size = new Size(24, 24);
                pictures[i].Location = new Point(12, 12 + i * (pictures[i].Size.Height + 12));
                pictures[i].SizeMode = PictureBoxSizeMode.CenterImage;
                labels[i] = new Label();
                var item = rewards[i].Item1 switch
                {
                    10000 => "Material",
                    20000 => "Tera Shard",
                    _ => Raid.strings.Item[rewards[i].Item1]
                };
                var subject = rewards[i].Item3 switch
                {
                    1 => "(Host)",
                    2 => "(Client)",
                    3 => "(Once)",
                    _ => string.Empty
                };
                var img = rewards[i].Item1 switch
                {
                    // Handling for sprites that pkhex doesn't have
                    1904 => (Image?)Properties.Resources.ResourceManager.GetObject("item_1904"),
                    1905 => (Image?)Properties.Resources.ResourceManager.GetObject("item_1905"),
                    1906 => (Image?)Properties.Resources.ResourceManager.GetObject("item_1906"),
                    1907 => (Image?)Properties.Resources.ResourceManager.GetObject("item_1907"),
                    1908 => (Image?)Properties.Resources.ResourceManager.GetObject("item_1908"),
                    10000 => (Image?)Properties.Resources.ResourceManager.GetObject("material"),
                    20000 => (Image?)PKHeX.Drawing.PokeSprite.Properties.Resources.ResourceManager.GetObject("aitem_1862"),
                    _ => (Image?)PKHeX.Drawing.PokeSprite.Properties.Resources.ResourceManager.GetObject($"aitem_{rewards[i].Item1}")
                };
                if (img != null && Rewards.RareRewards.Contains(rewards[i].Item1))
                    img = ImageUtil.LayerImage(img, rare, 0, 0, 0.7);
                pictures[i].Image = img;
                labels[i].Text = $"{item} x{rewards[i].Item2} {subject}".TrimEnd();
                labels[i].Location = new Point(60, 12 + i * (pictures[i].Size.Height + 12));
                Controls.Add(pictures[i]);
                Controls.Add(labels[i]);
            }
            ClientSize = new Size(ClientSize.Width, 12 + rewards.Count * (pictures[0].Size.Height + 12));
        }
    }
}
