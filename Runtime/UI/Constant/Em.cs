using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Cysharp.Text;

public static class InstanceFactory
{
    private static readonly Dictionary<Type, Func<object>> _constructorCache = new();

    public static object CreateInstanceOptimized(Type type)
    {
        if (!_constructorCache.TryGetValue(type, out var constructor))
        {
            var ctor = type.GetConstructor(Type.EmptyTypes);
            if (ctor == null)
            {
                throw new MissingMethodException(ZString.Format("Type {0} missing public parameterless constructor", type.Name));
            }

            var newExpr = Expression.New(ctor);
            var lambda = Expression.Lambda<Func<object>>(newExpr);
            constructor = lambda.Compile();

            _constructorCache[type] = constructor;
        }

        return constructor();
    }
}
