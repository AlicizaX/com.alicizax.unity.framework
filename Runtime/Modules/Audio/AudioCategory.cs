using UnityEngine;
using UnityEngine.Audio;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioCategory
    {
        private readonly AudioService _service;
        private readonly AudioAgent[] _agents;
        private readonly AudioAgent[] _activeAgents;
        private static readonly byte[] BitIndices =
        {
            0, 1, 28, 2, 29, 14, 24, 3, 30, 22, 20, 15, 25, 17, 4, 8,
            31, 27, 13, 23, 21, 19, 16, 7, 26, 12, 18, 6, 11, 5, 10, 9
        };

        private readonly AudioAgent[] _priorityTails = new AudioAgent[257];
        private readonly uint[] _priorityMasks = new uint[9];
        private uint _priorityGroups;
        private readonly int[] _freeStack;
        private readonly int _globalIndexOffset;
        private int _createdCount;
        private int _activeCount;
        private int _freeCount;
        private bool _enabled;

        internal AudioType Type { get; }
        internal int TypeIndex { get; }
        internal Transform InstanceRoot { get; private set; }
        internal AudioMixerGroup MixerGroup { get; }
        internal AudioGroupConfig Config { get; }
        internal int CreatedCount => _createdCount;
        internal int ActiveCount => _activeCount;
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
                if (!_enabled)
                {
                    Stop(false);
                }
            }
        }

        internal AudioCategory(AudioService service, AudioGroupConfig config, int globalIndexOffset)
        {
            _service = service;
            Config = config;
            Type = config.AudioType;
            TypeIndex = (int)config.AudioType;
            _enabled = !config.Mute;
            _globalIndexOffset = globalIndexOffset;

            MixerGroup = config.MixerGroup;
            InstanceRoot = new GameObject(AudioService.GetCategoryRootName(config.AudioType)).transform;
            InstanceRoot.SetParent(service.InstanceRoot, false);

            int capacity = config.MaxSourceCount;
            _agents = new AudioAgent[capacity];
            _activeAgents = new AudioAgent[capacity];
            _freeStack = new int[capacity];

            int initialCount = config.InitialSourceCount;
            for (int i = 0; i < initialCount; i++)
            {
                AudioAgent agent = CreateAgent();
                _freeStack[_freeCount++] = agent.Index;
            }
        }

        internal ulong Play(AudioPlayRequest request)
        {
            if (!_enabled)
            {
                return 0UL;
            }

            AudioAgent agent = AcquireAgent(request);
            if (agent == null)
            {
                return 0UL;
            }

            try
            {
                return agent.Play(request);
            }
            catch
            {
                agent.Stop(false);
                throw;
            }
        }

        internal void Stop(bool fadeout)
        {
            for (int i = _activeCount - 1; i >= 0; i--)
            {
                _activeAgents[i].Stop(fadeout);
            }
        }

        internal void Warmup(int count)
        {
            int targetCount = Mathf.Clamp(count, 0, _agents.Length);
            while (_createdCount < targetCount)
            {
                AudioAgent agent = CreateAgent();
                _freeStack[_freeCount++] = agent.Index;
            }
        }

        internal void Update(float deltaTime)
        {
            int i = 0;
            while (i < _activeCount)
            {
                AudioAgent agent = _activeAgents[i];
                agent.Update(deltaTime);
                if (i < _activeCount && _activeAgents[i] == agent)
                {
                    i++;
                }
            }
        }

        internal bool TryGetAgent(int index, out AudioAgent agent)
        {
            if ((uint)index >= (uint)_agents.Length)
            {
                agent = null;
                return false;
            }

            agent = _agents[index];
            return agent != null;
        }

        internal void FillDebugInfo(float volume, AudioCategoryDebugInfo info)
        {
            if (info == null)
            {
                return;
            }

            info.Type = Type;
            info.Enabled = _enabled;
            info.Volume = volume;
            info.Capacity = _agents.Length;
            info.CreatedCount = _createdCount;
            info.ActiveCount = _activeCount;
            info.FreeCount = _freeCount;
            info.HeapCount = _activeCount;
        }

        internal void MarkOccupied(AudioAgent agent)
        {
            agent.ActiveIndex = _activeCount;
            _activeAgents[_activeCount++] = agent;
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
            RemoveActive(agent);
            RemovePriority(agent);
            _freeStack[_freeCount++] = agent.Index;
        }

        internal void Shutdown()
        {
            Stop(false);

            for (int i = 0; i < _agents.Length; i++)
            {
                AudioAgent agent = _agents[i];
                if (agent != null)
                {
                    agent.Shutdown();
                    _service.ReleaseSourceObject(TypeIndex, i);
                    MemoryPool.Release(agent);
                    _agents[i] = null;
                }
            }

            _activeCount = 0;
            _freeCount = 0;
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
                InstanceRoot = null;
            }
        }

        private AudioAgent AcquireAgent(AudioPlayRequest request)
        {
            if (_freeCount > 0)
            {
                int index = _freeStack[--_freeCount];
                return _agents[index];
            }

            if (_createdCount < _agents.Length)
            {
                return CreateAgent();
            }

            int group = LowestSetBit(_priorityGroups);
            int priority = (group << 5) + LowestSetBit(_priorityMasks[group]);
            AudioAgent candidate = _priorityTails[priority].PriorityNext;
            if (priority > AudioAgent.ResolvePlaybackPriority(request, Config))
            {
                return null;
            }

            RemoveActive(candidate);
            RemovePriority(candidate);
            return candidate;
        }

        private AudioAgent CreateAgent()
        {
            int index = _createdCount;
            AudioSourceObject sourceObject = _service.AcquireSourceObject(this, index);
            AudioAgent agent = MemoryPool.Acquire<AudioAgent>();
            agent.Initialize(_service, this, index, _globalIndexOffset + index, sourceObject);
            _agents[index] = agent;
            _createdCount++;
            return agent;
        }

        private void RemoveActive(AudioAgent agent)
        {
            int index = agent.ActiveIndex;
            if (index < 0)
            {
                return;
            }

            int lastIndex = --_activeCount;
            AudioAgent last = _activeAgents[lastIndex];
            _activeAgents[lastIndex] = null;
            if (index != lastIndex)
            {
                _activeAgents[index] = last;
                last.ActiveIndex = index;
            }

            agent.ActiveIndex = -1;
        }

        private void RemovePriority(AudioAgent agent)
        {
            AudioAgent next = agent.PriorityNext;
            if (next == null)
            {
                return;
            }

            int priority = agent.PlaybackPriority;
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

        private static int LowestSetBit(uint mask)
        {
            return BitIndices[unchecked(((mask & (0U - mask)) * 0x077CB531U) >> 27)];
        }
    }
}
