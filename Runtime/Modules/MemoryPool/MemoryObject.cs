namespace AlicizaX
{
    public interface IPoolEvictable
    {
        void OnEvict();
    }

    public abstract class MemoryObject
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
