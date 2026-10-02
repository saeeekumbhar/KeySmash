using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace KeySmash;

public static class TrayIconFactory
{
    public static Icon CreateKeySmashIcon(bool active)
    {
        using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Active vs Dimmed colors
            var keyTopColor = active ? Color.FromArgb(45, 52, 64) : Color.FromArgb(28, 32, 40);
            var keyBottomColor = active ? Color.FromArgb(24, 28, 36) : Color.FromArgb(16, 18, 24);
            var borderColor = active ? Color.FromArgb(90, 105, 128) : Color.FromArgb(45, 52, 64);
            var textColor = active ? Color.FromArgb(235, 240, 245) : Color.FromArgb(95, 105, 120);
            var dotColor = active ? Color.FromArgb(64, 180, 255) : Color.FromArgb(60, 68, 80);

            using var brush = new LinearGradientBrush(
                new Rectangle(2, 2, 28, 28),
                keyTopColor,
                keyBottomColor,
                LinearGradientMode.Vertical);

            using var path = new GraphicsPath();
            path.AddArc(2, 2, 6, 6, 180, 90);
            path.AddArc(24, 2, 6, 6, 270, 90);
            path.AddArc(24, 24, 6, 6, 0, 90);
            path.AddArc(2, 24, 6, 6, 90, 90);
            path.CloseFigure();

            g.FillPath(brush, path);

            using var borderPen = new Pen(borderColor, 1.5f);
            g.DrawPath(borderPen, path);

            // letter 'K' in center
            using var font = new Font(FontFamily.GenericSansSerif, 13f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(textColor);
            var stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("K", font, textBrush, new RectangleF(0, 1, 32, 32), stringFormat);

            // small status dot on top right
            using var waveBrush = new SolidBrush(dotColor);
            g.FillEllipse(waveBrush, 22, 5, 4, 4);

            if (!active)
            {
                // Distinct red diagonal slash over the indicator dot clearly signifying guns/audio are down
                using var slashPen = new Pen(Color.FromArgb(239, 68, 68), 1.75f);
                slashPen.StartCap = LineCap.Round;
                slashPen.EndCap = LineCap.Round;
                g.DrawLine(slashPen, 19, 3, 27, 11);
            }
        }

        using var pngMs = new MemoryStream();
        bmp.Save(pngMs, ImageFormat.Png);
        var pngBytes = pngMs.ToArray();

        using var iconMs = new MemoryStream();
        using var bw = new BinaryWriter(iconMs);

        // ICO header
        bw.Write((short)0); // reserved
        bw.Write((short)1); // icon type
        bw.Write((short)1); // 1 image

        // entry
        bw.Write((byte)32); // width
        bw.Write((byte)32); // height
        bw.Write((byte)0);  // colors
        bw.Write((byte)0);  // reserved
        bw.Write((short)1); // planes
        bw.Write((short)32);// bpp
        bw.Write(pngBytes.Length); // size
        bw.Write(22); // offset (6 header + 16 entry)

        bw.Write(pngBytes);
        iconMs.Position = 0;

        return new Icon(iconMs);
    }
}
