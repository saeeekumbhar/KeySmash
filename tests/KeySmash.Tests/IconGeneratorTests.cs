using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Xunit;

namespace KeySmash.Tests;

public class IconGeneratorTests
{
    [Fact]
    public void GenerateAppIcon()
    {
        var current = AppDomain.CurrentDomain.BaseDirectory;
        var dirInfo = new DirectoryInfo(current);
        while (dirInfo != null && !File.Exists(Path.Combine(dirInfo.FullName, "KeySmash.sln")))
        {
            dirInfo = dirInfo.Parent;
        }

        var root = dirInfo?.FullName ?? Path.GetFullPath(@"..\..\..\..\..");
        var assetsIcon = Path.Combine(root, "assets", "icon.ico");
        var srcIcon = Path.Combine(root, "src", "KeySmash", "icon.ico");

        using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // key background with rounded corners
            using var brush = new LinearGradientBrush(
                new Rectangle(2, 2, 28, 28),
                Color.FromArgb(45, 52, 64),
                Color.FromArgb(24, 28, 36),
                LinearGradientMode.Vertical);

            using var path = new GraphicsPath();
            path.AddArc(2, 2, 6, 6, 180, 90);
            path.AddArc(24, 2, 6, 6, 270, 90);
            path.AddArc(24, 24, 6, 6, 0, 90);
            path.AddArc(2, 24, 6, 6, 90, 90);
            path.CloseFigure();

            g.FillPath(brush, path);

            using var borderPen = new Pen(Color.FromArgb(90, 105, 128), 1.5f);
            g.DrawPath(borderPen, path);

            // letter 'K' in center
            using var font = new Font(FontFamily.GenericSansSerif, 13f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.FromArgb(235, 240, 245));
            var stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("K", font, textBrush, new RectangleF(0, 1, 32, 32), stringFormat);

            // small sound wave dot on top right
            using var waveBrush = new SolidBrush(Color.FromArgb(64, 180, 255));
            g.FillEllipse(waveBrush, 22, 5, 4, 4);
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        var pngBytes = ms.ToArray();

        using var iconStream = new MemoryStream();
        using var bw = new BinaryWriter(iconStream);

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

        var iconData = iconStream.ToArray();
        File.WriteAllBytes(assetsIcon, iconData);
        File.WriteAllBytes(srcIcon, iconData);

        Assert.True(File.Exists(assetsIcon));
        Assert.True(File.Exists(srcIcon));
    }
}
