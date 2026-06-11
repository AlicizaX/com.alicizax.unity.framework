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

            MixerGroup = config.MixerGroup;
            InstanceRoot = new GameObject(AudioService.GetCategoryRootName(config.AudioType)).transform;
            InstanceRoot.SetParent(service.InstanceRoot, false);

            int capacity = config.AgentHelperCount;
            _agents = new AudioAgent[capacity];
            _activeAgents = new AudioAgent[capacity];
            _playHeap = new AudioAgent[capacity];
            _freeStack = new int[capacity];
            _freeCount = capacity;

            for (int i = 0; i < capacity; i++)
            {
                AudioSourceObject sourceObject = service.AcquireSourceObject(this, i);
                AudioAgent agent = MemoryPool.Acquire<AudioAgent>();
                agent.Initialize(service, this, i, globalIndexOffset + i, sourceObject);
                _agents[i] = agent;
                _freeStack[i] = capacity - 1 - i;
            }
        }

        internal ulong Play(AudioPlayRequest request)
        {
            if (!_enabled)
            {
                return 0UL;
            }

            AudioAgent agent = AcquireAgent();
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

            if (InstanceRoot != null)
            {
                Object.Destroy(InstanceRoot.gameObject);
                InstanceRoot = null;
            }
        }

        private AudioAgent AcquireAgent()
        {
            if (_freeCount > 0)
            {
                int index = _freeStack[--_freeCount];
                return _agents[index];
            }

            if (_heapCount <= 0)
            {
                return null;
            }

            AudioAgent oldest = _playHeap[0];
            RemoveActive(oldest);
            RemoveHeapAt(0);
            return oldest;
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
            if (index > 0 && IsOlder(last, _playHeap[parent]))
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
                if (!IsOlder(item, parentAgent))
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
                if (right < _heapCount && IsOlder(_playHeap[right], childAgent))
                {
                    child = right;
                    childAgent = _playHeap[child];
                }

                if (!IsOlder(childAgent, item))
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

        private static bool IsOlder(AudioAgent left, AudioAgent right)
        {
            return left.StartedAt < right.StartedAt;
        }
    }
}
