using System;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    public enum UIBackend : byte
    {
        UGUI = 0,
        UIToolkit = 1,
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class WindowAttribute : Attribute
    {
        /// <summary>
        /// 窗口层级
        /// </summary>
        public readonly UILayer WindowLayer;

        /// <summary>
        /// 延时关闭
        /// </summary>
        public readonly int CacheTime;

        /// <summary>
        /// </summary>
        /// <param name="windowLayer">显示层级</param>
        /// <param name="cacheTime">缓存时间/s  -1永久 0不 >=1生效</param>
        public WindowAttribute(UILayer windowLayer, int cacheTime = 0)
        {
            WindowLayer = windowLayer;
            CacheTime = cacheTime;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class UIUpdateAttribute : Attribute
    {
        public UIUpdateAttribute()
        {
        }
    }


    [AttributeUsage(AttributeTargets.Class)]
    public class UIResAttribute : Attribute
    {
        public readonly string ResLocation;
        public readonly EUIResLoadType ResLoadType;
        public readonly UIBackend Backend;

        public UIResAttribute(string location, EUIResLoadType loadType, UIBackend backend = UIBackend.UGUI)
        {
            ResLocation = location;
            ResLoadType = loadType;
            Backend = backend;
        }
    }

    public enum EUIResLoadType : byte
    {
        Resources,
        AssetBundle
    }
}
