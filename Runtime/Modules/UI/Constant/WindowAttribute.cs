using System;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    [AttributeUsage(AttributeTargets.Class)]
    public class WindowAttribute : Attribute
    {
        public readonly UILayer WindowLayer;

        public readonly int CacheTime;

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
