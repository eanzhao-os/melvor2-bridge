using System.Reflection;

namespace MelvorBridge;

/// <summary>
/// DOTNET_STARTUP_HOOKS 入口。
/// 注意：Initialize 运行于 Godot native 初始化之前，严禁在此触碰任何 Godot 类型，
/// 否则 GodotSharp 的静态构造会在 native 未就绪时崩溃。只启动后台线程。
/// </summary>
public static class StartupHook
{
    private static int _started;

    public static void Initialize()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
        try
        {
            var t = new Thread(GameContext.Run)
            {
                Name = "MelvorBridge",
                IsBackground = true,
            };
            t.Start();
        }
        catch (Exception ex)
        {
            try { Log.Write($"Initialize failed: {ex}"); } catch { }
        }
    }

    /// <summary>由 GodotObject 构造函数钩子调用（主线程）。见 Injector。</summary>
    public static void OnNativeObjectCreated() => GameContext.OnNativeObjectCreated();
}

/// <summary>极简文件日志，写到游戏日志目录旁，便于排障。</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    private static string Path_
    {
        get
        {
            if (_path == null)
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _path = System.IO.Path.Combine(appData, "Games By Malcs", "Melvor Idle 2", "logs", "bridge.log");
            }
            return _path;
        }
    }

    public static void Write(string msg)
    {
        lock (Gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
                File.AppendAllText(Path_, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            }
            catch { }
        }
    }

    public static string[] Tail(int n)
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(Path_)) return Array.Empty<string>();
                return File.ReadAllLines(Path_)[^Math.Min(n, 1000)..];
            }
            catch { return Array.Empty<string>(); }
        }
    }
}
