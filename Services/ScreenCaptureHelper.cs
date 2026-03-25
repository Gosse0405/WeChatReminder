using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WeChatReminder.Services;

public static class ScreenCaptureHelper
{
    public static double CaptureAverageBrightness(Rectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return 0;

        using var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);

        g.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size);

        var bitmapRect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        BitmapData? bitmapData = null;
        byte[]? buffer = null;

        try
        {
            bitmapData = bmp.LockBits(bitmapRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            int stride = bitmapData.Stride;
            int absStride = Math.Abs(stride);
            int byteCount = absStride * bitmapData.Height;
            buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            Marshal.Copy(bitmapData.Scan0, buffer, 0, byteCount);

            double total = 0;
            int count = 0;

            for (int y = 0; y < bmp.Height; y += 2)
            {
                int rowOffset = stride >= 0
                    ? y * stride
                    : (bmp.Height - 1 - y) * absStride;

                for (int x = 0; x < bmp.Width; x += 2)
                {
                    int offset = rowOffset + x * 4;
                    byte b = buffer[offset];
                    byte gValue = buffer[offset + 1];
                    byte r = buffer[offset + 2];

                    total += (0.299 * r + 0.587 * gValue + 0.114 * b);
                    count++;
                }
            }

            return count == 0 ? 0 : total / count;
        }
        finally
        {
            if (bitmapData != null)
                bmp.UnlockBits(bitmapData);

            if (buffer != null)
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
