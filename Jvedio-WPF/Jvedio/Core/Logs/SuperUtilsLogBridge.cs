using Jvedio.Core.Logs;

namespace Jvedio.Core.Logs
{
    /// <summary>
    /// 临时桥接：把控件库内部的日志接进我们自己的日志文件。
    /// <para>
    /// <c>SuperControls.Style.Handler.LogHandler.Logger</c> 的属性类型是
    /// <c>SuperUtils.Framework.Logger.AbstractLogger</c>——闭源控件库（本次不在替换范围内）
    /// 自己依赖了闭源日志库，所以必须给它一个那个类型的实例。
    /// </para>
    /// <para>
    /// <b>本文件是 Jvedio 代码里唯一还碰 <c>SuperUtils</c> 日志类型的地方</b>，
    /// 目的是把这点耦合关进一个文件。等 <c>SuperControls.Style</c> 也被替换后，
    /// 删除本文件，并去掉 <c>App.xaml.cs</c> 里对 <c>LogHandler.Logger</c> 的赋值即可。
    /// </para>
    /// </summary>
    internal class SuperUtilsLogBridge : global::SuperUtils.Framework.Logger.AbstractLogger
    {
        private readonly AbstractLogger _target;

        public SuperUtilsLogBridge(AbstractLogger target)
        {
            _target = target;
            // 两套 Level 枚举取值完全一致（None=0/Error=1/Warn=2/Info=3/Debug=4），按序号映射即可
            LogLevel = (global::SuperUtils.Framework.Logger.AbstractLogger.Level)(int)target.LogLevel;
        }

        /// <summary>级别过滤已经由外部库自己做过，这里原样转交。</summary>
        public override void LogPrint(string str)
        {
            _target?.LogPrint(str);
        }
    }
}
