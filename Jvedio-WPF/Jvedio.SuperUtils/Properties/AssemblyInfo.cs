using System.Reflection;

// 旧式 csproj（非 SDK 风格）不会把 <AssemblyVersion> 属性自动生成成程序集特性，
// 之前产物是 0.0.0.0。闭源的 CommonNet.dll 以及全部番号插件（bus / fc2 / library / db）
// 都引用 SuperUtils 1.2.0.0；弱名称程序集按简单名绑定、版本不一致目前也能加载，
// 但版本对齐后行为更可预期，也避免将来某个加载上下文做严格校验时突然炸掉。
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]
