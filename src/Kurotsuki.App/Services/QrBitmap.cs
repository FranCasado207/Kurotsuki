using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Kurotsuki.App.Services;

/// <summary>Draws a QR module matrix as a black-on-white bitmap with the required 4-module quiet zone.</summary>
public static class QrBitmap
{
    public static WriteableBitmap Render(bool[,] modules, int scale = 6, int quiet = 4)
    {
        var n = modules.GetLength(0);
        var size = (n + quiet * 2) * scale;
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var buffer = bitmap.Lock();
        var row = new byte[size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                int mx = x / scale - quiet, my = y / scale - quiet;
                var dark = mx >= 0 && my >= 0 && mx < n && my < n && modules[my, mx];
                var v = dark ? (byte)0 : (byte)255;
                row[x * 4] = row[x * 4 + 1] = row[x * 4 + 2] = v;
                row[x * 4 + 3] = 255;
            }
            System.Runtime.InteropServices.Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, row.Length);
        }
        return bitmap;
    }
}
