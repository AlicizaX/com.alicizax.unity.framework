using System;
using AlicizaX;
using AlicizaX.Timer.Runtime;
using Cysharp.Text;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        private readonly struct CacheEntry
        {
            public readonly UIMetadata Metadata;
            public readonly ulong TimerHandle;

            public CacheEntry(UIMetadata metadata, ulong timerHandle)
            {
                Metadata = metadata;
                TimerHandle = timerHandle;
            }
        }

        private CacheEntry[] m_CacheWindow = new CacheEntry[8];
        private int[] m_CacheTypeIdToIndex = UITypeIndexArray.Create(8);
        private int m_CacheWindowCount;
        private Action<UIMetadata> _onTimerDisposeWindow;

        private void CacheWindow(UIMetadata uiMetadata, bool force)
        {
            UIBase view = uiMetadata?.View;
            if (view == null || view.Holder == null || !view.Holder.IsValid())
            {
                Log.Error("Cannot cache null UI metadata or holder");
                return;
            }

            if (force || uiMetadata.MetaInfo.CacheTime == 0)
            {
                RemoveFromCache(uiMetadata.MetaInfo.TypeId);
                uiMetadata.DisposeImmediate();
                return;
            }

            RemoveFromCache(uiMetadata.MetaInfo.TypeId);
            ulong timerHandle = 0UL;

            view.PauseEventListeners();
            // 缓存期间释放用户数据引用，并关闭 Canvas 渲染（不触发网格重建）
            view.ClearUserData();
            view.SetCanvasEnabled(false);
            view.Holder.transform.SetParent(UICacheLayer, false);
            if (uiMetadata.MetaInfo.CacheTime > 0)
            {
                ITimerService timerService = GetTimerService();
                _onTimerDisposeWindow ??= OnTimerDisposeWindow;
                timerHandle = timerService.AddTimer(
                    _onTimerDisposeWindow,
                    uiMetadata,
                    uiMetadata.MetaInfo.CacheTime,
                    isLoop: false,
                    isUnscaled: true);

                if (timerHandle == 0UL)
                {
#if UNITY_EDITOR
                    if (UIWarningSettings.OtherWarningsEnabled)
                    {
                        Log.Warning("Failed to create cache timer for {0}", uiMetadata.UILogicTypeName);
                    }
#endif
                    uiMetadata.DisposeImmediate();
                    return;
                }
            }

            uiMetadata.InCache = true;
            AddToCache(uiMetadata, timerHandle);
        }

        private void OnTimerDisposeWindow(UIMetadata meta)
        {
            if (meta != null)
            {
                RemoveFromCache(meta.MetaInfo.TypeId);
                meta.DisposeImmediate();
            }
        }

        private void RemoveFromCache(RuntimeTypeHandle typeHandle)
        {
            if (UIMetaRegistry.TryGet(typeHandle, out UIMetaRegistry.UIMetaInfo metaInfo))
            {
                RemoveFromCache(metaInfo.TypeId);
            }
        }

        private void RemoveFromCache(int typeId)
        {
            if ((uint)typeId >= (uint)m_CacheTypeIdToIndex.Length)
            {
                return;
            }

            int index = m_CacheTypeIdToIndex[typeId];
            if (index < 0 || index >= m_CacheWindowCount)
            {
                return;
            }

            CacheEntry entry = m_CacheWindow[index];
            int lastIndex = m_CacheWindowCount - 1;
            CacheEntry last = m_CacheWindow[lastIndex];
            m_CacheWindow[index] = last;
            m_CacheWindow[lastIndex] = default;
            m_CacheWindowCount = lastIndex;
            m_CacheTypeIdToIndex[typeId] = -1;
            if (index != lastIndex && last.Metadata != null)
            {
                m_CacheTypeIdToIndex[last.Metadata.MetaInfo.TypeId] = index;
            }

            entry.Metadata.InCache = false;
            if (entry.TimerHandle != 0UL && _timerService != null)
            {
                _timerService.RemoveTimer(entry.TimerHandle);
            }
        }

        private void AddToCache(UIMetadata metadata, ulong timerHandle)
        {
            int typeId = metadata.MetaInfo.TypeId;
            EnsureCacheIndexCapacity(typeId);
            EnsureCacheCapacity();
            int index = m_CacheWindowCount++;
            m_CacheWindow[index] = new CacheEntry(metadata, timerHandle);
            m_CacheTypeIdToIndex[typeId] = index;
        }

        private void EnsureCacheCapacity()
        {
            if (m_CacheWindowCount < m_CacheWindow.Length)
            {
                return;
            }

            Array.Resize(ref m_CacheWindow, m_CacheWindow.Length << 1);
        }

        private void EnsureCacheIndexCapacity(int typeId)
        {
            UITypeIndexArray.EnsureCapacity(ref m_CacheTypeIdToIndex, typeId);
        }

        private ITimerService GetTimerService()
        {
            if (_timerService != null)
            {
                return _timerService;
            }

            _timerService = AppServices.App.Require<ITimerService>();
            return _timerService;
        }
    }
}
