#if UNITY_EDITOR
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
        public int OpenWindowCount;
        public int CacheWindowCount;
        public int UpdateCount;
        public bool BlockActive;
        public float BlockRemaining;
        public Camera Camera;
        public Canvas Canvas;
        public Transform Root;
    }

    internal sealed class UILayerDebugInfo
    {
        public UILayer Layer;
        public int WindowCount;

        public void Clear()
        {
            Layer = UILayer.Background;
            WindowCount = 0;
        }
    }

    internal sealed class UIWindowDebugInfo
    {
        public int LayerIndex;
        public int OrderIndex;
        public string LogicTypeName;
        public string HolderTypeName;
        public UIState State;
        public bool Visible;
        public bool Processing;
        public bool Updating;
        public int Depth;
        public int CacheTime;
        public float CacheRemaining;
        public Transform HolderTransform;

        public void Clear()
        {
            LayerIndex = 0;
            OrderIndex = 0;
            LogicTypeName = null;
            HolderTypeName = null;
            State = UIState.Uninitialized;
            Visible = false;
            Processing = false;
            Updating = false;
            Depth = 0;
            CacheTime = 0;
            CacheRemaining = 0f;
            HolderTransform = null;
        }
    }
}
#endif
