using UnityEngine;
using UnityEngine.Audio;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioCategory
    {
        private readonly AudioService _service;
        private readonly AudioAgent[] _agents;
        private readonly AudioAgent[] _activeAgents;
        private readonly AudioAgent[] _playHeap;
        private readonly int[] _freeStack;
        private readonly int _globalIndexOffset;
        private int _createdCount;
        private int _activeCount;
        private int _heapCount;
        private int _freeCount;
        private bool _enabled;

        internal AudioType Type { get; }
        internal int TypeIndex { get; }
        internal Transform InstanceRoot { get; private set; }
        internal AudioMixerGroup MixerGroup { get; }
        internal AudioGroupConfig Config { get; }
        internal int Capacity => _agents.Length;
        internal int CreatedCount => _createdCount;
        internal int ActiveCount => _activeCount;
        internal int FreeCount => _freeCount;
        internal int HeapCount => _heapCount;
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
            _playHeap = new AudioAgent[capacity];
            _freeStack = new int[capacity];

            int initialCount = config.InitialSourceCount;
            for (int i = 0; i < initialCount; i++)
            {
                AudioAgent agent = CreateAgent();
                if (agent != null)
                {
                    _freeStack[_freeCount++] = agent.Index;
                }
            }
        }

        internal ulong Play(AudioPlayRequest request)
        {
            if (!_enabled)
            {
                return 0UL;
            }

            AudioAgent agent = AcquireAgent(request);
            return agent != null ? agent.Play(request) : 0UL;
        }

        internal void Stop(bool fadeout)
        {
            for (int i = _activeCount - 1; i >= 0; i--)
            {
                AudioAgent agent = _activeAgents[i];
                if (agent != null)
                {
                    agent.Stop(fadeout);
                }
            }
        }

        internal void Warmup(int count)
        {
            int targetCount = Mathf.Clamp(count, 0, _agents.Length);
            while (_createdCount < targetCount)
            {
                AudioAgent agent = CreateAgent();
                if (agent == null)
                {
                    return;
                }

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
            info.HeapCount = _heapCount;
        }

        internal void MarkOccupied(AudioAgent agent)
        {
            if (agent.ActiveIndex < 0)
            {
                agent.ActiveIndex = _activeCount;
                _activeAgents[_activeCount++] = agent;
            }

            if (agent.HeapIndex < 0)
            {
                int heapIndex = _heapCount++;
                _playHeap[heapIndex] = agent;
                agent.HeapIndex = heapIndex;
                SiftHeapUp(heapIndex);
            }
        }

        internal void MarkFree(AudioAgent agent)
        {
            RemoveActive(agent);
            RemoveHeap(agent);

            if (_freeCount < _freeStack.Length)
            {
                _freeStack[_freeCount++] = agent.Index;
            }
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
            _heapCount = 0;
            _freeCount = 0;
            _createdCount = 0;

            if (InstanceRoot != null)
            {
                Object.Destroy(InstanceRoot.gameObject);
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

            if (_heapCount <= 0)
            {
                return null;
            }

            AudioAgent candidate = _playHeap[0];
            int incomingPriority = ResolvePlaybackPriority(request);
            if (candidate.PlaybackPriority > incomingPriority)
            {
                return null;
            }

            RemoveActive(candidate);
            RemoveHeapAt(0);
            return candidate;
        }

        private AudioAgent CreateAgent()
        {
            int index = _createdCount;
            if ((uint)index >= (uint)_agents.Length)
            {
                return null;
            }

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

        private void RemoveHeap(AudioAgent agent)
        {
            int index = agent.HeapIndex;
            if (index >= 0)
            {
                RemoveHeapAt(index);
            }
        }

        private void RemoveHeapAt(int index)
        {
            int lastIndex = --_heapCount;
            AudioAgent removed = _playHeap[index];
            AudioAgent last = _playHeap[lastIndex];
            _playHeap[lastIndex] = null;
            removed.HeapIndex = -1;

            if (index == lastIndex)
            {
                return;
            }

            _playHeap[index] = last;
            last.HeapIndex = index;
            int parent = (index - 1) >> 1;
            if (index > 0 && IsBetterStealCandidate(last, _playHeap[parent]))
            {
                SiftHeapUp(index);
            }
            else
            {
                SiftHeapDown(index);
            }
        }

        private void SiftHeapUp(int index)
        {
            AudioAgent item = _playHeap[index];
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                AudioAgent parentAgent = _playHeap[parent];
                if (!IsBetterStealCandidate(item, parentAgent))
                {
                    break;
                }

                _playHeap[index] = parentAgent;
                parentAgent.HeapIndex = index;
                index = parent;
            }

            _playHeap[index] = item;
            item.HeapIndex = index;
        }

        private void SiftHeapDown(int index)
        {
            AudioAgent item = _playHeap[index];
            int half = _heapCount >> 1;
            while (index < half)
            {
                int child = (index << 1) + 1;
                int right = child + 1;
                AudioAgent childAgent = _playHeap[child];
                if (right < _heapCount && IsBetterStealCandidate(_playHeap[right], childAgent))
                {
                    child = right;
                    childAgent = _playHeap[child];
                }

                if (!IsBetterStealCandidate(childAgent, item))
                {
                    break;
                }

                _playHeap[index] = childAgent;
                childAgent.HeapIndex = index;
                index = child;
            }

            _playHeap[index] = item;
            item.HeapIndex = index;
        }

        private static bool IsBetterStealCandidate(AudioAgent left, AudioAgent right)
        {
            if (left.PlaybackPriority != right.PlaybackPriority)
            {
                return left.PlaybackPriority < right.PlaybackPriority;
            }

            return left.StartedAt < right.StartedAt;
        }

        private int ResolvePlaybackPriority(AudioPlayRequest request)
        {
            if (request != null && request.Priority > 0)
            {
                return Mathf.Clamp(request.Priority, 0, 256);
            }

            return Mathf.Clamp(256 - Config.SourcePriority, 0, 256);
        }
    }
}
