using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

/// <summary>
/// Child-process probe invoked via DOTNET_STARTUP_HOOKS by ImageCacheTests to run MS2Godot.dll's module initializer
/// without Godot's native callbacks (as happens in an exported build before InitializeFromGameProject wires up
/// NativeFuncs). Does nothing unless MS2_TEST_MODULE_INIT is set.
/// </summary>
internal static class StartupHook
{
    sealed class IsolatedLoadContext : AssemblyLoadContext
    {
        readonly AssemblyDependencyResolver resolver;
        public IsolatedLoadContext(string path) : base(isCollectible: false) => resolver = new(path);
        protected override Assembly Load(AssemblyName name) =>
            resolver.ResolveAssemblyToPath(name) is { } p ? LoadFromAssemblyPath(p) : null;
    }

    public static void Initialize()
    {
        var path = Environment.GetEnvironmentVariable("MS2_TEST_MODULE_INIT");
        if (string.IsNullOrEmpty(path)) return;
        var mode = Environment.GetEnvironmentVariable("MS2_TEST_MODULE_INIT_MODE") ?? "isolated";
        Assembly asm;
        AssemblyLoadContext alc;
        if (mode == "default")
        {
            var resolver = new AssemblyDependencyResolver(path);
            alc = AssemblyLoadContext.Default;
            alc.Resolving += (_, name) => resolver.ResolveAssemblyToPath(name) is { } p ? alc.LoadFromAssemblyPath(p) : null;
            asm = alc.LoadFromAssemblyPath(path);
        }
        else
        {
            alc = new IsolatedLoadContext(path);
            asm = alc.LoadFromAssemblyPath(path);
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        RuntimeHelpers.RunModuleConstructor(asm.ManifestModule.ModuleHandle);
        sw.Stop();
        // Also run the static constructors of the types touched by the module initializer: beforefieldinit lets the
        // runtime execute them eagerly (e.g. under ReadyToRun), so none may call into Godot before NativeFuncs is up.
        RuntimeHelpers.RunClassConstructor((asm.GetType("Ms2Godot.DataCache") ?? throw new InvalidOperationException("missing DataCache")).TypeHandle);
        RuntimeHelpers.RunClassConstructor((asm.GetType("Ms2Godot.UiCode") ?? throw new InvalidOperationException("missing UiCode")).TypeHandle);
        var precompiled = asm.GetType("Ms2Godot.Precompiled") ?? throw new InvalidOperationException("missing Precompiled");
        var asmDir = (string)precompiled.GetProperty("AssemblyDir")!.GetValue(null);
        var expectedDir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.Equals(asmDir, expectedDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"AssemblyDir '{asmDir}' != '{expectedDir}'");
        var summary = (string)precompiled.GetProperty("Summary")!.GetValue(null) ?? "";
        if (!summary.Contains(" precompiled in place from ", StringComparison.Ordinal) || !summary.EndsWith(" ms", StringComparison.Ordinal))
            throw new InvalidOperationException($"unexpected Summary '{summary}'");
        var formats = alc.LoadFromAssemblyName(new AssemblyName("Ms2.Formats"));
        var cacheStore = formats.GetType("Ms2.Formats.Cache.CacheStore") ?? throw new InvalidOperationException("missing CacheStore");
        if (cacheStore.GetField("Of")!.GetValue(null) == null)
            throw new InvalidOperationException("DataCache.Init did not register CacheStore.Of");
        Console.WriteLine($"MODULE_INIT_OK ({sw.Elapsed.TotalMilliseconds:F1} ms): {summary}");
        Environment.Exit(0);
    }
}
