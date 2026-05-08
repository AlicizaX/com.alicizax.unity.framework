using System;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;

namespace AlicizaX
{
    public delegate void InEventHandler<T>(in T evt) where T : struct, IEventArgs;

    public interface IEventArgs { }
    
    public static class EventInitialSize<T> where T : struct, IEventArgs
    {
        public static int Size = 4; // default
    }


}
