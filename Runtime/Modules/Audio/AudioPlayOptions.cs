namespace AlicizaX.Audio.Runtime
{
    public struct AudioPlayOptions
    {
        public bool Async;
        public AudioCachePolicy CachePolicy;
        public float Pitch;
        public float FadeInSeconds;
        public float FadeOutSeconds;
        public int Priority;

        public static AudioPlayOptions Default => default;
    }
}
