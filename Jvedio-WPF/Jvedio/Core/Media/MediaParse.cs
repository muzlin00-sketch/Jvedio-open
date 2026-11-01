using System;
using System.IO;
using System.Linq;
using static Jvedio.App;

namespace Jvedio.Core.Media
{
    /// <summary>
    /// 视频时长/尺寸/截图时间点计算，取代 <c>SuperUtils.Media.MediaParse</c>。
    /// <para>
    /// 截图时间点不是均匀分布：先按「忽略片头/片尾（单位<b>分钟</b>）」缩短有效时长，
    /// 再按张数均分。忽略参数之所以是分钟，是历史调用约定，不要改成秒。
    /// </para>
    /// </summary>
    public static class MediaParse
    {
        public const int MAX_SCREEN_SHOT_NUM = 30;

        public const int MIN_SCREEN_SHOT_NUM = 1;

        public const int DEFAULT_SCREEN_SHOT_NUM = 10;

        /// <summary>取视频时长，格式 <c>HH:mm:ss</c>；文件不存在或解析失败返回 null。</summary>
        public static string GetVideoDuration(string path)
        {
            if (!File.Exists(path))
                return null;

            try {
                using (MediaInfo mediaInfo = new MediaInfo()) {
                    mediaInfo.Open(path);
                    string duration = mediaInfo.Get(StreamKind.General, 0, "Duration/String3");
                    if (string.IsNullOrEmpty(duration))
                        return null;

                    return duration.Substring(0, duration.LastIndexOf('.'));
                }
            } catch (Exception ex) {
                Logger?.Error(ex.Message);
                return null;
            }
        }

        /// <summary>把 <c>HH:mm:ss</c> 转成秒；格式不对返回 0。</summary>
        public static double DurationToSecond(string duration)
        {
            if (string.IsNullOrEmpty(duration) || duration.Split(':').Count() < 3)
                return 0;

            string[] parts = duration.Split(':');
            return double.Parse(parts[0]) * 60 * 60 + double.Parse(parts[1]) * 60 + double.Parse(parts[2]);
        }

        /// <summary>把秒转成 <c>HH:mm:ss</c>；0 秒返回 <c>00:00:00</c>。</summary>
        public static string SecondToDuration(double second)
        {
            if (second == 0)
                return "00:00:00";

            TimeSpan timeSpan = TimeSpan.FromSeconds(second);
            return string.Concat(
                timeSpan.Hours.ToString().PadLeft(2, '0'), ":",
                timeSpan.Minutes.ToString().PadLeft(2, '0'), ":",
                timeSpan.Seconds.ToString().PadLeft(2, '0'));
        }

        /// <summary>扣掉要忽略的片头/片尾时长（参数单位：分钟）。</summary>
        public static double GetProperSecond(double second, double ignoreStart, double ignoreEnd)
        {
            double result = second;
            if (ignoreStart > 0)
                result -= ignoreStart * 60;

            if (ignoreEnd > 0)
                result -= ignoreEnd * 60;

            return result;
        }

        /// <summary>
        /// 计算截图时间点。
        /// </summary>
        /// <param name="screenShotNum">期望张数，超出 [1,30] 时按 10 张处理。</param>
        /// <param name="ignoreStart">忽略片头，单位分钟。</param>
        /// <param name="ignoreEnd">忽略片尾，单位分钟。</param>
        /// <returns>时间点数组；文件不可用时返回 null。</returns>
        public static string[] GetCutOffArray(string path, double screenShotNum, double ignoreStart, double ignoreEnd)
        {
            if (!File.Exists(path))
                return null;

            string duration = GetVideoDuration(path);
            if (string.IsNullOrEmpty(duration))
                return null;

            double totalSecond = DurationToSecond(duration);
            if (!(totalSecond > 0))
                return null;

            int num = (int)screenShotNum;
            if (num < MIN_SCREEN_SHOT_NUM || num > MAX_SCREEN_SHOT_NUM)
                num = DEFAULT_SCREEN_SHOT_NUM;

            string[] result = new string[num];
            double properSecond = GetProperSecond(totalSecond, ignoreStart, ignoreEnd);

            if (properSecond < result.Length) {
                // 有效时长比张数还短：一秒一张，不按均分
                if (!(properSecond > 0)) {
                    SplitVideo(ref result, totalSecond, num, 0);
                    return result;
                }

                result = new string[(int)properSecond];
                for (int i = 0; i < result.Length; i++)
                    result[i] = SecondToDuration(i);

                return result;
            }

            SplitVideo(ref result, properSecond, num, ignoreStart);
            return result;
        }

        /// <summary>按步长把时间点填进数组（步长取整到秒）。</summary>
        private static void SplitVideo(ref string[] arr, double second, int num, double ignoreStart)
        {
            uint step = (uint)(second / num);
            for (int i = 0; i < arr.Length; i++)
                arr[i] = SecondToDuration(ignoreStart * 60 + step * (ulong)i);
        }

        /// <summary>取视频宽高；文件不存在或解析失败返回 (0,0)。</summary>
        public static (double width, double height) GetWidthHeight(string path)
        {
            if (!File.Exists(path))
                return (0, 0);

            string width = "0";
            string height = "0";
            try {
                using (MediaInfo mediaInfo = new MediaInfo()) {
                    mediaInfo.Open(path);
                    width = mediaInfo.Get(StreamKind.Video, 0, "Width");
                    height = mediaInfo.Get(StreamKind.Video, 0, "Height");
                }
            } catch (Exception ex) {
                Logger?.Error(ex.Message);
            }

            int.TryParse(width, out int w);
            int.TryParse(height, out int h);
            return (w, h);
        }
    }
}
