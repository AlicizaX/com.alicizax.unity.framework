#if UNITY_EDITOR
using System;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    internal interface IUIDebugService
    {
        int LayerCount { get; }
        int CacheWindowCount { get; }
        void FillServiceDebugInfo(UIServiceDebugInfo info);
        bool FillLayerDebugInfo(int layerIndex, UILayerDebugInfo info);
        bool FillWindowDebugInfo(int layerIndex, int windowIndex, UIWindowDebugInfo info);
        int FillCacheDebugInfo(UIWindowDebugInfo[] infos, int capacity);
    }

    internal sealed class UIServiceDebugInfo
    {
        public bool Initialized;
        public bool Orthographic;
        public int LayerCount;
        public int OpenWindowCount;
        public int CacheWindowCount;
        public int UpdateWindowCount;
        public ulong BlockTimerHandle;
        public bool BlockActive;
        public Camera Camera;
        public Canvas Canvas;
        public Transform Root;
        public Transform CanvasRoot;

        public void Clear()
        {
            Initialized = false;
            Orthographic = false;
            LayerCount = 0;
            OpenWindowCount = 0;
            CacheWindowCount = 0;
            UpdateWindowCount = 0;
            BlockTimerHandle = 0UL;
            BlockActive = false;
            Camera = null;
            Canvas = null;
            Root = null;
            CanvasRoot = null;
        }
    }

    internal sealed class UILayerDebugInfo
    {
        public int LayerIndex;
        public UILayer Layer;
        public int WindowCount;
        public int LastFullscreenIndex;
        public RectTransform RectTransform;

        public void Clear()
        {
            LayerIndex = 0;
            Layer = UILayer.Background;
            WindowCount = 0;
            LastFullscreenIndex = -1;
            RectTransform = null;
        }
    }

    internal sealed class UIWindowDebugInfo
    {
        public int LayerIndex;
        public int OrderIndex;
        public RuntimeTypeHandle RuntimeTypeHandle;
        public string LogicTypeName;
        public string HolderTypeName;
        public UIState State;
        public bool Visible;
        public bool FullScreen;
        public bool InCache;
        public bool NeedUpdate;
        public bool ShowInProgress;
        public bool CloseInProgress;
        public int Depth;
        public float CacheTime;
        public ulong CacheTimerHandle;
        public Transform HolderTransform;
        public float StateDuration;

        public void Clear()
        {
            LayerIndex = 0;
            OrderIndex = 0;
            RuntimeTypeHandle = default;
            LogicTypeName = null;
            HolderTypeName = null;
            State = UIState.Uninitialized;
            Visible = false;
            FullScreen = false;
            InCache = false;
            NeedUpdate = false;
            ShowInProgress = false;
            CloseInProgress = false;
            Depth = 0;
            CacheTime = 0f;
            CacheTimerHandle = 0UL;
            HolderTransform = null;
            StateDuration = 0f;
        }
    }
}
#endif
