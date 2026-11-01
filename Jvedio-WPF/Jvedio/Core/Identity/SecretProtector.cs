using System;
using System.Security.Cryptography;
using System.Text;

namespace Jvedio.Identity
{
    /// <summary>
    /// 本机敏感串（目前用于代理口令）的加解密。
    /// <para>
    /// 取代原闭源 <c>JvedioLib.dll</c> 的 <c>Encrypt.AesEncrypt/AesDecrypt</c>。
    /// 那套实现把 AES 密钥硬编码在混淆过的程序集里，密钥与程序同生共死——
    /// 拿到程序就等于拿到密钥，加解密只是让明文不以可读形式落盘。
    /// </para>
    /// <para>
    /// 这里改用 Windows DPAPI（当前用户作用域）：密钥由系统按用户托管，
    /// 程序二进制里没有任何密钥，拷走程序也解不开别处的密文。
    /// 代价是密文不能跨用户/跨机器迁移——对本机保存的代理口令来说这是正确取舍。
    /// </para>
    /// </summary>
    public static class SecretProtector
    {
        /// <summary>
        /// 附加熵。它不是密钥，只是让同一用户下"别的程序用 DPAPI 加密同名口令"也无法互换。
        /// </summary>
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Jvedio.ProxySecret.v1");

        /// <summary>
        /// 加密为 Base64 字符串。空输入返回空串，不返回 null，调用方可直接落库。
        /// </summary>
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain))
                return string.Empty;

            try {
                byte[] encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            } catch (Exception) {
                // 加密失败时返回空串而不是抛出：代理口令丢了用户可以重填，
                // 但设置页不能因为保存一个可选字段就整页崩掉。
                return string.Empty;
            }
        }

        /// <summary>
        /// 解密。解不开时返回空串——包括旧版用闭源 DLL 加密的存量值。
        /// </summary>
        public static string Unprotect(string cipher)
        {
            if (string.IsNullOrEmpty(cipher))
                return string.Empty;

            try {
                byte[] decrypted = ProtectedData.Unprotect(
                    Convert.FromBase64String(cipher), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            } catch (Exception) {
                // 旧版密文（闭源 DLL 的 AES）在这里必然失败，属预期内的迁移成本：
                // 用户重新输入一次代理口令即可，不需要为它保留一条闭源依赖。
                return string.Empty;
            }
        }
    }
}
