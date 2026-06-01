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

        private void CacheWindow(UIMetadata uiMetadata, bool force)
        {
            if (uiMetadata?.View?.Holder == null)
            {
                Log.Error("Cannot cache null UI metadata or holder");
                return;
            }

            if (force || uiMetadata.MetaInfo.CacheTime == 0)
            {
                uiMetadata.DisposeImmediate();
                return;
            }

            RemoveFromCache(uiMetadata.MetaInfo.TypeId);
            ulong timerHandle = 0UL;

            uiMetadata.View.Holder.transform.SetParent(UICacheLayer);
            if (uiMetadata.MetaInfo.CacheTime > 0)
            {
                ITimerService timerService = GetTimerService();
                timerHandle = timerService.AddTimer(
                    OnTimerDisposeWindow,
                    uiMetadata,
                    uiMetadata.MetaInfo.CacheTime,
                    isLoop: false,
                    isUnscaled: true);

                if (timerHandle == 0UL)
                {
                    Log.Warning(ZString.Format("Failed to create cache timer for {0}", uiMetadata.UILogicType.Name));
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
