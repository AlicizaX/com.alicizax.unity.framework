using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Cysharp.Text;

namespace AlicizaX
{
    public static partial class Utility
    {
        /// <summary>
        /// 实例创建相关的实用函数。
        /// </summary>
        public static class InstanceFactory
        {
            private static readonly Dictionary<Type, Func<object>> s_constructorCache = new();

            public static object CreateInstanceOptimized(Type type)
            {
                if (!s_constructorCache.TryGetValue(type, out var constructor))
                {
                    var ctor = type.GetConstructor(Type.EmptyTypes);
                    if (ctor == null)
                    {
                        Log.Error(ZString.Format("Type {0} missing public parameterless constructor", type.Name));
                        return null;
                    }

                    var newExpr = Expression.New(ctor);
                    var lambda = Expression.Lambda<Func<object>>(newExpr);
                    constructor = lambda.Compile();

                    s_constructorCache[type] = constructor;
                }

                return constructor();
            }
        }
    }
}
