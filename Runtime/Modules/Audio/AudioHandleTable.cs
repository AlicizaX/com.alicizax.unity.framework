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

        private static ulong s_nextGeneration;

        private readonly AudioAgent[] _agents;

        internal int Capacity => _agents.Length;

        internal AudioAgent[] Agents => _agents;

        internal AudioHandleTable(int capacity)
        {
            if ((ulong)capacity > IndexMask)
                throw new GameFrameworkException("Audio agent count exceeds handle capacity.");

            _agents = new AudioAgent[capacity];
        }

        internal ulong Allocate(AudioAgent agent)
        {
            int index = agent.GlobalIndex;
            _agents[index] = agent;
            s_nextGeneration = (s_nextGeneration + 1UL) & GenerationMask;
            if (s_nextGeneration == 0UL)
                s_nextGeneration = 1UL;

            return (s_nextGeneration << IndexBits) | (uint)(index + 1);
        }

        internal void Release(AudioAgent agent)
        {
            _agents[agent.GlobalIndex] = null;
        }

        internal AudioAgent Resolve(ulong handle)
        {
            int index = (int)(handle & IndexMask) - 1;
            if ((uint)index >= (uint)_agents.Length)
                return null;

            AudioAgent agent = _agents[index];
            return agent != null && agent.Handle == handle ? agent : null;
        }
    }
}
