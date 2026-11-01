using Jvedio.Entity.CommonSQL;
using Newtonsoft.Json;
using Jvedio.Common;
using Jvedio.Core.ORM.Enums;
using Jvedio.Core.ORM.Wrapper;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using static Jvedio.App;

namespace Jvedio.Core.Config.Base
{
    /// <summary>
    ///
    /// </summary>
    public abstract class AbstractConfig : IConfig, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void RaisePropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }


        protected string ConfigName { get; set; }

        /// <summary>
        ///
        /// </summary>
        /// <param name="configName"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public AbstractConfig(string configName)
        {
            // configName 不为 null
            if (string.IsNullOrEmpty(configName))
                throw new ArgumentNullException("AbstractConfig.ConfigName");
            ConfigName = configName;
        }

        public virtual void Read()
        {
            SelectWrapper<AppConfig> wrapper = new SelectWrapper<AppConfig>();
            wrapper.Eq("ConfigName", ConfigName);
            AppConfig appConfig = MapperManager.appConfigMapper.SelectOne(wrapper);
            Logger.Info($"read config: {ConfigName}");
            if (appConfig == null || appConfig.ConfigId == 0)
                return;
            Dictionary<string, object> dict = Jvedio.Common.JsonUtils.TryDeserializeObject<Dictionary<string, object>>(appConfig.ConfigValue);
            if (dict == null)
                return;
            Type type = this.GetType();
            foreach (string key in dict.Keys) {
                if (string.IsNullOrEmpty(key))
                    continue;
                object value = dict[key];
                var prop = type.GetProperty(key);
                if (prop == null || value == null)
                    continue;
                try {
                    object converted = ConvertForProperty(value, prop.PropertyType);
                    prop.SetValue(this, converted, null);
                } catch {
                    // 单项失败不影响其它配置项
                    continue;
                }
            }
        }

        /// <summary>
        /// 把 JSON 反序列化得到的值转换成属性需要的类型。
        /// <para>
        /// 必须做这一步：JSON 里的数字统一被读成 <see cref="long"/>，
        /// 直接反射赋给 <c>int</c> / <c>float</c> 属性会抛
        /// ArgumentException，而外层是「失败就跳过」的容错写法——
        /// 于是这些配置项**静默地永远保持构造函数里的默认值**。
        /// 曾经的表现为：改了端口、改了缓存过期时间，重启后依然按默认值跑，
        /// 且日志里没有任何线索。
        /// </para>
        /// </summary>
        private static object ConvertForProperty(object value, Type propertyType)
        {
            Type target = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (target.IsInstanceOfType(value))
                return value;

            // 字符串写进枚举、数字写进枚举
            if (target.IsEnum) {
                if (value is string)
                    return Enum.Parse(target, (string)value, true);
                return Enum.ToObject(target, Convert.ToInt64(value));
            }

            // 字符串写进基础类型（老配置文件里可能存的是 "5200"）
            if (value is string && target != typeof(string)) {
                string text = ((string)value).Trim();
                if (text.Length == 0)
                    return null;
                if (target == typeof(bool))
                    return bool.Parse(text);
            }

            return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }

        public virtual void Save()
        {
            System.Reflection.PropertyInfo[] propertyInfos = this.GetType().GetProperties();
            if (propertyInfos == null)
                return;
            Dictionary<string, object> dictionary = propertyInfos.ToList().ToDictionary(x => x.Name, x => x.GetValue(this));
            AppConfig appConfig = new AppConfig();
            appConfig.ConfigName = ConfigName;
            appConfig.ConfigValue = JsonConvert.SerializeObject(dictionary);        // 不为 null
            MapperManager.appConfigMapper.Insert(appConfig, InsertMode.Replace);
            Logger.Info($"save config: {ConfigName}");
        }
    }
}
