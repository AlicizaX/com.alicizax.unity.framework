using System;
using System.Reflection;

namespace AlicizaX
{
    public delegate void InEventHandler<T>(in T evt) where T : struct, IPayloadEventArgs;

    public interface IPayloadEventArgs
    {
    }

    public interface IEmptyEventArgs
    {
    }

    internal static class EventArgsGuard
    {
        internal static void Validate<T>() where T : struct
        {
            Type type = typeof(T);
            bool empty = typeof(IEmptyEventArgs).IsAssignableFrom(type);
            bool payload = typeof(IPayloadEventArgs).IsAssignableFrom(type);
            if (empty == payload)
                throw new InvalidOperationException($"{type.FullName} must implement exactly one event contract.");
            if (empty && type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length !=
                0)
                throw new InvalidOperationException(
                    $"{type.FullName} implements IEmptyEventArgs but declares instance fields.");
        }
    }

    public static class EventInitialSize<T> where T : struct
    {
        public static int Size = 4;
    }
}
