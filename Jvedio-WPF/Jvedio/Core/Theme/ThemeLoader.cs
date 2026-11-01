using Jvedio.Core.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Jvedio.Core.Theme
{
    /// <summary>
    /// 用户可自定义的主题加载器。
    /// <para>
    /// 读取 <c>Data/theme.json</c>，把用户配置的颜色注入应用资源，
    /// 覆盖内置的莫兰迪默认值。
    /// </para>
    /// <para>
    /// 设计意图：让「换主题」不需要改代码或重新编译。
    /// 用户只改 JSON 里的色值，重启即生效。
    /// </para>
    /// <para>
    /// 容错策略：任何一项解析失败都退回内置默认值，
    /// 绝不让主题配置错误导致程序启动失败。
    /// </para>
    /// </summary>
    public static class ThemeLoader
    {
        /// <summary>主题配置文件相对路径（相对程序目录）</summary>
        public const string CONFIG_RELATIVE_PATH = @"Data\theme.json";

        /// <summary>
        /// 只允许注入这些 key。
        /// <para>
        /// 原因：若某个 key 被控件模板以 <c>StaticResource</c> 引用，
        /// 运行期替换会导致模板解析失败，整个窗口白屏（已实测）。
        /// 因此限定为项目中确认以 DynamicResource 引用的 key。
        /// </para>
        /// </summary>
        private static readonly HashSet<string> SafeKeys = new HashSet<string>(StringComparer.Ordinal) {
            "Window.Background",
            "Window.Title.Background",
            "Window.Title.Foreground",
            "Window.Title.Hover.Background",
            "Window.Side.Background",
            "Window.Side.Hover.Background",
            "Window.StatusBar.Background",
            "Window.StatusBar.Hover.Background",
            "Window.Foreground",
            "Window.Sub.Foreground",
            "Window.Detail.Background",
            "Window.Detail.Foreground",
            "Window.InnerDialog.Background",
            "Menu.Background",
            "Menu.Hover.Background",
            "ListBoxItem.Background",
            "ListBoxItem.Hover.Background",
            "ListBoxItem.Selected.Active.Background",
            "Button.Hover.Background",
            "TabItem.Background",
            "Common.HighLight.Background",
            "Common.HighLight.Deep.Background",
            "Control.Disabled.Background",
        };

        /// <summary>
        /// 加载并应用用户主题。返回实际应用的自定义项数量。
        /// </summary>
        public static int Apply()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_RELATIVE_PATH);
            if (!File.Exists(path)) {
                App.Logger?.Info($"未找到主题配置 {path}，使用内置默认配色");
                return 0;
            }

            JObject root;
            try {
                root = JObject.Parse(File.ReadAllText(path));
            } catch (Exception ex) {
                App.Logger?.Error($"主题配置解析失败，使用内置默认配色: {ex.Message}");
                return 0;
            }

            Dictionary<string, string> resolved = ResolveColors(root);

            int applied = 0;
            var failed = new List<string>();

            foreach (var pair in resolved) {
                if (string.IsNullOrWhiteSpace(pair.Value))
                    continue;

                if (!TryParseColor(pair.Value, out Color color)) {
                    failed.Add($"{pair.Key}={pair.Value}(无效)");
                    continue;
                }

                if (!TryMapToResourceKey(pair.Key, out string[] resourceKeys))
                    continue;

                foreach (string key in resourceKeys) {
                    if (!SafeKeys.Contains(key))
                        continue;
                    try {
                        Application.Current.Resources[key] = new SolidColorBrush(color);
                        applied++;
                    } catch (Exception ex) {
                        failed.Add($"{key}:{ex.GetType().Name}");
                    }
                }
            }

            ApplyMetrics(root);

            App.Logger?.Info($"主题已应用：{applied} 个颜色资源");
            if (failed.Count > 0)
                App.Logger?.Error($"主题项应用失败: {string.Join(", ", failed)}");

            return applied;
        }

        /// <summary>
        /// 解析最终色值：当前主题优先，其次预设，最后留空（由内置默认值兜底）。
        /// </summary>
        private static Dictionary<string, string> ResolveColors(JObject root)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 预设
            string presetName = root.Value<string>("预设");
            JObject presets = root["预设列表"] as JObject;
            JObject preset = null;

            if (presets != null && !string.IsNullOrEmpty(presetName)) {
                preset = presets[presetName] as JObject;
                if (preset == null)
                    App.Logger?.Error($"主题预设 [{presetName}] 不存在，退回内置默认值");
            }

            JObject current = root["当前主题"] as JObject;

            // 收集所有出现过的键
            var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (preset != null)
                foreach (var p in preset.Properties()) allKeys.Add(p.Name);
            if (current != null)
                foreach (var p in current.Properties()) allKeys.Add(p.Name);

            foreach (string key in allKeys) {
                string value = current?.Value<string>(key);
                if (string.IsNullOrWhiteSpace(value))
                    value = preset?.Value<string>(key);
                result[key] = value ?? string.Empty;
            }

            return result;
        }

        /// <summary>
        /// 把主题键映射到程序实际使用的资源 key（一个语义项可能对应多个闭源 key）。
        /// </summary>
        private static bool TryMapToResourceKey(string themeKey, out string[] resourceKeys)
        {
            switch (themeKey.ToLowerInvariant()) {
                case "bg.base":
                    resourceKeys = new[] { "Window.Background", "Background" };
                    return true;
                case "bg.subtle":
                    resourceKeys = new[] { "Window.Title.Background", "TabItem.Background" };
                    return true;
                case "bg.raised":
                    resourceKeys = new[] { "Window.InnerDialog.Background", "Window.Detail.Background", "Menu.Background", "ListBoxItem.Background" };
                    return true;
                case "fg.primary":
                    resourceKeys = new[] { "Window.Foreground", "Foreground", "Window.Title.Foreground" };
                    return true;
                case "fg.secondary":
                    resourceKeys = new[] { "Window.Sub.Foreground" };
                    return true;
                case "fg.muted":
                    resourceKeys = new[] { "Window.Detail.Foreground" };
                    return true;
                case "border":
                    resourceKeys = new[] { "Control.Disabled.Background" };
                    return true;
                case "accent":
                    resourceKeys = new[] { "Button.Selected.BorderBrush", "Button.Fill.Background", "CheckBox.Checked.Background", "ListBoxItem.Selected.Active.BorderBrush", "Common.HighLight.Deep.Background" };
                    return true;
                case "accent.soft":
                    resourceKeys = new[] { "ListBoxItem.Selected.Active.Background", "Common.HighLight.Background", "Common.HighLight.BorderBrush" };
                    return true;
                case "accent.wash":
                    resourceKeys = new[] { "Window.Title.Hover.Background", "Menu.Hover.Background", "ListBoxItem.Hover.Background", "Button.Hover.Background", "Window.StatusBar.Hover.Background" };
                    return true;
                case "sidebar":
                    resourceKeys = new[] { "Window.Side.Background", "Window.StatusBar.Background", "Window.Side.Hover.Background", "Menu.Hover.Deep.Background", "Window.Title.Hover.Deep.Background" };
                    return true;
                default:
                    resourceKeys = null;
                    return false;
            }
        }

        /// <summary>
        /// 应用界面尺寸类变量（圆角、阴影强度等）。
        /// <para>
        /// 只能替换以 <c>DynamicResource</c> 引用的资源。
        /// 目前项目里 Card.CornerRadius / Card.CornerRadius.Top / Radius.Sm 均为 DynamicResource，
        /// 因此可以安全覆盖。
        /// </para>
        /// </summary>
        private static void ApplyMetrics(JObject root)
        {
            JObject ui = root["界面"] as JObject;
            if (ui == null)
                return;

            try {
                // 卡片圆角：同时影响卡片主体与封面图的上两角
                double cardRadius = ui.Value<double?>("卡片圆角") ?? 0;
                if (cardRadius > 0) {
                    Application.Current.Resources["Card.CornerRadius"] = new CornerRadius(cardRadius);
                    Application.Current.Resources["Card.CornerRadius.Top"] =
                        new CornerRadius(cardRadius, cardRadius, 0, 0);
                    Application.Current.Resources["Card.CornerRadius.Bottom"] =
                        new CornerRadius(0, 0, cardRadius, cardRadius);
                }

                // 通用圆角：影响容器、按钮等
                double radius = ui.Value<double?>("圆角") ?? 0;
                if (radius > 0) {
                    Application.Current.Resources["Radius.Md"] = new CornerRadius(radius);
                    Application.Current.Resources["Radius.Lg"] = new CornerRadius(radius * 1.5);
                    Application.Current.Resources["Radius.Sm"] = new CornerRadius(Math.Max(1, radius / 2));
                }

                // 字号缩放：以 1.0 为基准整体缩放，避免逐个调 7 个档位
                double fontScale = ui.Value<double?>("字号缩放") ?? 0;
                if (fontScale > 0) {
                    ApplyFontScale(fontScale);
                }

                // 注意：不在此处替换 BlueDropShadowEffect。
                // 该 Effect 被大量卡片模板以 StaticResource 引用，
                // 运行期替换会导致模板无法重新解析，界面白屏。
                // 阴影强度已在 Effects.xaml 中静态定义，如需调整请改那里。
            } catch (Exception ex) {
                App.Logger?.Error($"应用界面尺寸变量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 字号整体缩放。
        /// <para>
        /// 提供「缩放」而不是逐个暴露 7 个档位，是因为用户真正想要的是
        /// 「整体大一点 / 小一点」，而不是精确控制某一档。
        /// 缩放能保证各档位的相对关系不变，不会破坏层级。
        /// </para>
        /// <para>
        /// 范围限制在 0.8–1.6：低于 0.8 正文不可读，高于 1.6 布局会溢出。
        /// </para>
        /// </summary>
        private static void ApplyFontScale(double scale)
        {
            scale = Math.Min(1.6, Math.Max(0.8, scale));

            var sizes = new Dictionary<string, double> {
                { "Font.Size.Caption", 11 },
                { "Font.Size.BodySm", 12 },
                { "Font.Size.Body", 13 },
                { "Font.Size.BodyMd", 14 },
                { "Font.Size.BodyLg", 15 },
                { "Font.Size.Title", 16 },
                { "Font.Size.Code", 15 },
            };

            foreach (var pair in sizes) {
                Application.Current.Resources[pair.Key] = pair.Value * scale;
            }

            App.Logger?.Info($"字号已缩放：{scale:0.00}x");
        }

        /// <summary>
        /// 解析 #RGB / #RRGGBB / #AARRGGBB 三种写法。
        /// </summary>
        private static bool TryParseColor(string text, out Color color)
        {
            color = Colors.Transparent;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string s = text.Trim();
            if (!s.StartsWith("#"))
                s = "#" + s;

            try {
                color = (Color)ColorConverter.ConvertFromString(s);
                return true;
            } catch {
                return false;
            }
        }
    }
}
