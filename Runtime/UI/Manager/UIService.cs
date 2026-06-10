using System;
using AlicizaX;
using AlicizaX.Timer.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService : ServiceBase, IUIService,
#if UNITY_EDITOR
        IUIDebugService,
#endif
        IServiceTickable
    {
        private ITimerService _timerService;
        private UIMetadata[] _updateableWindows = new UIMetadata[8];
        private int _updateableWindowCount;

        protected override void OnInitialize()
        {
        }

        protected override void OnDestroyService()
        {
            DestroyAllManagedUI();
        }

        void IServiceTickable.Tick(float deltaTime)
        {
            // 正向遍历 + 实时计数：窗口在 Update 中关闭自己时（交换删除），最多漏更一帧，不会重复更新
            for (int i = 0; i < _updateableWindowCount; i++)
            {
                _updateableWindows[i]?.View?.InternalUpdate();
            }
        }


        public UniTask<UIBase> ShowUI(string type, params object[] userDatas)
        {
            if (UIMetaRegistry.TryGet(type, out var metaRegistry))
            {
                UIMetadata metadata = UIMetadataFactory.GetWindowMetadata(metaRegistry.RuntimeTypeHandle);
                if (metadata == null)
                {
                    return UniTask.FromResult<UIBase>(null);
                }

                return ShowUIImplAsync(metadata, userDatas);
            }

            return UniTask.FromResult<UIBase>(null);
        }

        public T ShowUISync<T>() where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWindowMetadata<T>();
            return metadata == null ? null : (T)ShowUIImplSync(metadata, null);
        }

        public T ShowUISync<T>(params object[] userDatas) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWindowMetadata<T>();
            return metadata == null ? null : (T)ShowUIImplSync(metadata, userDatas);
        }

        public async UniTask<T> ShowUI<T>() where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWindowMetadata<T>();
            return metadata == null ? null : (T)await ShowUIImplAsync(metadata, null);
        }

        public async UniTask<T> ShowUI<T>(params System.Object[] userDatas) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWindowMetadata<T>();
            return metadata == null ? null : (T)await ShowUIImplAsync(metadata, userDatas);
        }


        public void CloseUI<T>(bool force = false) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.TryGetWindowMetadata(typeof(T).TypeHandle);
            if (metadata != null)
            {
                CloseUIImpl(metadata, force).Forget();
            }
        }

        public T GetUI<T>() where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.TryGetWindowMetadata(typeof(T).TypeHandle);
            return metadata == null ? null : (T)GetUIImpl(metadata);
        }


        public void CloseUI(RuntimeTypeHandle handle, bool force = false)
        {
            var metadata = UIMetadataFactory.TryGetWindowMetadata(handle);
            if (metadata == null)
            {
                return;
            }

            if (metadata.State != UIState.Uninitialized && metadata.State != UIState.Destroying)
            {
                CloseUIImpl(metadata, force).Forget();
            }
        }


        private void DestroyAllManagedUI()
        {
            for (int layerIndex = 0; layerIndex < _openUI.Length; layerIndex++)
            {
                LayerData layer = _openUI[layerIndex];
                if (layer == null)
                {
                    continue;
                }

                int count = layer.Count;
                for (int i = count - 1; i >= 0; i--)
                {
                    UIMetadata meta = layer.Items[i];
                    if (meta == null)
                    {
                        continue;
                    }

                    meta.CancelAsyncOperations();
                    meta.DisposeImmediate();
                }

                Array.Clear(layer.Items, 0, layer.Count);
                for (int i = 0; i < layer.TypeIdToIndex.Length; i++)
                {
                    layer.TypeIdToIndex[i] = -1;
                }

                layer.Count = 0;
                layer.LastFullscreenIndex = -1;
            }

            Array.Clear(_updateableWindows, 0, _updateableWindowCount);
            _updateableWindowCount = 0;

            if (m_CacheWindowCount > 0)
            {
                for (int i = m_CacheWindowCount - 1; i >= 0; i--)
                {
                    CacheEntry entry = m_CacheWindow[i];
                    if (entry.TimerHandle != 0UL && _timerService != null)
                    {
                        _timerService.RemoveTimer(entry.TimerHandle);
                    }

                    entry.Metadata.InCache = false;
                    entry.Metadata.CancelAsyncOperations();
                    entry.Metadata.DisposeImmediate();
                    m_CacheWindow[i] = default;
                }

                for (int i = 0; i < m_CacheTypeIdToIndex.Length; i++)
                {
                    m_CacheTypeIdToIndex[i] = -1;
                }

                m_CacheWindowCount = 0;
            }

            if (m_LastCountDownHandle != 0UL && _timerService != null)
            {
                _timerService.RemoveTimer(m_LastCountDownHandle);
                m_LastCountDownHandle = 0UL;
            }

            if (m_LayerBlock != null)
            {
                UnityEngine.Object.Destroy(m_LayerBlock);
                m_LayerBlock = null;
            }

            if (UIRoot != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(UIRoot.gameObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(UIRoot.gameObject);
                }
            }

            UICacheLayer = null;
            UICanvasRoot = null;
            UICanvas = null;
            UICamera = null;
            UIRoot = null;
        }
    }
}
