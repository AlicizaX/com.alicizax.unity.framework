using System;
using System.Runtime.CompilerServices;

namespace AlicizaX
{
    [Obsolete("Event channels grow on demand. Call EventBus.ReservePayloadCapacity or EventBus.ReserveEmptyCapacity to preallocate.")]
    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class PrewarmAttribute : Attribute
    {
        public int Capacity { get; }
        public PrewarmAttribute(int capacity) => Capacity = capacity;
    }

    public readonly struct EventRuntimeHandle : IDisposable
    {
        private readonly EventChannel _channel;
        private readonly int _slot;
        private readonly int _version;

        internal EventRuntimeHandle(EventChannel channel, int slot, int version)
        {
            _channel = channel;
            _slot = slot;
            _version = version;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_channel != null) _channel.Unsubscribe(_slot, _version);
        }
    }
}
