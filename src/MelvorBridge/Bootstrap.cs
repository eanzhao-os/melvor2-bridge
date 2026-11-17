using System.Runtime.CompilerServices;

namespace MelvorBridge;

/// <summary>
/// 模块初始化器：本 DLL 被 Assembly.LoadFrom 加载时自动执行。
/// 由主程序集注入的 &lt;Module&gt;.cctor 通过 LoadFrom 拉起，无需类型级引用。
/// </summary>
internal static class Bootstrap
{
    [ModuleInitializer]
    internal static void Init() => StartupHook.Initialize();
}
