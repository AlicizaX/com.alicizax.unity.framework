using System;

namespace AlicizaX.Audio.Runtime
{
    /// <summary>
    /// 将稳定的公开句柄（索引 + 代际）映射到存活 agent。独立于 <see cref="AudioService"/>，
    /// 使句柄代际和槽位存储只有一个归属者。
    /// </summary>
    internal sealed class AudioHandleTable
    {
        private const int IndexBits = 20;
        private const ulong IndexMask = (1UL << IndexBits) - 1UL;
        private const ulong GenerationMask = ulong.MaxValue >> IndexBits;

        private AudioAgent[] _agents = Array.Empty<AudioAgent>();
        private ulong _nextGeneration;

        internal int Capacity => _agents.Length;

        internal AudioAgent[] Agents => _agents;

        internal void Initialize(int capacity)
        {
            if ((ulong)capacity > IndexMask)
                throw new GameFrameworkException("Audio agent count exceeds handle capacity.");

            // 重新初始化时不要重置代际：上一轮实例的句柄必须保持不可解析。
            _agents = new AudioAgent[capacity];
        }

        internal ulong Allocate(AudioAgent agent)
        {
            int index = agent.GlobalIndex;
            if ((uint)index >= (uint)_agents.Length)
                throw new GameFrameworkException("Audio agent global index is out of range.");

            _agents[index] = agent;
            _nextGeneration = (_nextGeneration + 1UL) & GenerationMask;
            if (_nextGeneration == 0UL)
                _nextGeneration = 1UL;

            return (_nextGeneration << IndexBits) | (ulong)(index + 1);
        }

        internal void Release(AudioAgent agent)
        {
            int index = agent.GlobalIndex;
            if ((uint)index < (uint)_agents.Length && ReferenceEquals(_agents[index], agent))
                _agents[index] = null;
        }

        internal AudioAgent Resolve(ulong handle)
        {
            int index = (int)(handle & IndexMask) - 1;
            if ((uint)index >= (uint)_agents.Length)
                return null;

            AudioAgent agent = _agents[index];
            return agent != null && agent.Handle == handle ? agent : null;
        }

        internal void Clear()
        {
            Array.Clear(_agents, 0, _agents.Length);
            _agents = Array.Empty<AudioAgent>();
        }
    }
}
