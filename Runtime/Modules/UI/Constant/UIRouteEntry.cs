using System;

namespace AlicizaX.UI.Runtime
{
    /// <summary>路由条目（值类型）。history 中同类型最多一条，导航到已存在类型时按该条截断。</summary>
    public readonly struct UIRouteEntry
    {
        public readonly Type UIType;
        public readonly object UserData;
        public readonly int Sequence;

        internal UIRouteEntry(Type uiType, object userData, int sequence)
        {
            UIType = uiType;
            UserData = userData;
            Sequence = sequence;
        }

        /// <summary>空条目（<see cref="IsValid"/> 为 false）返回默认句柄。</summary>
        public RuntimeTypeHandle TypeHandle => UIType != null ? UIType.TypeHandle : default;
        public bool IsValid => UIType != null;
    }
}
