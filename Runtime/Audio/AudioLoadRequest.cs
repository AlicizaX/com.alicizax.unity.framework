using AlicizaX;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioLoadRequest : IMemory
    {
        public AudioClipCacheEntry Entry;
        public AudioLoadRequest Prev;
        public AudioLoadRequest Next;
        public AudioAgent Agent;
        public int Generation;

        public void Clear()
        {
            Entry = null;
            Prev = null;
            Next = null;
            Agent = null;
            Generation = 0;
        }
    }
}
