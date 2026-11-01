using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Jvedio.Controls
{
    /// <summary>
    /// 应用内轻提示。
    /// <para>
    /// 替代闭源库的 <c>MessageNotify</c>：后者是独立窗口、固定配色，
    /// 与本项目的莫兰迪浅绿 + 磨砂玻璃语言不搭。
    /// 本类把提示做成主窗口内的浮层，颜色全部取自项目 Token。
    /// </para>
    /// <para>
    /// 设计约束：
    /// - 同一时刻只显示一条（新提示直接顶掉旧的），避免叠成一堆
    /// - 不接收鼠标事件，绝不挡住用户操作
    /// - 动画只做位移与透明度，不用 BlurEffect（大面积模糊会掉帧）
    /// </para>
    /// </summary>
    public partial class Toast : UserControl
    {
        /// <summary>当前显示的实例（全局唯一）</summary>
        private static Toast _current;

        /// <summary>自动关闭计时器</summary>
        private static DispatcherTimer _timer;

        /// <summary>提示类型，决定圆点颜色</summary>
        public enum Kind
        {
            Success,
            Error,
            Warning,
            Info
        }

        public Toast()
        {
            InitializeComponent();
        }

        public static void Success(string message) => Show(message, Kind.Success);

        public static void Error(string message) => Show(message, Kind.Error);

        public static void Warning(string message) => Show(message, Kind.Warning);

        public static void Info(string message) => Show(message, Kind.Info);

        /// <summary>
        /// 显示一条提示。
        /// </summary>
        /// <param name="message">提示内容</param>
        /// <param name="kind">类型，影响圆点颜色</param>
        /// <param name="durationMs">停留时长（毫秒）</param>
        public static void Show(string message, Kind kind = Kind.Info, int durationMs = 2600)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            // 必须回到 UI 线程：调用方可能来自后台任务（如局域网服务回调）
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;
            if (!dispatcher.CheckAccess()) {
                dispatcher.BeginInvoke(new Action(() => Show(message, kind, durationMs)));
                return;
            }

            var host = ResolveHost();
            if (host == null)
                return;

            // 已有一条在显示：复用同一个实例，避免每弹一次就新建一个控件
            if (_current == null || !host.Children.Contains(_current)) {
                _current = new Toast();
                host.Children.Add(_current);
            }

            _current.Apply(message, kind);
            _current.PlayIn();

            // 重置计时器
            if (_timer == null) {
                _timer = new DispatcherTimer();
                _timer.Tick += (s, e) => {
                    _timer.Stop();
                    _current?.PlayOut();
                };
            }
            _timer.Stop();
            _timer.Interval = TimeSpan.FromMilliseconds(durationMs);
            _timer.Start();
        }

        /// <summary>
        /// 找到用于挂载提示的容器：主窗口最外层的 Grid。
        /// <para>
        /// 挂最外层而不是内容区，是为了让提示浮在整个界面之上，
        /// 不受侧栏、标签页布局影响。
        /// </para>
        /// </summary>
        private static Panel ResolveHost()
        {
            var win = Application.Current?.MainWindow;
            if (win == null)
                return null;

            if (win.Content is Panel p)
                return p;

            // 内容不是 Panel 时，找第一个 Grid
            if (win.Content is DependencyObject content) {
                var grid = FindFirstGrid(content);
                if (grid != null)
                    return grid;
            }
            return null;
        }

        private static Panel FindFirstGrid(DependencyObject root)
        {
            if (root == null)
                return null;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++) {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Grid g)
                    return g;
                var nested = FindFirstGrid(child);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        /// <summary>设置内容与配色</summary>
        private void Apply(string message, Kind kind)
        {
            Message.Text = message;
            Dot.Background = ResolveBrush(kind);
        }

        /// <summary>
        /// 取类型对应的颜色。
        /// <para>
        /// 优先读项目 Token（跟随主题），取不到再退回硬编码色值，
        /// 保证即使 Token 被改名也不会出现「圆点消失」。
        /// </para>
        /// </summary>
        private Brush ResolveBrush(Kind kind)
        {
            string key;
            string fallback;
            switch (kind) {
                case Kind.Success:
                    key = "Semantic.Success"; fallback = "#FF5A8A6A"; break;
                case Kind.Error:
                    key = "Semantic.Error"; fallback = "#FFC0705F"; break;
                case Kind.Warning:
                    key = "Semantic.Warning"; fallback = "#FFC9A961"; break;
                default:
                    key = "Semantic.Accent"; fallback = "#FF87A48E"; break;
            }

            var brush = TryFindResource(key) as Brush;
            if (brush != null)
                return brush;

            try {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
            } catch {
                return Brushes.Gray;
            }
        }

        /// <summary>淡入 + 轻微下滑</summary>
        private void PlayIn()
        {
            Visibility = Visibility.Visible;

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fade);

            var slide = new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(220)) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Slide.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        /// <summary>淡出</summary>
        private void PlayOut()
        {
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200)) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (s, e) => { Visibility = Visibility.Collapsed; };
            BeginAnimation(OpacityProperty, fade);
        }
    }
}
