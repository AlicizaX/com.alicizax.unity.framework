using System;
using AlicizaX;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioLoadRequest : MemoryObject
    {
        public AudioClipCacheEntry Entry;
        public AudioLoadRequest Prev;
        public AudioLoadRequest Next;
        public AudioAgent Agent;
        public int Generation;
        public Action<bool> Completed;

        public override void Clear()
        {
            Entry = null;
            Prev = null;
            Next = null;
            Agent = null;
            Generation = 0;
            Completed = null;
        }
    }
}
