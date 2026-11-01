using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Jvedio.Core.Media
{
    /// <summary>
    /// 图片加载/转换，取代 <c>SuperUtils.Media.ImageHelper</c>。
    /// <para>
    /// 关键点：文件一律先读进 <see cref="MemoryStream"/> 再建 <see cref="BitmapImage"/>，
    /// 并把 <c>CacheOption</c> 设成 <c>OnLoad</c>，否则图片文件会被界面长期占用、删不掉。
    /// </para>
    /// </summary>
    public static class ImageHelper
    {
        public static BitmapImage BitmapImageFromFile(string filepath, int decodePixelWidth = 0)
        {
            if (!File.Exists(filepath))
                return null;

            try {
                using (FileStream fileStream = new FileStream(filepath, FileMode.Open)) {
                    using (MemoryStream memoryStream = new MemoryStream()) {
                        fileStream.CopyTo(memoryStream);
                        memoryStream.Position = 0;

                        BitmapImage image = new BitmapImage();
                        image.BeginInit();
                        image.StreamSource = memoryStream;
                        if (decodePixelWidth > 0)
                            image.DecodePixelWidth = decodePixelWidth;

                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.EndInit();
                        image.Freeze();
                        return image;
                    }
                }
            } catch (Exception) {
                return null;
            }
        }

        public static BitmapImage BitmapImageFromByte(byte[] fileByte)
        {
            if (fileByte == null || fileByte.Length == 0)
                return null;

            try {
                using (MemoryStream memoryStream = new MemoryStream(fileByte)) {
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = memoryStream;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            } catch (Exception) {
                return null;
            }
        }

        public static BitmapImage ImageFromUri(string uri, double decodePixelWidth = 0)
        {
            if (string.IsNullOrEmpty(uri))
                return null;

            try {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(uri);
                if (decodePixelWidth != 0)
                    image.DecodePixelWidth = (int)decodePixelWidth;

                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                return image;
            } catch (Exception) {
                return null;
            }
        }

        public static Bitmap ReadImageFromFile(string filepath, int decodePixelWidth = 0)
        {
            if (!File.Exists(filepath))
                return null;

            BitmapImage image = BitmapImageFromFile(filepath, decodePixelWidth);
            return image == null ? null : ImageSourceToBitmap(image);
        }

        /// <summary>
        /// 把 WPF 图像转成 GDI+ 位图。
        /// <para>位深必须是<b>预乘</b> alpha 的 32bppPArgb，与原实现一致；
        /// 换成普通 32bppArgb 会让半透明像素颜色偏亮。</para>
        /// </summary>
        public static Bitmap ImageSourceToBitmap(ImageSource imageSource)
        {
            if (imageSource == null)
                return null;

            BitmapSource source = (BitmapSource)imageSource;
            Bitmap bitmap = new Bitmap(source.PixelWidth, source.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            BitmapData data = bitmap.LockBits(
                new Rectangle(System.Drawing.Point.Empty, bitmap.Size),
                ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try {
                source.CopyPixels(Int32Rect.Empty, data.Scan0, data.Height * data.Stride, data.Stride);
            } finally {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        /// <summary>转成图标；<paramref name="hicon"/> 交给调用方用 <see cref="DestroyIcon"/> 释放。</summary>
        public static Icon BitmapToIcon(Bitmap bmp, out IntPtr hicon)
        {
            hicon = IntPtr.Zero;
            if (bmp == null)
                return null;

            try {
                hicon = bmp.GetHicon();
                return Icon.FromHandle(hicon);
            } catch (Exception) {
                return null;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }
}
