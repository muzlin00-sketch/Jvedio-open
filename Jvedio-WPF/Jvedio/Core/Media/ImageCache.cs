using Jvedio.Core.Media;
using System;
using System.Runtime.Caching;
using System.Windows.Media.Imaging;

namespace Jvedio.Core.Media
{
    /// <summary>
    /// 图片内存缓存。
    /// <para>
    /// 缓存键包含解码宽度：同一张图以不同 <c>DecodePixelWidth</c> 解码会得到尺寸不同的
    /// <see cref="BitmapImage"/>，若只用路径作键会互相串图（列表用小图、详情用大图时尤其明显）。
    /// </para>
    /// <para>
    /// 使用独立 <see cref="MemoryCache"/> 实例而非 <see cref="MemoryCache.Default"/>：
    /// 后者是进程级共享缓存，一旦被 <see cref="Clear"/> 释放，其他使用者也会一并失效。
    /// </para>
    /// </summary>
    public static class ImageCache
    {
        /// <summary>
        /// 默认图片缓存时长（分钟）
        /// </summary>
        public const long DEFAULT_CACHE_EXPIRATION = 10;

        private const string CACHE_NAME = "Jvedio.ImageCache";

        private static readonly object _Lock = new object();

        private static MemoryCache _Cache = CreateCache();

        private static MemoryCache CreateCache()
        {
            return new MemoryCache(CACHE_NAME);
        }

        /// <summary>
        /// 组合缓存键：路径 + 解码宽度。宽度为 0（原尺寸）时用 "orig" 区分。
        /// </summary>
        private static string BuildKey(string path, int decodePixelWidth)
        {
            string size = decodePixelWidth > 0
                ? decodePixelWidth.ToString()
                : "orig";
            return path + "|" + size;
        }

        public static BitmapImage Get(string path, int DecodePixelWidth = 0)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            string key = BuildKey(path, DecodePixelWidth);

            object cached = _Cache.Get(key);
            if (cached is BitmapImage cachedImage)
                return cachedImage;

            BitmapImage img = LoadImage(path, DecodePixelWidth);

            if (img == null)
                return null;

            if (!ConfigManager.Settings.ImageCache)
                return img;

            Add(key, img);
            return img;
        }

        /// <summary>
        /// 从磁盘加载图片。
        /// <para>
        /// 不使用闭源 <c>ImageHelper.ReadImageFromFile</c>：它内部以 <c>UriSource</c> 打开文件，
        /// 在部分盘符（如独立机械盘）上会抛「访问被拒绝」，即使该文件实际可读。
        /// </para>
        /// <para>
        /// 这里改为 <see cref="FileStream"/> + <see cref="BitmapCacheOption.OnLoad"/>：
        /// 以 <see cref="FileShare.ReadWrite"/> 打开，读完立即释放句柄，
        /// 避免与其他进程的读写冲突，也避免 WPF 延迟加载导致的文件占用。
        /// </para>
        /// </summary>
        private static BitmapImage LoadImage(string path, int decodePixelWidth)
        {
            try {
                if (!System.IO.File.Exists(path))
                    return null;

                BitmapImage bitmap = new BitmapImage();
                using (System.IO.FileStream fs = new System.IO.FileStream(
                    path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite)) {
                    bitmap.BeginInit();
                    bitmap.StreamSource = fs;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    if (decodePixelWidth > 0) {
                        bitmap.DecodePixelWidth = decodePixelWidth;
                    }
                    bitmap.EndInit();
                }
                bitmap.Freeze();
                return bitmap;
            } catch (Exception ex) {
                App.Logger?.Error($"加载图片失败 [{path}]: {ex.GetType().Name} - {ex.Message}");
                return null;
            }
        }

        private static void Add(string key, BitmapImage image)
        {
            CacheItemPolicy policy = new CacheItemPolicy();
            policy.SlidingExpiration = TimeSpan.FromMinutes(ConfigManager.Settings.CacheExpiration);
            // 覆盖写入：同一键重复加入时更新为最新图片
            _Cache.Set(key, image, policy);
        }

        /// <summary>
        /// 移除指定路径的所有缓存项（忽略解码宽度）。
        /// </summary>
        public static void Remove(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            string prefix = path + "|";
            var keys = new System.Collections.Generic.List<string>();
            foreach (var item in _Cache)
            {
                if (item.Key is string k && k.StartsWith(prefix, StringComparison.Ordinal))
                    keys.Add(k);
            }

            foreach (string k in keys)
                _Cache.Remove(k);
        }

        /// <summary>
        /// 清空缓存内容。
        /// <para>
        /// 只逐个移除条目，不释放 <see cref="MemoryCache"/> 实例本身，
        /// 避免缓存对象进入已释放状态后所有 <see cref="Get"/> 永久失效。
        /// </para>
        /// </summary>
        public static void Clear()
        {
            lock (_Lock)
            {
                var keys = new System.Collections.Generic.List<string>();
                foreach (var item in _Cache)
                    keys.Add(item.Key as string);

                foreach (string k in keys)
                {
                    if (!string.IsNullOrEmpty(k))
                        _Cache.Remove(k);
                }
            }
        }
    }
}
