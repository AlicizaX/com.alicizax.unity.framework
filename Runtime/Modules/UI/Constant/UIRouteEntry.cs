using System;

namespace AlicizaX.UI.Runtime
{
    /// <summary>路由条目（值类型）。history 中同类型最多一条，导航到已存在类型时按该条截断。</summary>
    public readonly struct UIRouteEntry
    {
        public readonly Type UIType;
        public readonly object UserData;

        internal UIRouteEntry(Type uiType, object userData)
        {
            UIType = uiType;
            UserData = userData;
        }

        /// <summary>history 为空时取到的是空条目，此值为 false。</summary>
        public bool IsValid => UIType != null;
    }
}
