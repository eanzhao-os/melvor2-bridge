// MelvorBridge 注入器：给游戏主程序集的 <Module> 添加 .cctor，
// 程序集加载时自动执行 MelvorBridge.StartupHook.Initialize()。
// 用法:
//   Injector inject <gameDll> <bridgeDll>   备份+注入
//   Injector restore <gameDll>              还原备份
//   Injector status <gameDll>               检查注入状态
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

if (args.Length < 2)
{
    Console.WriteLine("usage: Injector <inject|restore|status> <gameDll> [bridgeDll]");
    return 2;
}

var cmd = args[0];
var gamePath = Path.GetFullPath(args[1]);
var backupPath = gamePath + ".orig";

switch (cmd)
{
    case "status":
    {
        using var mod = ModuleDefMD.Load(gamePath);
        Console.WriteLine($"module: {mod.Name}");
        Console.WriteLine($"injected: {IsInjected(mod)}");
        Console.WriteLine($"backup exists: {File.Exists(backupPath)}");
        break;
    }

    case "restore":
    {
        if (!File.Exists(backupPath)) { Console.WriteLine("no backup found"); return 1; }
        File.Copy(backupPath, gamePath, overwrite: true);
        RestoreDepsJson(Path.GetDirectoryName(gamePath)!);
        Console.WriteLine("restored original dll (+deps.json)");
        break;
    }

    case "inject":
    {
        if (args.Length < 3) { Console.WriteLine("inject needs <bridgeDll>"); return 2; }
        var bridgePath = Path.GetFullPath(args[2]);

        // 幂等：已注入则先还原
        using (var check = ModuleDefMD.Load(gamePath))
            if (IsInjected(check))
            {
                Console.WriteLine("already injected, nothing to do");
                return 0;
            }

        // 备份（仅首次）
        if (!File.Exists(backupPath))
        {
            File.Copy(gamePath, backupPath);
            Console.WriteLine($"backup -> {backupPath}");
        }

        using var bridge = ModuleDefMD.Load(File.ReadAllBytes(bridgePath));

        // 以字节数组加载，避免 dnlib 持有文件句柄导致无法写回
        using var game = ModuleDefMD.Load(File.ReadAllBytes(gamePath));

        // 桥 DLL 放到主 DLL 同目录 + 游戏根目录（cctor 用 AppDomain.BaseDirectory 定位后者）
        var dataDir = Path.GetDirectoryName(gamePath)!;
        var gameRoot = Path.GetFullPath(Path.Combine(dataDir, ".."));
        foreach (var dir in new[] { dataDir, gameRoot })
        {
            var dest = Path.Combine(dir, Path.GetFileName(bridgePath));
            File.Copy(bridgePath, dest, overwrite: true);
            Console.WriteLine($"bridge dll -> {dest}");
        }

        var moduleType = game.GlobalType ?? CreateModuleType(game);

        var cctor = moduleType.FindStaticConstructor();
        if (cctor is null)
        {
            cctor = new MethodDefUser(
                ".cctor",
                MethodSig.CreateStatic(game.CorLibTypes.Void),
                dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
                dnlib.DotNet.MethodAttributes.Private | dnlib.DotNet.MethodAttributes.Static
                | dnlib.DotNet.MethodAttributes.SpecialName | dnlib.DotNet.MethodAttributes.RTSpecialName)
            { Body = new CilBody() };
            moduleType.Methods.Add(cctor);
        }

        // <Module>::.cctor（自包含，仅引用 BCL，永不因桥缺失而 JIT 失败）：
        //   try { Assembly.LoadFrom(Path.Combine(AppDomain.BaseDirectory, "MelvorBridge.dll")); }
        //   catch { }
        // 直接引用方案：桥注册进 deps.json(TPA) 后，cctor 可直接 MemberRef 调用桥入口
        //   <Module>::.cctor -> call void [MelvorBridge]MelvorBridge.StartupHook::Initialize()
        var initMethod = bridge.Find("MelvorBridge.StartupHook", false)?
            .FindMethod("Initialize")
            ?? throw new InvalidOperationException("MelvorBridge.StartupHook.Initialize not found");
        var hookMethod = bridge.Find("MelvorBridge.StartupHook", false)?
            .FindMethod("OnNativeObjectCreated")
            ?? throw new InvalidOperationException("MelvorBridge.StartupHook.OnNativeObjectCreated not found");
        var importer = new Importer(game, ImporterOptions.TryToUseDefs);
        var body = cctor.Body ??= new CilBody();
        body.Instructions.Clear();
        body.ExceptionHandlers.Clear();
        body.Instructions.Add(OpCodes.Call.ToInstruction(importer.Import(initMethod)));
        body.Instructions.Add(OpCodes.Ret.ToInstruction());

        // 核心钩子：GodotObject 构造函数（必经、永远在 Godot 主线程）
        var godotObject = game.Find("Godot.GodotObject", false)
            ?? throw new InvalidOperationException("Godot.GodotObject not found in target");
        var importedHook = importer.Import(hookMethod);
        int patched = 0;
        foreach (var ctor in godotObject.FindConstructors())
        {
            if (ctor?.Body == null) continue;
            ctor.Body.Instructions.Insert(0, OpCodes.Call.ToInstruction(importedHook));
            patched++;
        }
        Console.WriteLine($"hooked GodotObject ctors: {patched}");

        PatchDepsJson(dataDir);

        var opts = new ModuleWriterOptions(game);
        opts.Logger = DummyLogger.NoThrowInstance;
        var tmpPath = gamePath + ".injected.tmp";
        game.Write(tmpPath, opts);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using (var src = File.OpenRead(tmpPath))
                using (var dst = new FileStream(gamePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    src.CopyTo(dst);
                break;
            }
            catch (IOException) when (attempt < 10)
            {
                Console.WriteLine($"target dll busy, retry {attempt}...");
                Thread.Sleep(1500);
            }
        }
        File.Delete(tmpPath);
        Console.WriteLine("injected module initializer into " + Path.GetFileName(gamePath));
        break;
    }

    default:
        Console.WriteLine("unknown command");
        return 2;
}

