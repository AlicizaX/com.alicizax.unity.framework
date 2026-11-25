using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AlicizaX
{
    public static partial class Utility
    {
        /// <summary>
        /// 程序集相关的实用函数。
        /// </summary>
        public static class Assembly
        {
            private static readonly System.Reflection.Assembly[] s_Assemblies = null;
            private static readonly Dictionary<string, Type> s_CachedTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
            private static readonly ConcurrentDictionary<System.Reflection.Assembly, byte> s_ReportedTypeLoadFailures = new ConcurrentDictionary<System.Reflection.Assembly, byte>();

            static Assembly()
            {
                s_Assemblies = AppDomain.CurrentDomain.GetAssemblies();
            }

            /// <summary>
            /// 获取已加载的程序集。
            /// </summary>
            /// <returns>已加载的程序集。</returns>
            public static System.Reflection.Assembly[] GetAssemblies()
            {
                return s_Assemblies;
            }

            /// <summary>
            /// 获取已加载的程序集中的可加载类型。
            /// </summary>
            /// <returns>已加载的程序集中的可加载类型。</returns>
            public static Type[] GetTypes()
            {
                List<Type> results = new List<Type>();
                foreach (System.Reflection.Assembly assembly in s_Assemblies)
                {
                    if(assembly.FullName.Contains("Sirenix")) continue;
                    results.AddRange(GetLoadableTypes(assembly));
                }

                return results.ToArray();
            }

            /// <summary>
            /// 获取已加载的程序集中的可加载类型。
            /// </summary>
            /// <param name="results">存放可加载类型的列表。</param>
            public static void GetTypes(List<Type> results)
            {
                if (results == null)
                {
                    throw new GameFrameworkException("Results is invalid.");
                }

                results.Clear();
                foreach (System.Reflection.Assembly assembly in s_Assemblies)
                {
                    results.AddRange(GetLoadableTypes(assembly));
                }
            }

            private static IEnumerable<Type> GetLoadableTypes(System.Reflection.Assembly assembly)
            {
                try
                {
                    return assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    if (s_ReportedTypeLoadFailures.TryAdd(assembly, 0))
                    {
                        string details = string.Join(Environment.NewLine, exception.LoaderExceptions
                            .Where(error => error != null)
                            .Select(error => error.Message)
                            .Distinct());
                        Log.Warning($"Could not load some types from '{assembly.FullName}'.{Environment.NewLine}{details}");
                    }

                    return exception.Types.Where(type => type != null);
                }
            }

            /// <summary>
            /// 获取已加载的程序集中的指定类型。
            /// </summary>
            /// <param name="typeName">要获取的类型名。</param>
            /// <returns>已加载的程序集中的指定类型。</returns>
            public static Type GetType(string typeName)
            {
                if (string.IsNullOrEmpty(typeName))
                {
                    throw new GameFrameworkException("Type name is invalid.");
                }

                Type type = null;
                if (s_CachedTypes.TryGetValue(typeName, out type))
                {
                    return type;
                }

                type = Type.GetType(typeName);
                if (type != null)
                {
                    s_CachedTypes.Add(typeName, type);
                    return type;
                }

                foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(typeName);
                    if (type != null)
                    {
                        s_CachedTypes.Add(typeName, type);
                        return type;
                    }
                }

                return null;
            }

            /// <summary>
            /// 获取已加载的程序集中的指定类型的子类列表。
            /// </summary>
            /// <param name="type">指定类型</param>
            /// <returns></returns>
            public static List<string> GetRuntimeTypeNames(Type type)
            {
                var types = GetTypes();
                List<string> results = new List<string>();
                foreach (var t in types)
                {
                    if (t.IsAbstract || !t.IsClass)
                    {
                        continue;
                    }

                    if (t.IsSubclassOf(type) || t.IsImplWithInterface(type))
                    {
                        results.Add(t.FullName);
                    }
                }

                return results;
            }

            public static List<Type> GetRuntimeTypes(Type typeBase)
            {
                var types = GetTypes();
                List<Type> results = new List<Type>();
                foreach (var t in types)
                {
                    if (t.IsAbstract || !t.IsClass)
                    {
                        continue;
                    }

                    if (t.IsSubclassOf(typeBase) || t.IsImplWithInterface(typeBase))
                    {
                        results.Add(t);
                    }
                }

                return results;
            }
        }
    }
}
