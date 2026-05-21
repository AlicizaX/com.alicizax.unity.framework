using AlicizaX.Audio.Runtime;
using Cysharp.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class AudioInformationWindow : PollingDebuggerWindowBase
        {
            private readonly AudioServiceDebugInfo _serviceInfo = new AudioServiceDebugInfo();
            private readonly AudioCategoryDebugInfo _categoryInfo = new AudioCategoryDebugInfo();
            private readonly AudioAgentDebugInfo _agentInfo = new AudioAgentDebugInfo();
            private readonly AudioClipCacheDebugInfo _clipCacheInfo = new AudioClipCacheDebugInfo();
            private IAudioDebugService _audioDebugService;

            public override void Initialize(params object[] args)
            {
                TryBindAudioService();
            }

            protected override void BuildWindow(VisualElement root)
            {
                if (_audioDebugService == null && !TryBindAudioService())
                {
                    VisualElement unavailable = CreateSection("Audio", out VisualElement unavailableCard);
                    unavailableCard.Add(CreateRow("State", "Audio service is not initialized."));
                    root.Add(unavailable);
                    return;
                }

                _audioDebugService.FillServiceDebugInfo(_serviceInfo);
                DrawOverview(root);
                DrawCategories(root);
                DrawAgents(root);
                DrawClipCache(root);
            }

            private bool TryBindAudioService()
            {
                if (AppServices.HasWorld && AppServices.App.TryGet<IAudioService>(out IAudioService audioService) && audioService is IAudioDebugService debugService)
                {
                    _audioDebugService = debugService;
                    return true;
                }

                _audioDebugService = null;
                return false;
            }

            private void DrawOverview(VisualElement root)
            {
                VisualElement section = CreateSection("Audio Overview", out VisualElement card);
                card.Add(CreateRow("Initialized", _serviceInfo.Initialized.ToString()));
                card.Add(CreateRow("Unity Audio Disabled", _serviceInfo.UnityAudioDisabled.ToString()));
                card.Add(CreateRow("Enable", _serviceInfo.Enable.ToString()));
                card.Add(CreateRow("Volume", _serviceInfo.Volume.ToString("F3")));
                card.Add(CreateRow("Listener", _serviceInfo.Listener != null ? _serviceInfo.Listener.name : "<None>"));
                card.Add(CreateRow("Instance Root", _serviceInfo.InstanceRoot != null ? _serviceInfo.InstanceRoot.name : "<None>"));
                card.Add(CreateRow("Active Agents", _serviceInfo.ActiveAgentCount.ToString()));
                card.Add(CreateRow("Handle Capacity", _serviceInfo.HandleCapacity.ToString()));
                card.Add(CreateRow("Clip Cache", ZString.Format("{0} / {1}", _serviceInfo.ClipCacheCount, _serviceInfo.ClipCacheCapacity)));
                root.Add(section);
            }

            private void DrawCategories(VisualElement root)
            {
                VisualElement section = CreateSection("Audio Categories", out VisualElement card);
                for (int i = 0; i < _audioDebugService.CategoryCount; i++)
                {
                    if (!_audioDebugService.FillCategoryDebugInfo(i, _categoryInfo))
                    {
                        continue;
                    }

                    card.Add(CreateRow(
                        _categoryInfo.Type.ToString(),
                        ZString.Format(
                            "Enabled {0} | Volume {1:F2} | Active {2} | Free {3} | Heap {4} | Capacity {5}",
                            _categoryInfo.Enabled,
                            _categoryInfo.Volume,
                            _categoryInfo.ActiveCount,
                            _categoryInfo.FreeCount,
                            _categoryInfo.HeapCount,
                            _categoryInfo.Capacity)));
                }

                root.Add(section);
            }

            private void DrawAgents(VisualElement root)
            {
                VisualElement section = CreateSection("Active Audio Agents", out VisualElement card);
                bool hasActive = false;
                for (int typeIndex = 0; typeIndex < _audioDebugService.CategoryCount; typeIndex++)
                {
                    if (!_audioDebugService.FillCategoryDebugInfo(typeIndex, _categoryInfo))
                    {
                        continue;
                    }

                    for (int agentIndex = 0; agentIndex < _categoryInfo.Capacity; agentIndex++)
                    {
                        if (!_audioDebugService.FillAgentDebugInfo(typeIndex, agentIndex, _agentInfo) || _agentInfo.State == AudioAgentRuntimeState.Free)
                        {
                            continue;
                        }

                        hasActive = true;
                        card.Add(CreateRow(
                            ZString.Format("{0}[{1}]", _agentInfo.Type, _agentInfo.Index),
                            ZString.Format(
                                "{0} | Handle {1} | Clip {2} | Source {3} | Volume {4:F2} | Spatial {5} | Occluded {6} | Range {7:F1}-{8:F1}",
                                _agentInfo.State,
                                _agentInfo.Handle,
                                GetClipName(_agentInfo.Clip),
                                GetSourceName(),
                                _agentInfo.Volume,
                                _agentInfo.Spatial,
                                _agentInfo.Occluded,
                                _agentInfo.MinDistance,
                                _agentInfo.MaxDistance)));
                    }
                }

                if (!hasActive)
                {
                    card.Add(CreateRow("Agents", "No active audio agents."));
                }

                root.Add(section);
            }

            private void DrawClipCache(VisualElement root)
            {
                VisualElement section = CreateSection("Audio Clip Cache", out VisualElement card);
                AudioClipCacheEntry entry = _audioDebugService.FirstClipCacheEntry;
                if (entry == null)
                {
                    card.Add(CreateRow("Cache", "Empty"));
                    root.Add(section);
                    return;
                }

                while (entry != null)
                {
                    AudioClipCacheEntry next = entry.AllNext;
                    if (_audioDebugService.FillClipCacheDebugInfo(entry, _clipCacheInfo))
                    {
                        card.Add(CreateRow(
                            _clipCacheInfo.Address,
                            ZString.Format(
                                "Ref {0} | Pending {1} | Loaded {2} | Loading {3} | Pinned {4} | LRU {5} | Last {6:F1}s",
                                _clipCacheInfo.RefCount,
                                _clipCacheInfo.PendingCount,
                                _clipCacheInfo.IsLoaded,
                                _clipCacheInfo.Loading,
                                _clipCacheInfo.Pinned,
                                _clipCacheInfo.InLru,
                                Time.realtimeSinceStartup - _clipCacheInfo.LastUseTime)));
                    }

                    entry = next;
                }

                root.Add(section);
            }

            private string GetSourceName()
            {
                return string.IsNullOrEmpty(_agentInfo.Address) ? "<Direct Clip>" : _agentInfo.Address;
            }

            private static string GetClipName(AudioClip clip)
            {
                return clip != null ? clip.name : "<None>";
            }
        }
    }
}
