namespace AlicizaX
{
    public interface IPoolEvictable
    {
        void OnEvict();
    }

    public abstract class MemoryObject
    {
        internal MemoryPoolBase Pool;
        internal bool InPool;

        public abstract void Clear();
    }
}
