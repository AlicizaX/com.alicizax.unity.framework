using System;
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
        ulong PlayAsync(AudioType type, string path, bool loop = false, float volume = 1f);
        ulong Play(AudioType type, string path, bool loop, float volume, in AudioPlayOptions options);
        ulong Play(AudioType type, AudioClip clip, bool loop = false, float volume = 1f);
        ulong Play(AudioType type, AudioClip clip, bool loop, float volume, in AudioPlayOptions options);
        ulong Play3D(AudioType type, string path, in Vector3 position, bool loop = false, float volume = 1f);
        ulong Play3DAsync(AudioType type, string path, in Vector3 position, bool loop = false, float volume = 1f);
        ulong Play3D(AudioType type, string path, in Vector3 position, bool loop, float volume, in AudioSpatialOptions spatial, in AudioPlayOptions options);
        ulong Play3D(AudioType type, AudioClip clip, in Vector3 position, bool loop = false, float volume = 1f);
        ulong Play3D(AudioType type, AudioClip clip, in Vector3 position, bool loop, float volume, in AudioSpatialOptions spatial, in AudioPlayOptions options);
        ulong PlayFollow(AudioType type, string path, Transform target, in Vector3 localOffset, bool loop = false, float volume = 1f);
        ulong PlayFollowAsync(AudioType type, string path, Transform target, in Vector3 localOffset, bool loop = false, float volume = 1f);
        ulong PlayFollow(AudioType type, string path, Transform target, in Vector3 localOffset, bool loop, float volume, in AudioSpatialOptions spatial, in AudioPlayOptions options);
        ulong PlayFollow(AudioType type, AudioClip clip, Transform target, in Vector3 localOffset, bool loop = false, float volume = 1f);
        ulong PlayFollow(AudioType type, AudioClip clip, Transform target, in Vector3 localOffset, bool loop, float volume, in AudioSpatialOptions spatial, in AudioPlayOptions options);
        bool Stop(ulong handle, bool fadeout = false);
        bool Stop(ulong handle, float fadeOutSeconds);
        bool SetVolume(ulong handle, float volume, float fadeSeconds = 0f);
        bool IsPlaying(ulong handle);
        void Stop(AudioType type, bool fadeout);
        void StopAll(bool fadeout);
        void Warmup(AudioType type, int count);
        bool Preload(string address, AudioCachePolicy policy = AudioCachePolicy.Pin);
        void PreloadAsync(string address, AudioCachePolicy policy, Action<bool> completed = null);
        bool Unload(string address, bool force = false);
        void ClearCache(bool force = false);
    }
}
