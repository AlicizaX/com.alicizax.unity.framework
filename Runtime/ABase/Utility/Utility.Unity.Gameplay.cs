using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AlicizaX
{
    public static partial class Utility
    {
        public static partial class Unity
        {
            public static void ShowCursor(bool locked, bool visible)
            {
                Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = visible;
            }

            public static bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance, int cullLayers, Layer interactLayer)
            {
                if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, cullLayers))
                {
                    if (interactLayer.CompareLayer(hit.collider.gameObject))
                    {
                        hitInfo = hit;
                        return true;
                    }
                }

                hitInfo = default;
                return false;
            }

            public static AudioSource PlayOneShot2D(Vector3 position, AudioClip clip, float volume = 1f, string name = "OneShotAudio")
            {
                if (clip == null)
                {
                    return null;
                }

                GameObject gameObject = new GameObject(name);
                gameObject.transform.position = position;
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.spatialBlend = 0f;
                source.clip = clip;
                source.volume = volume;
                source.Play();
                Object.Destroy(gameObject, clip.length * ((UnityEngine.Time.timeScale < 0.01f) ? 0.01f : UnityEngine.Time.timeScale));
                return source;
            }

            public static AudioSource PlayOneShot2D(Vector3 position, SoundClip clip, string name = "OneShotAudio")
            {
                if (clip == null || clip.audioClip == null)
                {
                    return null;
                }

                return PlayOneShot2D(position, clip.audioClip, clip.volume, name);
            }

            public static AudioSource PlayOneShot3D(Vector3 position, AudioClip clip, float volume = 1f, string name = "OneShotAudio")
            {
                if (clip == null)
                {
                    return null;
                }

                GameObject gameObject = new GameObject(name);
                gameObject.transform.position = position;
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.spatialBlend = 1f;
                source.clip = clip;
                source.volume = volume;
                source.Play();
                Object.Destroy(gameObject, clip.length * ((UnityEngine.Time.timeScale < 0.01f) ? 0.01f : UnityEngine.Time.timeScale));
                return source;
            }

            public static AudioSource PlayOneShot3D(Vector3 position, AudioClip clip, float maxDistance, float volume = 1f, string name = "OneShotAudio")
            {
                if (clip == null)
                {
                    return null;
                }

                GameObject gameObject = new GameObject(name);
                gameObject.transform.position = position;
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.spatialBlend = 1f;
                source.clip = clip;
                source.volume = volume;
                source.maxDistance = maxDistance;
                source.Play();
                Object.Destroy(gameObject, clip.length * ((UnityEngine.Time.timeScale < 0.01f) ? 0.01f : UnityEngine.Time.timeScale));
                return source;
            }

            public static AudioSource PlayOneShot3D(Vector3 position, SoundClip clip, string name = "OneShotAudio")
            {
                if (clip == null || clip.audioClip == null)
                {
                    return null;
                }

                return PlayOneShot3D(position, clip.audioClip, clip.volume, name);
            }

        }
    }
}
