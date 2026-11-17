using System.Reflection;

namespace MelvorBridge;

/// <summary>
/// 定位并持有游戏对象图：SceneTree → Main → GameDriver →(反射)→ Game。
/// 早期等待全程纯反射；确认 Godot 就绪之后才允许强类型调用（见 Server）。
/// GameDriver 在切换存档/重进游戏时会 Bind 新的 Game，因此每次请求都重新解析。
/// </summary>
public static class GameContext
{
    private static volatile bool _godotUp;
    private static volatile bool _connected;
    private static volatile bool _connecting;
    private static object? _gameDriver;
    private static PropertyInfo? _gameProp;

    public static bool Ready => _gameDriver != null;

    public static void Run()
    {
        // HTTP 服务立即上线（/health 可查连接状态）。
        // 游戏连接由 OnNativeObjectCreated 钩子驱动（注入到 GodotObject ctor，主线程）。
        Log.Write("bridge thread up (native hook will drive connection)");
        Server.Start();
    }

    /// <summary>
    /// 由注入到 GodotObject..ctor 的回调调用——永远在 Godot 主线程。
    /// 每次创建 Godot 对象时尝试连接，连上后短路。
    /// </summary>
    public static void OnNativeObjectCreated()
    {
        if (_connected) return;
        if (_connecting) return;
        _connecting = true;
        try
        {
            if (TryConnect())
            {
                _connected = true;
                Log.Write("connected to game (from native object ctor hook).");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"connect attempt: {ex.Message}");
        }
        finally { _connecting = false; }
    }

    private static bool TryConnect()
    {
        var sceneTree = Godot.Engine.GetMainLoop() as Godot.SceneTree;
        var root = sceneTree?.Root;
        if (root == null) { Log.Write("connect: no root"); return false; }

        var mainNode = FindNodeByTypeFullName(root, "GamesByMalcs.Melvor2.Main");
        if (mainNode == null)
        {
            if (!_treeDumped)
            {
                _treeDumped = true;
                Log.Write("scene tree dump:\n" + DumpTree(root, 0, 4));
            }
            return false;
        }

        var gdProp = mainNode.GetType().GetProperty("GameDriver", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (gdProp == null) { Log.Write("connect: no GameDriver prop"); return false; }
        var gd = gdProp.GetValue(mainNode);
        if (gd == null) { Log.Write("connect: GameDriver null"); return false; }

        var gameProp = gd.GetType().GetProperty("Game", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (gameProp == null) { Log.Write("connect: no Game prop"); return false; }
        var game = gameProp.GetValue(gd);
        if (game == null) { Log.Write("connect: game not bound yet (menu?)"); return false; }

        _gameDriver = gd;
        _gameProp = gameProp;
        return true;
    }

    private static bool _treeDumped;

    /// <summary>用全部参数默认值调用实例方法（Godot API 大量使用默认参数）。</summary>
    private static object? InvokeWithDefaults(MethodInfo m, object? target)
    {
        var args = m.GetParameters()
            .Select(p => p.DefaultValue is DBNull ? (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null) : p.DefaultValue)
            .ToArray();
        return m.Invoke(target, args);
    }

    private static string DumpTree(Godot.Node node, int depth, int maxDepth)
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            sb.AppendLine($"{new string(' ', depth * 2)}{node.Name} [{node.GetType().FullName}]");
            if (depth >= maxDepth) { sb.AppendLine($"{new string(' ', (depth + 1) * 2)}..."); return sb.ToString(); }
            int n = node.GetChildCount();
            for (int i = 0; i < n && i < 40; i++)
            {
                var child = node.GetChild(i);
                if (child != null) sb.Append(DumpTree(child, depth + 1, maxDepth));
            }
        }
        catch (Exception ex) { sb.AppendLine($"{new string(' ', depth * 2)}<error {ex.Message}>"); }
        return sb.ToString();
    }

    private static Godot.Node? FindNodeByTypeFullName(Godot.Node node, string fullName)
    {
        if (node.GetType().FullName == fullName) return node;
        int n = node.GetChildCount();
        for (int i = 0; i < n; i++)
        {
            var child = node.GetChild(i);
            if (child == null) continue;
            var found = FindNodeByTypeFullName(child, fullName);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>当前 Game 实例（可能为 null：玩家退回主菜单时）。</summary>
    public static object? GetGame()
    {
        EnsureConnected();
        var gd = _gameDriver;
        var prop = _gameProp;
        if (gd == null || prop == null) return null;
        try { return prop.GetValue(gd); } catch { return null; }
    }

    /// <summary>
    /// 未连接时主动在主线程重试一次（RunOnMainThread 的 CallDeferred 任何线程可安全调用）。
    /// ctor 钩子在界面稳定后触发变少，这里补上轮询兜底。
    /// </summary>
    public static void EnsureConnected()
    {
        if (_connected) return;
        try
        {
            RunOnMainThread(() =>
            {
                if (_connected) return 0;
                if (TryConnect())
                {
                    _connected = true;
                    Log.Write("connected to game (ensure path).");
                }
                return 0;
            }, timeoutMs: 3000);
        }
        catch { /* 主线程忙或未就绪，下次再试 */ }
    }

    public static object? GetActionManager(object? game = null)
    {
        game ??= GetGame();
        return game?.GetType().GetProperty("ActionManager")?.GetValue(game);
    }

    public static object? GetEntities(object? game = null)
    {
        game ??= GetGame();
        return game?.GetType().GetProperty("Entities")?.GetValue(game);
    }

    /// <summary>
    /// 把操作投递到 Godot 主线程执行（Callable.From(action).CallDeferred()）。
    /// Godot 的 call_deferred 队列线程安全。同步等待结果，超时抛 TimeoutException。
    /// 注意：本方法只在游戏运行期间（GodotSharp 已加载）被 HTTP handler 调用。
    /// </summary>
    public static T? RunOnMainThread<T>(Func<T> fn, int timeoutMs = 15000)
    {
        T? result = default;
        var done = new ManualResetEventSlim(false);
        Exception? err = null;

        Godot.Callable.From((Action)(() =>
        {
            try { result = fn(); }
            catch (Exception ex) { err = ex; Log.Write($"main-thread action failed: {ex}"); }
            finally { done.Set(); }
        })).CallDeferred();

        if (!done.Wait(timeoutMs))
            throw new TimeoutException($"main thread did not process within {timeoutMs}ms");
        if (err != null) throw err;
        return result;
    }

    public static void RunOnMainThread(Action fn, int timeoutMs = 15000) =>
        RunOnMainThread<object?>(() => { fn(); return null; }, timeoutMs);
}
