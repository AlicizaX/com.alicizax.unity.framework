using System;
using System.Collections.Generic;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// 路由条目：不可变，参数数组由条目独占，查询与传递都不再复制。
    /// history 中同类型最多一条（导航时按已存在类型截断，见 UIService.Navigation）。
    /// </summary>
    public sealed class UIRouteEntry
    {
        private readonly object[] _args;

        public Type UIType { get; }
        public RuntimeTypeHandle TypeHandle { get; }
        public int Sequence { get; }

        /// <summary>参数只读视图；数组由 history 条目独占，业务不得写入。</summary>
        public IReadOnlyList<object> Arguments { get; }

        internal object[] Args => _args;

        internal UIRouteEntry(Type uiType, object[] args, int sequence)
        {
            UIType = uiType;
            TypeHandle = uiType.TypeHandle;
            _args = args;
            Sequence = sequence;
            Arguments = Array.AsReadOnly(args);
        }

        internal static object[] CopyArgs(object[] args)
        {
            if (args == null || args.Length == 0) return Array.Empty<object>();
            object[] copy = new object[args.Length];
            Array.Copy(args, copy, args.Length);
            return copy;
        }
    }
}
