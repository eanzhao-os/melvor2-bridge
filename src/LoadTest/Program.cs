using System.Reflection;
var path = args[0];
AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
    var name = new AssemblyName(e.Name).Name + ".dll";
    var cand = Path.Combine(args[1], name);
    return File.Exists(cand) ? Assembly.LoadFrom(cand) : null;
};
try {
    var asm = Assembly.LoadFrom(path);
    Console.WriteLine($"loaded: {asm.FullName}");
    var types = asm.GetTypes();
    Console.WriteLine($"GetTypes OK: {types.Length} types");
    // 触摸一个类型强制 JIT（会先跑 module cctor）
    var t = types.FirstOrDefault(x => x.Name == "Main");
    Console.WriteLine($"Main type found: {t != null}");
    var m = t?.GetMethod("OnNotification", BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
    Console.WriteLine("done");
} catch (Exception ex) {
    Console.WriteLine($"FAILED: {ex.GetType().Name}: {ex.Message}");
    if (ex.InnerException != null) Console.WriteLine($"  inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
    Environment.Exit(1);
}
