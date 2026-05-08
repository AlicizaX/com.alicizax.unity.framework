using UnityEngine;

namespace AlicizaX.Audio.Runtime
{
    public interface IAudioService : IService
    {
        float Volume { get; set; }
        bool Enable { get; set; }

        float GetCategoryVolume(AudioType type);
        void SetCategoryVolume(AudioType type, float value);
        bool GetCategoryEnable(AudioType type);
        void SetCategoryEnable(AudioType type, bool value);
        ulong Play(AudioType type, string path, bool loop = false, float volume = 1f);
        ulong Play(AudioType type, AudioClip clip, bool loop = false, float volume = 1f);
        ulong Play3D(AudioType type, string path, in Vector3 position, bool loop = false, float volume = 1f);
        ulong Play3D(AudioType type, AudioClip clip, in Vector3 position, bool loop = false, float volume = 1f);
        ulong PlayFollow(AudioType type, string path, Transform target, in Vector3 localOffset, bool loop = false, float volume = 1f);
        ulong PlayFollow(AudioType type, AudioClip clip, Transform target, in Vector3 localOffset, bool loop = false, float volume = 1f);
        bool Stop(ulong handle, bool fadeout = false);
        bool IsPlaying(ulong handle);
        void Stop(AudioType type, bool fadeout);
        void StopAll(bool fadeout);
    }
}
