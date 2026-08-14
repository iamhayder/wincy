using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Wincy.Services;

/// <summary>Conversions between clipboard DIBs, PNG bytes and WPF bitmaps.</summary>
public static class ImageHelper
{
    private const int BitmapFileHeaderSize = 14;
    private const int BitmapInfoHeaderSize = 40;

    /// <summary>
    /// A CF_DIB payload is a BITMAPINFOHEADER followed by pixels — a .bmp file with its
    /// 14-byte file header removed. Putting the header back makes it decodable.
    /// </summary>
    public static byte[]? DibToBmpFile(byte[] dib)
    {
        if (dib.Length < BitmapInfoHeaderSize)
        {
            return null;
        }

        var headerSize = BitConverter.ToInt32(dib, 0);
        var bitCount = BitConverter.ToInt16(dib, 14);
        var compression = BitConverter.ToInt32(dib, 16);
        var clrUsed = BitConverter.ToInt32(dib, 32);

        var paletteEntries = bitCount switch
        {
            <= 8 when clrUsed > 0 => clrUsed,
            <= 8 => 1 << bitCount,
            _ => clrUsed
        };

        var paletteBytes = paletteEntries * 4;

        // BI_BITFIELDS (3) adds three colour masks after the header.
        if (compression == 3)
        {
            paletteBytes += 12;
        }

        var pixelOffset = BitmapFileHeaderSize + headerSize + paletteBytes;
        var file = new byte[BitmapFileHeaderSize + dib.Length];

        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BitConverter.GetBytes(file.Length).CopyTo(file, 2);
        BitConverter.GetBytes(0).CopyTo(file, 6);
        BitConverter.GetBytes(pixelOffset).CopyTo(file, 10);
        dib.CopyTo(file, BitmapFileHeaderSize);

        return file;
    }

    /// <summary>Decodes any image payload Wincy stores (PNG bytes or a raw DIB).</summary>
    public static BitmapSource? Decode(byte[]? data, bool isDib)
    {
        if (data is null || data.Length == 0)
        {
            return null;
        }

        try
        {
            var bytes = isDib ? DibToBmpFile(data) : data;
            if (bytes is null)
            {
                return null;
            }

            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(
                stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
            {
                return null;
            }

            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not decode image: " + ex.Message);
            return null;
        }
    }

    public static byte[]? EncodePng(BitmapSource source)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not encode PNG: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Rebuilds a CF_DIB payload from a bitmap: a 40-byte BITMAPINFOHEADER followed by
    /// bottom-up 32bpp BI_RGB rows. Wincy stores images as PNG to keep the database
    /// small, but most Windows apps only accept a DIB on paste, so one is regenerated
    /// at write time.
    /// </summary>
    public static byte[]? BitmapToDib(BitmapSource source)
    {
        try
        {
            var bgra = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            var width = bgra.PixelWidth;
            var height = bgra.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            bgra.CopyPixels(pixels, stride, 0);

            var dib = new byte[BitmapInfoHeaderSize + pixels.Length];

            BitConverter.GetBytes(BitmapInfoHeaderSize).CopyTo(dib, 0);   // biSize
            BitConverter.GetBytes(width).CopyTo(dib, 4);                  // biWidth
            BitConverter.GetBytes(height).CopyTo(dib, 8);                 // biHeight (bottom-up)
            BitConverter.GetBytes((short)1).CopyTo(dib, 12);              // biPlanes
            BitConverter.GetBytes((short)32).CopyTo(dib, 14);             // biBitCount
            BitConverter.GetBytes(0).CopyTo(dib, 16);                     // biCompression = BI_RGB
            BitConverter.GetBytes(pixels.Length).CopyTo(dib, 20);         // biSizeImage

            // Bottom-up: the last source row is written first.
            for (var y = 0; y < height; y++)
            {
                Buffer.BlockCopy(
                    pixels, (height - 1 - y) * stride,
                    dib, BitmapInfoHeaderSize + (y * stride),
                    stride);
            }

            return dib;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not build a DIB: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Scales a bitmap so that it fits within the given box, never enlarging it.
    /// Maccy caps thumbnails at 340 x imageMaxHeight; Wincy does the same.
    /// </summary>
    public static BitmapSource? Resize(BitmapSource? source, double maxWidth, double maxHeight)
    {
        if (source is null)
        {
            return null;
        }

        if (source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            return null;
        }

        var scale = Math.Min(maxWidth / source.PixelWidth, maxHeight / source.PixelHeight);
        if (scale >= 1.0)
        {
            return source;
        }

        try
        {
            var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            transformed.Freeze();
            return transformed;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not resize image: " + ex.Message);
            return source;
        }
    }
}
