using System;

namespace AlicizaX
{
    internal static class EventContainer<TEvent, THandler> where TEvent : struct where THandler : Delegate
    {
        internal static readonly EventChannel<THandler> Channel = new EventChannel<THandler>(typeof(TEvent));
    }
}