return 0;

static TypeDefUser CreateModuleType(ModuleDef module)
{
    var t = new TypeDefUser("", "<Module>", module.CorLibTypes.Object.TypeDefOrRef);
    module.Types.Add(t);
    return t;
}

static void PatchDepsJson(string dataDir)
{
    var depsPath = Path.Combine(dataDir, "GamesByMalcs.Melvor2.deps.json");
    var depsBackup = depsPath + ".orig";
    if (!File.Exists(depsBackup)) File.Copy(depsPath, depsBackup);

    var json = File.ReadAllText(depsPath);
    var doc = System.Text.Json.JsonDocument.Parse(json);
    string tmpPath;
    {
        tmpPath = depsPath + ".tmp";
        using var tmpStream = File.Create(tmpPath);
        using var writer = new System.Text.Json.Utf8JsonWriter(tmpStream, new System.Text.Json.JsonWriterOptions { Indented = true });
    void CopyValue(System.Text.Json.JsonElement el)
    {
        switch (el.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var p in el.EnumerateObject()) { writer.WritePropertyName(p.Name); CopyValue(p.Value); }
                writer.WriteEndObject(); break;
            case System.Text.Json.JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var v in el.EnumerateArray()) CopyValue(v);
                writer.WriteEndArray(); break;
            default: el.WriteTo(writer); break;
        }
    }
    writer.WriteStartObject();
    foreach (var prop in doc.RootElement.EnumerateObject())
    {
        writer.WritePropertyName(prop.Name);
        var el = prop.Value;
        if (prop.Name == "targets")
        {
            writer.WriteStartObject();
            foreach (var tfm in el.EnumerateObject())
            {
                writer.WritePropertyName(tfm.Name);
                writer.WriteStartObject();
                foreach (var pkg in tfm.Value.EnumerateObject())
                {
                    writer.WritePropertyName(pkg.Name); CopyValue(pkg.Value);
                }
                // TPA 条目：让 hostfxr 把 MelvorBridge.dll 纳入信任程序集列表
                writer.WritePropertyName("MelvorBridge/1.0.0");
                writer.WriteStartObject();
                writer.WritePropertyName("runtime");
                writer.WriteStartObject();
                writer.WritePropertyName("MelvorBridge.dll");
                writer.WriteStartObject(); writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        else if (prop.Name == "libraries")
        {
            writer.WriteStartObject();
            foreach (var lib in el.EnumerateObject())
            {
                writer.WritePropertyName(lib.Name); CopyValue(lib.Value);
            }
            writer.WritePropertyName("MelvorBridge/1.0.0");
            writer.WriteStartObject();
            writer.WriteString("type", "project");
            writer.WriteBoolean("serviceable", false);
            writer.WriteString("sha512", "");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        else CopyValue(el);
    }
        writer.WriteEndObject();
    } // writer + tmpStream disposed here
    for (int attempt = 1; ; attempt++)
    {
        try
        {
            File.Copy(tmpPath, depsPath, overwrite: true);
            File.Delete(tmpPath);
            break;
        }
        catch (IOException) when (attempt < 10)
        {
            Console.WriteLine($"deps.json busy, retry {attempt}...");
            Thread.Sleep(1500);
        }
    }
    Console.WriteLine("patched deps.json (MelvorBridge -> TPA)");
}

static void RestoreDepsJson(string dataDir)
{
    var depsPath = Path.Combine(dataDir, "GamesByMalcs.Melvor2.deps.json");
    var depsBackup = depsPath + ".orig";
    if (File.Exists(depsBackup)) { File.Copy(depsBackup, depsPath, overwrite: true); Console.WriteLine("restored deps.json"); }
}

static bool IsInjected(ModuleDef mod)
    => mod.GetTypeRefs().Any(r => r.Name == "StartupHook" && r.ResolutionScope is AssemblyRef ar && ar.Name == "MelvorBridge");
