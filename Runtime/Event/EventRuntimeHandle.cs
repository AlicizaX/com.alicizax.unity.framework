using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.IL2CPP.CompilerServices;

namespace AlicizaX
{

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class PrewarmAttribute : Attribute
    {
        public int Capacity { get; }
        public PrewarmAttribute(int capacity) => Capacity = capacity;
    }

    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.DivideByZeroChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct EventRuntimeHandle : IDisposable
    {
        private readonly long _data; // 高32位=index, 低32位=version
        private readonly int _typeId; // 类型ID，避免泛型委托

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal EventRuntimeHandle(int typeId, int index, int version)
        {
            _typeId = typeId;
            _data = ((long)index << 32) | (uint)version;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (_typeId != 0)
            {
                int index = (int)(_data >> 32);
                int version = (int)_data;
                UnsubscribeRegistry.Invoke(_typeId, index, version);
            }
        }
    }
}
