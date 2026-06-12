using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;

namespace AlicizaX
{
    public static class SafePublisher
    {
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Publish<T>(in T evt) where T : struct, IPayloadEventArgs
        {
            EventContainer<T>.SafePublish(in evt);
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Publish<T>() where T : struct, IEmptyEventArgs
        {
            EmptyEventContainer<T>.SafePublish();
        }
    }
}
