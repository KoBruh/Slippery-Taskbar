using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

internal static class IconConverter
{
    private sealed class Frame
    {
        public int Width;
        public int Height;
        public byte[] Dib;
    }

    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: IconConverter input.ico output.ico");
            return 2;
        }

        try
        {
            List<Frame> frames = ReadFrames(args[0]);
            WriteFrameworkIcon(args[1], frames);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static List<Frame> ReadFrames(string path)
    {
        byte[] ico = File.ReadAllBytes(path);
        if (ico.Length < 6 || ReadU16(ico, 0) != 0 || ReadU16(ico, 2) != 1)
            throw new InvalidDataException("Input is not an ICO file.");

        int count = ReadU16(ico, 4);
        if (count < 1 || ico.Length < 6 + count * 16)
            throw new InvalidDataException("ICO directory is truncated.");

        List<Frame> frames = new List<Frame>();
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + i * 16;
            int width = ico[entry] == 0 ? 256 : ico[entry];
            int height = ico[entry + 1] == 0 ? 256 : ico[entry + 1];
            int size = checked((int)ReadU32(ico, entry + 8));
            int offset = checked((int)ReadU32(ico, entry + 12));
            if (size <= 0 || offset < 0 || offset + size > ico.Length)
                throw new InvalidDataException("ICO image data is truncated.");

            byte[] image = new byte[size];
            Buffer.BlockCopy(ico, offset, image, 0, size);
            Bitmap bitmap = DecodeFrame(image, width, height);
            try
            {
                Frame frame = new Frame();
                frame.Width = bitmap.Width;
                frame.Height = bitmap.Height;
                frame.Dib = Make32BitDib(bitmap);
                frames.Add(frame);
            }
            finally
            {
                bitmap.Dispose();
            }
        }

        if (frames.Count == 0)
            throw new InvalidDataException("ICO contains no usable images.");
        return frames;
    }

    private static Bitmap DecodeFrame(byte[] image, int width, int height)
    {
        bool png = image.Length >= 8 &&
                   image[0] == 0x89 && image[1] == 0x50 && image[2] == 0x4E && image[3] == 0x47 &&
                   image[4] == 0x0D && image[5] == 0x0A && image[6] == 0x1A && image[7] == 0x0A;
        if (!png)
            throw new InvalidDataException("Only PNG-compressed source icon frames are expected by this converter.");

        using (MemoryStream stream = new MemoryStream(image, false))
        using (Image decoded = Image.FromStream(stream, true, true))
        {
            Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(output))
            {
                graphics.DrawImage(decoded, new Rectangle(0, 0, width, height));
            }
            return output;
        }
    }

    private static byte[] Make32BitDib(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        int xorBytes = checked(width * height * 4);
        int maskStride = ((width + 31) / 32) * 4;
        int maskBytes = checked(maskStride * height);

        using (MemoryStream stream = new MemoryStream(40 + xorBytes + maskBytes))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(40);              // BITMAPINFOHEADER size
            writer.Write(width);
            writer.Write(height * 2);      // XOR bitmap + AND mask
            writer.Write((ushort)1);       // planes
            writer.Write((ushort)32);      // bits per pixel
            writer.Write(0);               // BI_RGB
            writer.Write(xorBytes);
            writer.Write(0); writer.Write(0);
            writer.Write(0); writer.Write(0);

            // ICO DIB pixels are stored bottom-up as BGRA.
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    Color c = bitmap.GetPixel(x, y);
                    writer.Write(c.B);
                    writer.Write(c.G);
                    writer.Write(c.R);
                    writer.Write(c.A);
                }
            }

            // A zero AND mask lets the 32-bit alpha channel control transparency.
            writer.Write(new byte[maskBytes]);
            writer.Flush();
            return stream.ToArray();
        }
    }

    private static void WriteFrameworkIcon(string path, List<Frame> frames)
    {
        int offset = 6 + frames.Count * 16;
        using (FileStream file = File.Create(path))
        using (BinaryWriter writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)frames.Count);

            foreach (Frame frame in frames)
            {
                writer.Write((byte)(frame.Width >= 256 ? 0 : frame.Width));
                writer.Write((byte)(frame.Height >= 256 ? 0 : frame.Height));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(frame.Dib.Length);
                writer.Write(offset);
                offset += frame.Dib.Length;
            }

            foreach (Frame frame in frames)
                writer.Write(frame.Dib);
        }
    }

    private static ushort ReadU16(byte[] data, int offset)
    {
        return (ushort)(data[offset] | (data[offset + 1] << 8));
    }

    private static uint ReadU32(byte[] data, int offset)
    {
        return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
    }
}
