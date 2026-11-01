using System;

namespace Jvedio.Common
{
    /// <summary>数值相关的杂项工具。</summary>
    public static class NumberHelper
    {
        /// <summary>
        /// 生成一个毫秒级的随机等待时间，永远落在 <c>[0, max)</c>。
        /// <para>
        /// <b>第一个参数不影响取值范围</b>，只是参与位移——这是照原实现（
        /// <c>SuperUtils.Maths.NumberHelper.GenerateRandomMS</c>）的行为复刻的：
        /// 内部等价于 <c>new Random().Next(-value, max - value) + value</c>。
        /// 所以调用方传进来的"基础延迟"实际上只被当成抖动下界的偏移量，
        /// 真正决定等待上限的是第二个参数。
        /// </para>
        /// <para>异常时返回 0（原实现如此，用于避免随机数异常把任务调度整个打断）。</para>
        /// </summary>
        public static int GenerateRandomMS(int value, int max)
        {
            int result = 0;
            try {
                result = new Random().Next(-value, max - value) + value;
            } catch (Exception) {
                return 0;
            }
            return result < 0 ? 0 : result;
        }
    }
}
