namespace AlicizaX
{
    /// <summary>
    /// 内存对象Interface。
    /// </summary>
    public interface IMemory
    {
        /// <summary>
        /// 清理内存对象回收入池。
        /// </summary>
        void Clear();
    }

    public interface IPoolEvictable
    {
        void OnEvict();
    }

    public abstract class MemoryObject : IMemory
    {
        internal MemoryPoolHandle OwnerHandle;
        internal int PoolId;
        internal int SlotId;
        internal int PageGeneration;
        internal int SlotGeneration;
        internal byte State;

        public abstract void Clear();
    }
}
