using System.Collections.Generic;
using System.Reflection;

namespace AlicizaX.UI.Runtime
{
    /// <summary>首次查询未注册的 UI 或 Holder 时，调用其程序集的生成注册入口。</summary>
    internal static class UIGeneratedRegistryLoader
    {
        private static readonly HashSet<Assembly> _processedAssemblies = new();

        internal static void EnsureRegistered(Assembly assembly)
        {
            if (_processedAssemblies.Contains(assembly)) return;

            var registry = assembly.GetType("AlicizaX.UI.Runtime.UIGeneratedRegistry");
            registry?.GetMethod("RegisterAll", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.Invoke(null, null);

            _processedAssemblies.Add(assembly);
        }
    }
}
