using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoreLauncher.Core;

/// <summary>Пиксельный буфер BGRA с наложением по альфе.</summary>
public sealed class Px
{
    public int W, H;
    public byte[] D;

    public Px(int w, int h) { W = w; H = h; D = new byte[w * h * 4]; }

    public static Px FromBytes(byte[] png)
    {
        var dec = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var conv = new FormatConvertedBitmap(dec.Frames[0], PixelFormats.Bgra32, null, 0);
        var px = new Px(conv.PixelWidth, conv.PixelHeight);
        conv.CopyPixels(px.D, px.W * 4, 0);
        return px;
    }

    public static Px FromFile(string path) => FromBytes(File.ReadAllBytes(path));

    public void Fill(int x, int y, int w, int h, uint argb)
    {
        for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++) Set(i, j, argb);
    }

    public void Set(int x, int y, uint argb)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        int o = (y * W + x) * 4;
        D[o] = (byte)argb; D[o + 1] = (byte)(argb >> 8); D[o + 2] = (byte)(argb >> 16); D[o + 3] = (byte)(argb >> 24);
    }

    /// <summary>Копирует прямоугольник src в this с учётом прозрачности.</summary>
    public void Blit(Px src, int sx, int sy, int w, int h, int dx, int dy)
    {
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                int x = sx + i, y = sy + j;
                if (x < 0 || y < 0 || x >= src.W || y >= src.H) continue;
                int tx = dx + i, ty = dy + j;
                if (tx < 0 || ty < 0 || tx >= W || ty >= H) continue;
                int so = (y * src.W + x) * 4, to = (ty * W + tx) * 4;
                int a = src.D[so + 3];
                if (a == 0) continue;
                if (a == 255) { Buffer.BlockCopy(src.D, so, D, to, 4); continue; }
                int ta = D[to + 3];
                int oa = a + ta * (255 - a) / 255;
                if (oa == 0) continue;
                for (int c = 0; c < 3; c++)
                    D[to + c] = (byte)((src.D[so + c] * a + D[to + c] * ta * (255 - a) / 255) / oa);
                D[to + 3] = (byte)oa;
            }
    }

    public BitmapSource ToBitmap()
    {
        var bmp = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, D, W * 4);
        bmp.Freeze();
        return bmp;
    }

    public byte[] ToPng()
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(ToBitmap()));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}

/// <summary>Маленькие плоские превью для плиток: лицо скина и рисунок плаща. Сам персонаж рисуется объёмно в Skin3D.</summary>
public static class SkinRender
{
    public static BitmapSource Head(Px skin)
    {
        var o = new Px(8, 8);
        o.Blit(skin, 8, 8, 8, 8, 0, 0);
        o.Blit(skin, 40, 8, 8, 8, 0, 0);
        return o.ToBitmap();
    }

    public static BitmapSource CapeFace(Px cape)
    {
        var o = new Px(10, 16);
        o.Blit(cape, 1, 1, 10, 16, 0, 0);
        return o.ToBitmap();
    }
}
