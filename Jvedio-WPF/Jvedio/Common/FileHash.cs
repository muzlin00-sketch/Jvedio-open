using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Jvedio.Common
{
    /// <summary>
    /// 文件内容哈希。取代 <c>SuperUtils.Security.Encrypt</c> 的哈希部分。
    /// <para>
    /// 原类型叫 <c>Encrypt</c>，但它做的是<b>哈希</b>不是加密，也没有密钥，
    /// 因此这里改成语义准确的名字。改名不影响落库结果——算法是逐字节复刻的。
    /// </para>
    /// <para>
    /// <b>这些结果已经写进用户数据库的 <c>metadata.Hash</c> 字段，算法不可再改。</b>
    /// 尤其是 <see cref="ComputeFast"/> 的抽样规则，看起来像 bug（只取三段 64 字节、
    /// 还把长度字符串拼进哈希），但它就是既有数据的生成方式；
    /// 改成"更正确"的整文件 MD5 会让存量记录的哈希全部对不上，
    /// 表现为重复文件检测失效、重新扫描时整库被判定为"文件已变更"。
    /// </para>
    /// </summary>
    public static class FileHash
    {
        /// <summary>小于等于该字节数的文件走完整 MD5，大于才抽样。</summary>
        public const int FAST_THRESHOLD_BYTES = 1024;

        /// <summary>抽样时每段的字节数，共取三段。</summary>
        public const int SAMPLE_SIZE = 64;

        /// <summary>完整文件的 MD5（小写 hex）；文件不存在返回空串。</summary>
        public static string Compute(string path)
        {
            try {
                if (!File.Exists(path))
                    return string.Empty;

                using (MD5 md5 = MD5.Create())
                using (FileStream stream = File.OpenRead(path)) {
                    return ToHex(md5.ComputeHash(stream));
                }
            } catch (Exception) {
                return string.Empty;
            }
        }

        /// <summary>
        /// 抽样 MD5，用于大视频文件——完整算一遍太慢。
        /// <para>
        /// 规则（照搬原实现，含其怪异之处）：
        /// 文件不超过 1024 字节时等同 <see cref="Compute"/>；
        /// 否则取头 64 字节、<c>length/2</c> 处 64 字节、<c>length-65</c> 处 64 字节，
        /// 用 <see cref="BitConverter.ToString(byte[])"/> 转成带横杠的大写十六进制串，
        /// <b>把文件长度字符串拼在最前面</b>，再对这个字符串取 MD5。
        /// </para>
        /// <para>
        /// 尾部之所以从 <c>length-65</c> 而不是 <c>length-64</c> 开始，
        /// 是原实现偏移一处写错（少算了一个字节），导致文件最后一字节永远不参与哈希。
        /// 保留该行为以维持既有哈希值。
        /// </para>
        /// </summary>
        public static string ComputeFast(string path)
        {
            try {
                if (!File.Exists(path))
                    return string.Empty;

                FileInfo info = new FileInfo(path);
                long length = info.Length;
                if (length <= FAST_THRESHOLD_BYTES)
                    return Compute(path);

                byte[] buffer = new byte[SAMPLE_SIZE * 3];
                using (FileStream stream = info.OpenRead()) {
                    byte[] head = new byte[SAMPLE_SIZE];
                    byte[] middle = new byte[SAMPLE_SIZE];
                    byte[] tail = new byte[SAMPLE_SIZE];

                    stream.Read(head, 0, SAMPLE_SIZE);

                    stream.Seek(length / 2, SeekOrigin.Begin);
                    stream.Read(middle, 0, SAMPLE_SIZE);

                    // length - SAMPLE_SIZE - 1：与原实现一致（末字节被排除）
                    stream.Seek(length - SAMPLE_SIZE - 1, SeekOrigin.Begin);
                    stream.Read(tail, 0, SAMPLE_SIZE);

                    head.CopyTo(buffer, 0);
                    middle.CopyTo(buffer, SAMPLE_SIZE);
                    tail.CopyTo(buffer, SAMPLE_SIZE * 2);
                }

                string content = length.ToString(CultureInfo.InvariantCulture)
                    + BitConverter.ToString(buffer);
                return ComputeText(content);
            } catch (Exception) {
                return string.Empty;
            }
        }

        /// <summary>
        /// 一批文件的合并哈希。
        /// <para>
        /// 规则照搬原实现：文件数 <b>少于 5 个</b>时全部参与；
        /// 达到 5 个后<b>只取 5 个样本</b>——第 0、1、<c>count/2</c>、<c>count-2</c>、<c>count-1</c> 个，
        /// 逐个取 <see cref="ComputeFast"/> 后把非空结果按顺序拼接，再整体取 MD5。
        /// </para>
        /// <para>
        /// 因此这个值与"目录里有哪些文件"只是弱相关：中间大量文件被忽略，
        /// 且<b>结果依赖传入顺序</b>。调用方必须先排序再传入，否则同一批文件会得到不同哈希。
        /// </para>
        /// <para>
        /// 空列表返回 <c>null</c>（不是空串），这是原实现的契约。
        /// </para>
        /// </summary>
        public static string ComputeForFiles(IList<string> paths)
        {
            if (paths == null || paths.Count == 0)
                return null;

            int count = paths.Count;
            string[] samples;

            if (count >= 5) {
                samples = new string[] {
                    paths[0],
                    paths[1],
                    paths[count / 2],
                    paths[count - 2],
                    paths[count - 1],
                };
            } else {
                samples = new string[count];
                for (int i = 0; i < count; i++)
                    samples[i] = paths[i];
            }

            try {
                StringBuilder builder = new StringBuilder();
                foreach (string path in samples) {
                    string one = ComputeFast(path);
                    if (string.IsNullOrEmpty(one))
                        continue;
                    builder.Append(one);
                }
                return ComputeText(builder.ToString());
            } catch (Exception) {
                return string.Empty;
            }
        }

        /// <summary>字符串的 MD5（小写 hex）；空串返回空串。</summary>
        public static string ComputeText(string content)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            using (MD5 md5 = MD5.Create())
                return ToHex(md5.ComputeHash(Encoding.UTF8.GetBytes(content)));
        }

        private static string ToHex(byte[] hash)
        {
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }
}
