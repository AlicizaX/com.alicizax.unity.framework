using System;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    public enum UIOcclusionMode : byte
    {
        None,
        Visible,
        Lifecycle,
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class WindowAttribute : Attribute
    {
        /// <summary>
        /// 窗口层级
        /// </summary>
        public readonly UILayer WindowLayer;

        /// <summary>
        /// 遮挡模式。
        /// </summary>
        public readonly UIOcclusionMode OcclusionMode;

        /// <summary>
        /// 延时关闭
        /// </summary>
        public readonly int CacheTime;

        /// <summary>
        ///
        /// </summary>
        /// <param name="windowLayer">显示层级</param>
        /// <param name="occlusionMode">遮挡模式</param>
        /// <param name="cacheTime">缓存时间/s  -1永久 0不 >=1生效</param>
        public WindowAttribute(UILayer windowLayer, UIOcclusionMode occlusionMode = UIOcclusionMode.None, int cacheTime = 0)
        {
            WindowLayer = windowLayer;
            OcclusionMode = occlusionMode;
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
