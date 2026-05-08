using System;
using AlicizaX;
using AlicizaX.Timer.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService : ServiceBase, IUIService, IUIDebugService, IServiceTickable
    {
        private ITimerService _timerService;

        protected override void OnInitialize()
        {
        }

        protected override void OnDestroyService()
        {
            DestroyAllManagedUI();
        }

        void IServiceTickable.Tick(float deltaTime)
        {
            for (int layerIndex = 0; layerIndex < _openUI.Length; layerIndex++)
            {
                var layer = _openUI[layerIndex];
                int count = layer.Count;
                if (count == 0) continue;
                for (int i = 0; i < count; i++)
                {
                    if (layer.Count != count)
                    {
                        break;
                    }

                    var window = layer.Items[i];
                    if (window.MetaInfo.NeedUpdate)
                        window.View.InternalUpdate();
                }
            }
        }


        public UniTask<UIBase>? ShowUI(string type, params object[] userDatas)
        {
            if (UIMetaRegistry.TryGet(type, out var metaRegistry))
            {
                UIMetadata metadata = UIMetadataFactory.GetWindowMetadata(metaRegistry.RuntimeTypeHandle);
                return ShowUI(metadata, userDatas);
            }

            return null;
        }

        public T ShowUISync<T>(params object[] userDatas) where T : UIBase
        {
            return (T)ShowUIImplSync(UIMetadataFactory.GetWindowMetadata<T>(), userDatas);
        }

        public async UniTask<T> ShowUI<T>(params System.Object[] userDatas) where T : UIBase
        {
            return (T)await ShowUIAsync(UIMetadataFactory.GetWindowMetadata<T>(), userDatas);
        }


        public void CloseUI<T>(bool force = false) where T : UIBase
        {
            CloseUIImpl(UIMetadataFactory.GetWindowMetadata<T>(), force).Forget();
        }

        public T GetUI<T>() where T : UIBase
        {
            return (T)GetUIImpl(UIMetadataFactory.GetWindowMetadata<T>());
        }


        private UniTask<UIBase> ShowUI(UIMetadata meta, params System.Object[] userDatas)
        {
            return ShowUIImplAsync(meta, userDatas);
        }

        private async UniTask<UIBase> ShowUIAsync(UIMetadata meta, params System.Object[] userDatas)
        {
            return await ShowUIImplAsync(meta, userDatas);
        }


        public void CloseUI(RuntimeTypeHandle handle, bool force = false)
        {
            var metadata = UIMetadataFactory.GetWindowMetadata(handle);
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

            UICacheLayer = null;
            UICanvasRoot = null;
            UICanvas = null;
            UICamera = null;
            UIRoot = null;
        }
    }
}
