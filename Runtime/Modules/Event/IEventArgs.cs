using System;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;

namespace AlicizaX
{
    public delegate void InEventHandler<T>(in T evt) where T : struct, IPayloadEventArgs;

    public interface IEventArgs { }

    public interface IPayloadEventArgs : IEventArgs { }

    public interface IEmptyEventArgs : IEventArgs { }

#if UNITY_EDITOR
    internal static class EventArgsGuard
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ThrowIfDualEventKind<T>() where T : struct, IEventArgs
        {
            Type type = typeof(T);
            if (typeof(IPayloadEventArgs).IsAssignableFrom(type) &&
                typeof(IEmptyEventArgs).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    $"{type.FullName} cannot implement both {nameof(IPayloadEventArgs)} and {nameof(IEmptyEventArgs)}.");
            }
        }

        internal static void ThrowIfEmptyEventHasInstanceFields<T>() where T : struct, IEmptyEventArgs
        {
            Type type = typeof(T);
            if (type.GetFields(System.Reflection.BindingFlags.Instance |
                               System.Reflection.BindingFlags.Public |
                               System.Reflection.BindingFlags.NonPublic).Length == 0)
            {
                return;
            }

            throw new InvalidOperationException(
                $"{type.FullName} implements {nameof(IEmptyEventArgs)} but declares instance fields. " +
                $"Use {nameof(IPayloadEventArgs)} with InEventHandler<T> for payload events.");
        }
    }
#endif
    
    public static class EventInitialSize<T> where T : struct, IEventArgs
    {
        public static int Size = 4; // default
    }


}
