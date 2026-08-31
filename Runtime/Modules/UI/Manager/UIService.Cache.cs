using System;
using AlicizaX;
using AlicizaX.Timer.Runtime;

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
            if (view == null || view.Holder == null || !view.Holder.IsValid() || view.State != UIState.Closed)
            {
                Log.Error("Cannot cache UI that is not fully closed");
                uiMetadata?.DisposeImmediate();
                return;
            }

            if (force || uiMetadata.MetaInfo.CacheTime == 0)
            {
                RemoveFromCache(uiMetadata.MetaInfo.TypeId);
                uiMetadata.DisposeImmediate();
                return;
            }

            RemoveFromCache(uiMetadata.MetaInfo.TypeId);
            view.ClearUserData();
            view.EnterCacheVisual();
            view.InternalEnterCache();
            view.Holder.transform.SetParent(UICacheLayer, false);
            uiMetadata.CacheTimerHandle = 0UL;
            AddToCache(uiMetadata, 0UL);

            ITimerService timerService = GetTimerService();
            _onTimerDisposeWindow ??= OnTimerDisposeWindow;
            ulong timerHandle = timerService.AddTimer(
                _onTimerDisposeWindow,
                uiMetadata,
                uiMetadata.MetaInfo.CacheTime,
                isLoop: false,
                isUnscaled: true);

            if (timerHandle == 0UL)
            {
#if UNITY_EDITOR
                if (UIWarningSettings.Enabled)
                    Log.Warning("Failed to create cache timer for {0}", uiMetadata.UILogicTypeName);
#endif
                RemoveFromCache(uiMetadata.MetaInfo.TypeId);
                uiMetadata.DisposeImmediate();
                return;
            }

            int typeId = uiMetadata.MetaInfo.TypeId;
            if ((uint)typeId >= (uint)m_CacheTypeIdToIndex.Length || m_CacheTypeIdToIndex[typeId] < 0)
                return;

            uiMetadata.CacheTimerHandle = timerHandle;
            m_CacheWindow[m_CacheTypeIdToIndex[typeId]] = new CacheEntry(uiMetadata, timerHandle);
        }

        private void OnTimerDisposeWindow(UIMetadata meta)
        {
            if (meta == null)
                return;

            if (IsMetaInOpenStack(meta))
                return;

            RemoveFromCache(meta.MetaInfo.TypeId);
            if (meta.State == UIState.Cached)
                meta.DisposeImmediate();
        }

        private void RemoveFromCache(RuntimeTypeHandle typeHandle)
        {
            if (UIMetaRegistry.TryGet(typeHandle, out UIMetaRegistry.UIMetaInfo metaInfo))
                RemoveFromCache(metaInfo.TypeId);
        }

        private void RemoveFromCache(int typeId)
        {
            if ((uint)typeId >= (uint)m_CacheTypeIdToIndex.Length)
                return;

            int index = m_CacheTypeIdToIndex[typeId];
            if (index < 0 || index >= m_CacheWindowCount)
                return;

            RemoveFromCacheAt(index);
        }

        private void RemoveFromCacheAt(int index)
        {
            CacheEntry entry = m_CacheWindow[index];
            int typeId = entry.Metadata.MetaInfo.TypeId;
            int lastIndex = m_CacheWindowCount - 1;
            CacheEntry last = m_CacheWindow[lastIndex];
            m_CacheWindow[index] = last;
            m_CacheWindow[lastIndex] = default;
            m_CacheWindowCount = lastIndex;
            m_CacheTypeIdToIndex[typeId] = -1;
            if (index != lastIndex && last.Metadata != null)
                m_CacheTypeIdToIndex[last.Metadata.MetaInfo.TypeId] = index;

            ulong timerHandle = entry.TimerHandle != 0UL ? entry.TimerHandle : entry.Metadata.CacheTimerHandle;
            if (timerHandle != 0UL && _timerService != null)
            {
                _timerService.RemoveTimer(timerHandle);
                entry.Metadata.CacheTimerHandle = 0UL;
            }
        }

        private void AddToCache(UIMetadata meta, ulong timerHandle)
        {
            int typeId = meta.MetaInfo.TypeId;
            EnsureCacheIndexCapacity(typeId);
            EnsureCacheCapacity();
            int index = m_CacheWindowCount++;
            m_CacheWindow[index] = new CacheEntry(meta, timerHandle);
            m_CacheTypeIdToIndex[typeId] = index;
        }

        private void EnsureCacheCapacity()
        {
            if (m_CacheWindowCount < m_CacheWindow.Length)
                return;

            Array.Resize(ref m_CacheWindow, m_CacheWindow.Length << 1);
        }

        private void EnsureCacheIndexCapacity(int typeId)
        {
            UITypeIndexArray.EnsureCapacity(ref m_CacheTypeIdToIndex, typeId);
        }

        private ITimerService GetTimerService()
        {
            if (_timerService != null)
                return _timerService;

            _timerService = AppServices.App.Require<ITimerService>();
            return _timerService;
        }
    }
}
