using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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

    private static readonly int[] Sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };

    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: IconConverter input-image output.ico");
            return 2;
        }

        try
        {
            List<Frame> frames = BuildFrames(args[0]);
            WriteIcon(args[1], frames);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static List<Frame> BuildFrames(string inputPath)
    {
        List<Frame> frames = new List<Frame>();
        using (Image source = Image.FromFile(inputPath))
        {
            foreach (int size in Sizes)
            {
                using (Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    float scale = Math.Min((float)size / source.Width, (float)size / source.Height);
                    int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                    int height = Math.Max(1, (int)Math.Round(source.Height * scale));
                    int x = (size - width) / 2;
                    int y = (size - height) / 2;
                    graphics.DrawImage(source, new Rectangle(x, y, width, height));

                    Frame frame = new Frame();
                    frame.Width = size;
                    frame.Height = size;
                    frame.Dib = Make32BitDib(bitmap);
                    frames.Add(frame);
                }
            }
        }
        return frames;
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
            writer.Write(40);
            writer.Write(width);
            writer.Write(height * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(0);
            writer.Write(xorBytes);
            writer.Write(0); writer.Write(0);
            writer.Write(0); writer.Write(0);

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

            writer.Write(new byte[maskBytes]);
            writer.Flush();
            return stream.ToArray();
        }
    }

    private static void WriteIcon(string path, List<Frame> frames)
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
}
