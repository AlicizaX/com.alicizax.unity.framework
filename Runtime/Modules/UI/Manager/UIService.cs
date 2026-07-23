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

        protected override void OnDestroyService()
        {
            DestroyAllManagedUI();

            if (Router is UIRouter concreteRouter)
            {
                concreteRouter.ForceResetHistoryForDestroy();
            }
            else
            {
                Router?.ResetHistory();
            }
        }

        void IServiceTickable.Tick(float deltaTime)
        {

            for (int i = 0; i < _updateableWindowCount; i++)
            {
                _updateableWindows[i]?.View?.InternalUpdate();
            }
        }


        public async UniTask<UIBase> ShowUI(string type, params object[] userDatas)
        {
            UIShowResult result = await ShowUIResult(type, userDatas);
            return result.View;
        }

        public UniTask<UIShowResult> ShowUIResult(string type, params object[] userDatas)
        {
            if (UIMetaRegistry.TryGet(type, out var metaRegistry))
            {
                UIMetadata metadata = UIMetadataFactory.GetWindowMetadata(metaRegistry.RuntimeTypeHandle);
                if (metadata == null)
                {
                    return UniTask.FromResult(UIShowResult.Failed);
                }

                return EnqueueShowCommandAsync(metadata, userDatas);
            }

            return UniTask.FromResult(UIShowResult.Failed);
        }

        public async UniTask<UIBase> ShowUI(RuntimeTypeHandle handle, params object[] userDatas)
        {
            UIShowResult result = await ShowUIResult(handle, userDatas);
            return result.View;
        }

        public UniTask<UIShowResult> ShowUIResult(RuntimeTypeHandle handle, params object[] userDatas)
        {
            if (handle.Value == IntPtr.Zero)
            {
                return UniTask.FromResult(UIShowResult.Failed);
            }

            Type uiType = Type.GetTypeFromHandle(handle);
            if (uiType == null || !typeof(UIBase).IsAssignableFrom(uiType))
            {
                return UniTask.FromResult(UIShowResult.Failed);
            }

            UIMetadata metadata = UIMetadataFactory.GetWindowMetadata(handle);
            if (metadata == null)
            {
                return UniTask.FromResult(UIShowResult.Failed);
            }

            return EnqueueShowCommandAsync(metadata, userDatas);
        }

        public T ShowUISync<T>() where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWindowMetadata<T>();
            if (metadata == null || !IsLayerFullyIdle(metadata.MetaInfo.UILayer))
            {
                return null;
            }

            return (T)ShowUIImplSync(metadata, null);
        }

        public T ShowUISync<T>(params object[] userDatas) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWindowMetadata<T>();
            if (metadata == null || !IsLayerFullyIdle(metadata.MetaInfo.UILayer))
            {
                return null;
            }

            return (T)ShowUIImplSync(metadata, userDatas);
        }

        public async UniTask<T> ShowUI<T>() where T : UIBase
        {
            UIShowResult<T> result = await ShowUIResult<T>();
            return result.View;
        }

        public async UniTask<UIShowResult<T>> ShowUIResult<T>() where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWindowMetadata<T>();
            if (metadata == null)
            {
                return new UIShowResult<T>(null, UIShowResultState.Failed);
            }

            UIShowResult result = await EnqueueShowCommandAsync(metadata, null);
            return new UIShowResult<T>((T)result.View, result.State);
        }

        public async UniTask<T> ShowUI<T>(params System.Object[] userDatas) where T : UIBase
        {
            UIShowResult<T> result = await ShowUIResult<T>(userDatas);
            return result.View;
        }

        public async UniTask<UIShowResult<T>> ShowUIResult<T>(params System.Object[] userDatas) where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.GetWindowMetadata<T>();
            if (metadata == null)
            {
                return new UIShowResult<T>(null, UIShowResultState.Failed);
            }

            UIShowResult result = await EnqueueShowCommandAsync(metadata, userDatas);
            return new UIShowResult<T>((T)result.View, result.State);
        }


        public void CloseUI<T>(bool force = false) where T : UIBase
        {
            CloseUIAsync<T>(force).Forget();
        }

        public T GetUI<T>() where T : UIBase
        {
            UIMetadata metadata = UIMetadataFactory.TryGetWindowMetadata(typeof(T).TypeHandle);
            return metadata == null ? null : (T)GetUIImpl(metadata);
        }


        public void CloseUI(RuntimeTypeHandle handle, bool force = false)
        {
            CloseUIAsync(handle, force).Forget();
        }

        public UniTask<bool> CloseUIAsync<T>(bool force = false) where T : UIBase
        {
            return CloseUIAsync(typeof(T).TypeHandle, force);
        }

        public UniTask<bool> CloseUIAsync(RuntimeTypeHandle handle, bool force = false)
        {
            return CloseUIAsyncDirect(handle, force);
        }

        internal UniTask<bool> CloseUIFromRouterAsync(RuntimeTypeHandle handle, bool force = false)
        {
            return CloseUIAsyncCore(handle, force, allowEnqueue: false);
        }

        internal bool IsLayerCloseBlocked(RuntimeTypeHandle handle)
        {
            UIMetadata metadata = UIMetadataFactory.TryGetWindowMetadata(handle);
            if (metadata == null)
            {
                return false;
            }

            int layer = metadata.MetaInfo.UILayer;
            return IsLayerBlockedForMutation(layer) || HasPendingLayerCommands(layer);
        }

        private async UniTask<bool> CloseUIAsyncDirect(RuntimeTypeHandle handle, bool force)
        {
            if (_routerInternal != null && _routerInternal.IsCurrent(handle))
            {
                UIRouteResult routeResult = await _routerInternal.CloseCurrent(handle, force);
                return routeResult.Success;
            }

            return await CloseUIAsyncCore(handle, force, allowEnqueue: true);
        }

        private UniTask<bool> CloseUIAsyncCore(RuntimeTypeHandle handle, bool force, bool allowEnqueue)
        {
            UIMetadata metadata = UIMetadataFactory.TryGetWindowMetadata(handle);
            if (metadata == null)
            {
                return UniTask.FromResult(false);
            }

            int layer = metadata.MetaInfo.UILayer;
            if ((uint)layer >= (uint)_openUI.Length)
            {
                return UniTask.FromResult(false);
            }

            return EnqueueCloseCommandAsync(metadata, force, allowEnqueue);
        }

        public bool IsOpen<T>() where T : UIBase
        {
            return IsOpen(typeof(T).TypeHandle);
        }

        public bool IsOpen(RuntimeTypeHandle handle)
        {
            UIMetadata metadata = UIMetadataFactory.TryGetWindowMetadata(handle);
            return IsOpenImpl(metadata);
        }

        private void DestroyAllManagedUI()
        {
            ClearAllLayerCommandQueues();

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
                _layerMutationBusy[layerIndex] = false;
                _layerVisualDirty[layerIndex] = false;
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
