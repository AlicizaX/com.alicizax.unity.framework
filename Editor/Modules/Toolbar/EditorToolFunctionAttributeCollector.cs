using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace AlicizaX.Editor.Toolbar
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class EditorToolFunctionAttribute : Attribute
    {
        public string ToolMenuPath { get; private set; }
        public int MenuOrder { get; private set; }
        public MethodInfo MethodInfo { get; private set; }

        public EditorToolFunctionAttribute(string menu, int menuOrder = 0)
        {
            this.ToolMenuPath = menu;
            MenuOrder = menuOrder;
        }

        public void SetMethodInfo(MethodInfo methodInfo)
        {
            MethodInfo = methodInfo;
        }
    }

    internal static class EditorToolFunctionAttributeCollector
    {
        public static readonly List<EditorToolFunctionAttribute> Attributes = new List<EditorToolFunctionAttribute>();

        [InitializeOnLoadMethod]
        static void ScanAndRegisterAllMethods()
        {
            Attributes.Clear();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.FullName.IndexOf("Sirenix", StringComparison.Ordinal) >= 0)
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types.Where(type => type != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var method in methods)
                    {
                        var attribute = method.GetCustomAttribute<EditorToolFunctionAttribute>();
                        if (attribute == null)
                        {
                            continue;
                        }

                        attribute.SetMethodInfo(method);
                        Attributes.Add(attribute);
                    }
                }
            }


            Attributes.Sort((left, right) =>
            {
                var orderCompare = left.MenuOrder.CompareTo(right.MenuOrder);
                return orderCompare != 0
                    ? orderCompare
                    : string.Compare(left.ToolMenuPath, right.ToolMenuPath, StringComparison.OrdinalIgnoreCase);
            });
        }
    }
}
