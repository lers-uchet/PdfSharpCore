using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using SkiaSharp;

namespace PdfSharpCore.Utils
{
    /// <summary>
    /// Image source implementation based on SkiaSharp.
    /// </summary>
    public class SkiaSharpImageSource : ImageSource
    {
        /// <summary>
        /// Creates an image source from an existing SkiaSharp bitmap.
        /// The returned image source takes ownership of the bitmap and disposes it when disposed.
        /// </summary>
        public static IImageSource FromSkiaSharpImage(SKBitmap image, int? quality = 75)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            var name = "*" + Guid.NewGuid().ToString("B");
            return new SkiaSharpImageSourceImpl(name, image, quality ?? 75, IsTransparent(image));
        }

        protected override IImageSource FromBinaryImpl(string name, Func<byte[]> imageSource, int? quality = 75)
        {
            var image = SKBitmap.Decode(imageSource.Invoke());
            return CreateImageSource(name, image, quality);
        }

        protected override IImageSource FromFileImpl(string path, int? quality = 75)
        {
            var image = SKBitmap.Decode(path);
            return CreateImageSource(path, image, quality);
        }

        protected override IImageSource FromStreamImpl(string name, Func<Stream> imageStream, int? quality = 75)
        {
            using (var stream = imageStream.Invoke())
            using (var memoryStream = new MemoryStream())
            {
                stream.CopyTo(memoryStream);
                var image = SKBitmap.Decode(memoryStream.ToArray());
                return CreateImageSource(name, image, quality);
            }
        }

        private static IImageSource CreateImageSource(string name, SKBitmap image, int? quality)
        {
            if (image == null)
                throw new InvalidOperationException($"Could not decode image '{name}'.");

            return new SkiaSharpImageSourceImpl(name, image, quality ?? 75, IsTransparent(image));
        }

        private static bool IsTransparent(SKBitmap image)
        {
            return image.Info.AlphaType != SKAlphaType.Opaque;
        }

        private class SkiaSharpImageSourceImpl : IImageSource
        {
            private SKBitmap Image { get; }
            private readonly int _quality;

            public int Width => Image.Width;
            public int Height => Image.Height;
            public string Name { get; }
            public bool Transparent { get; }

            public SkiaSharpImageSourceImpl(string name, SKBitmap image, int quality, bool isTransparent)
            {
                Name = name;
                Image = image;
                _quality = Math.Max(0, Math.Min(100, quality));
                Transparent = isTransparent;
            }

            public void SaveAsJpeg(MemoryStream ms)
            {
                using (var image = SKImage.FromBitmap(Image))
                using (var data = image.Encode(SKEncodedImageFormat.Jpeg, _quality))
                {
                    if (data == null)
                        throw new InvalidOperationException("Could not encode image as JPEG.");

                    data.SaveTo(ms);
                }
            }

            public void Dispose()
            {
                Image.Dispose();
            }

            public void SaveAsPdfBitmap(MemoryStream ms)
            {
                SaveAsBmp(ms);
            }

            private void SaveAsBmp(MemoryStream ms)
            {
                int width = Image.Width;
                int height = Image.Height;
                int rowBytes = width * 4;
                int pixelDataSize = rowBytes * height;

                // PdfImage expects a 32-bpp, bottom-up, uncompressed BMP (BI_RGB).
                // SkiaSharp cannot encode BMP, so write the file manually. Reading the
                // pixels as unpremultiplied BGRA keeps the alpha channel intact.
                var destinationInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                using (var destination = new SKBitmap(destinationInfo))
                {
                    using (var image = SKImage.FromBitmap(Image))
                    {
                        if (!image.ReadPixels(destinationInfo, destination.GetPixels(), destination.RowBytes, 0, 0))
                            throw new InvalidOperationException("Could not read image pixels.");
                    }

                    using (var writer = new BinaryWriter(ms, Encoding.UTF8, true))
                    {
                        writer.Write((byte)'B');
                        writer.Write((byte)'M');
                        writer.Write(14 + 40 + pixelDataSize);
                        writer.Write(0);
                        writer.Write(14 + 40);

                        // BITMAPINFOHEADER
                        writer.Write(40);
                        writer.Write(width);
                        writer.Write(height);
                        writer.Write((short)1);
                        writer.Write((short)32);
                        writer.Write(0);
                        writer.Write(pixelDataSize);
                        writer.Write(2835); // 72 DPI
                        writer.Write(2835); // 72 DPI
                        writer.Write(0);
                        writer.Write(0);

                        var row = new byte[rowBytes];
                        for (int y = height - 1; y >= 0; y--)
                        {
                            Marshal.Copy(IntPtr.Add(destination.GetPixels(), y * destination.RowBytes), row, 0, rowBytes);
                            writer.Write(row);
                        }
                    }
                }
            }
        }
    }
}
