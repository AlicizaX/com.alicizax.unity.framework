using UnityEngine;
using UnityEngine.Audio;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioCategory
    {
        private const float MinVolume = 0.0001f;
        private const float MutedDecibels = -80f;
        internal const int MaxPlaybackPriority = 256;

        private static readonly string[] DefaultVolumeParameters =
        {
            "SoundVolume",
            "UISoundVolume",
            "MusicVolume",
            "VoiceVolume",
            "AmbientVolume"
        };

        private static readonly string[] RootNames =
        {
            "Audio Category - Sound",
            "Audio Category - UISound",
            "Audio Category - Music",
            "Audio Category - Voice",
            "Audio Category - Ambient"
        };

        private static readonly byte[] BitIndices =
        {
            0, 1, 28, 2, 29, 14, 24, 3, 30, 22, 20, 15, 25, 17, 4, 8,
            31, 27, 13, 23, 21, 19, 16, 7, 26, 12, 18, 6, 11, 5, 10, 9
        };

        private readonly AudioService _service;
        private readonly AudioMixer _mixer;
        private readonly string _volumeParameter;
        private readonly AudioAgent[] _agents;
        private readonly AudioAgent[] _priorityTails = new AudioAgent[MaxPlaybackPriority + 1];
        private readonly uint[] _priorityMasks = new uint[(MaxPlaybackPriority >> 5) + 1];
        private readonly int _globalIndexOffset;
        private uint _priorityGroups;
        private int _createdCount;
        private int _activeCount;
        private float _volume;
        private bool _enabled;

        internal AudioType Type { get; }
        internal Transform InstanceRoot { get; private set; }
        internal AudioGroupConfig Config { get; }
        internal int CreatedCount => _createdCount;
        internal int ActiveCount => _activeCount;
        internal float Volume => _volume;

        internal bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                {
                    return;
                }

                _enabled = value;
                ApplyMixerVolume();
                if (!value)
                {
                    Stop(false);
                }
            }
        }

        internal AudioCategory(AudioService service, AudioGroupConfig config, AudioMixer mixer, Transform serviceRoot, int globalIndexOffset)
        {
            _service = service;
            _mixer = mixer;
            Config = config;
            Type = config.AudioType;
            _volumeParameter = string.IsNullOrEmpty(config.ExposedVolumeParameter)
                ? DefaultVolumeParameters[(int)config.AudioType]
                : config.ExposedVolumeParameter;
            _enabled = !config.Mute;
            _globalIndexOffset = globalIndexOffset;
            InstanceRoot = new GameObject(RootNames[(int)config.AudioType]).transform;
            InstanceRoot.SetParent(serviceRoot, false);
            _agents = new AudioAgent[config.MaxSourceCount];
            SetVolume(config.Volume);

            int initialCount = config.InitialSourceCount;
            for (int i = 0; i < initialCount; i++)
            {
                CreateAgent();
            }
        }

        internal void SetVolume(float value)
        {
            _volume = Mathf.Clamp(value, MinVolume, 1f);
            ApplyMixerVolume();
        }

        internal ulong Play(in AudioPlayRequest request)
        {
            if (!_enabled)
            {
                return 0UL;
            }

            int priority = request.Options.Priority > 0
                ? Mathf.Min(request.Options.Priority, MaxPlaybackPriority)
                : Mathf.Clamp(MaxPlaybackPriority - Config.SourcePriority, 0, MaxPlaybackPriority);
            AudioAgent agent = AcquireAgent(priority);
            return agent != null ? agent.Play(in request, priority) : 0UL;
        }

        internal void Stop(bool fadeout)
        {
            for (int i = _activeCount - 1; i >= 0; i--)
            {
                _agents[i].Stop(fadeout);
            }
        }

        internal void Warmup(int count)
        {
            int targetCount = Mathf.Min(count, _agents.Length);
            while (_createdCount < targetCount)
            {
                CreateAgent();
            }
        }

        internal void Update(float deltaTime, float now, Transform listener)
        {
            for (int i = _activeCount - 1; i >= 0; i--)
            {
                _agents[i].Update(deltaTime, now, listener);
            }
        }

        internal bool TryGetAgent(int index, out AudioAgent agent)
        {
            if ((uint)index >= (uint)_createdCount)
            {
                agent = null;
                return false;
            }

            agent = _agents[index];
            return true;
        }

        internal void FillDebugInfo(AudioCategoryDebugInfo info)
        {
            info.Type = Type;
            info.Enabled = _enabled;
            info.Volume = _volume;
            info.Capacity = _agents.Length;
            info.CreatedCount = _createdCount;
            info.ActiveCount = _activeCount;
            info.FreeCount = _createdCount - _activeCount;
        }

        internal void MarkOccupied(AudioAgent agent)
        {
            int priority = agent.PlaybackPriority;
            AudioAgent tail = _priorityTails[priority];
            if (tail == null)
            {
                agent.PriorityPrev = agent;
                agent.PriorityNext = agent;
                _priorityMasks[priority >> 5] |= 1U << (priority & 31);
                _priorityGroups |= 1U << (priority >> 5);
            }
            else
            {
                AudioAgent head = tail.PriorityNext;
                agent.PriorityPrev = tail;
                agent.PriorityNext = head;
                tail.PriorityNext = agent;
                head.PriorityPrev = agent;
            }

            _priorityTails[priority] = agent;
        }

        internal void MarkFree(AudioAgent agent)
        {
            RemovePriority(agent);
            int lastIndex = --_activeCount;
            int index = agent.Index;
            AudioAgent last = _agents[lastIndex];
            _agents[index] = last;
            last.Index = index;
            _agents[lastIndex] = agent;
            agent.Index = lastIndex;
        }

        internal void Shutdown()
        {
            Stop(false);
            System.Array.Clear(_agents, 0, _createdCount);
            _createdCount = 0;
            if (InstanceRoot != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(InstanceRoot.gameObject);
                }
                else
                {
                    Object.DestroyImmediate(InstanceRoot.gameObject);
                }
            }

            InstanceRoot = null;
        }

        private AudioAgent AcquireAgent(int priority)
        {
            if (_activeCount == _createdCount)
            {
                if (_createdCount == _agents.Length)
                {
                    return StealAgent(priority);
                }

                CreateAgent();
            }

            return _agents[_activeCount++];
        }

        private AudioAgent StealAgent(int priority)
        {
            int group = LowestSetBit(_priorityGroups);
            int lowestPriority = (group << 5) + LowestSetBit(_priorityMasks[group]);
            if (lowestPriority > priority)
            {
                return null;
            }

            AudioAgent candidate = _priorityTails[lowestPriority].PriorityNext;
            RemovePriority(candidate);
            return candidate;
        }

        private void CreateAgent()
        {
            int index = _createdCount++;
            var agent = new AudioAgent();
            _agents[index] = agent;
            agent.Initialize(_service, this, index, _globalIndexOffset + index);
        }

        private void RemovePriority(AudioAgent agent)
        {
            int priority = agent.PlaybackPriority;
            AudioAgent next = agent.PriorityNext;
            if (ReferenceEquals(next, agent))
            {
                _priorityTails[priority] = null;
                int group = priority >> 5;
                _priorityMasks[group] &= ~(1U << (priority & 31));
                if (_priorityMasks[group] == 0U)
                {
                    _priorityGroups &= ~(1U << group);
                }
            }
            else
            {
                AudioAgent previous = agent.PriorityPrev;
                previous.PriorityNext = next;
                next.PriorityPrev = previous;
                if (ReferenceEquals(_priorityTails[priority], agent))
                {
                    _priorityTails[priority] = previous;
                }
            }

            agent.PriorityPrev = null;
            agent.PriorityNext = null;
        }

        private void ApplyMixerVolume()
        {
            _mixer.SetFloat(_volumeParameter, _enabled ? Mathf.Log10(_volume) * 20f : MutedDecibels);
        }

        private static int LowestSetBit(uint mask)
        {
            return BitIndices[unchecked(((mask & (0U - mask)) * 0x077CB531U) >> 27)];
        }
    }
}
