using System;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    /// <summary>窗口关闭后的缓存时长（秒）。</summary>
    public static class UICacheTime
    {
        /// <summary>关闭即销毁。</summary>
        public const int None = 0;

        /// <summary>常驻缓存，不计时（任意负数均表示常驻）。</summary>
        public const int Forever = -1;
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class WindowAttribute : Attribute
    {
        public readonly UILayer WindowLayer;

        public readonly int CacheTime;

        public WindowAttribute(UILayer windowLayer, int cacheTime = UICacheTime.None)
        {
            WindowLayer = windowLayer;
            CacheTime = cacheTime;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class UIResAttribute : Attribute
    {
        public readonly string ResLocation;
        public readonly EUIResLoadType ResLoadType;

        public UIResAttribute(string location, EUIResLoadType loadType)
        {
            ResLocation = location;
            ResLoadType = loadType;
        }
    }

    public enum EUIResLoadType : byte
    {
        Resources,
        AssetBundle
    }
}
