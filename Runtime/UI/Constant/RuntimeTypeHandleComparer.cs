using System;
using System.Collections.Generic;

namespace AlicizaX.UI.Runtime
{
    internal sealed class RuntimeTypeHandleComparer : IEqualityComparer<RuntimeTypeHandle>
    {
        public static readonly RuntimeTypeHandleComparer Instance = new();

        public bool Equals(RuntimeTypeHandle x, RuntimeTypeHandle y) => x.Value == y.Value;

        public int GetHashCode(RuntimeTypeHandle obj) => obj.Value.GetHashCode();
    }
}
