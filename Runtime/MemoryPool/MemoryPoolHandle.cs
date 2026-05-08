using System.Runtime.CompilerServices;

namespace AlicizaX
{
    public readonly struct MemoryPoolHandle
    {
        private readonly MemoryPoolRegistry.MemoryPoolHandle _handle;

        internal MemoryPoolHandle(MemoryPoolRegistry.MemoryPoolHandle handle)
        {
            _handle = handle;
        }

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _handle != null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IMemory Acquire()
        {
            return _handle.Acquire();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Release(IMemory memory)
        {
            _handle.Release(memory);
        }
    }
}
