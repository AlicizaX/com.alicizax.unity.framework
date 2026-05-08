using System;
using AlicizaX;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace UnityEngine
{
    [UnityEngine.Scripting.Preserve]
    public static class ColorExtensions
    {
        public static Color Alpha(this Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        public static Color Lightness(this Color color, float lightness)
        {
            Color.RGBToHSV(color, out var hue, out var saturation, out var _);
            return Color.HSVToRGB(hue, saturation, lightness);
        }
    }

    [UnityEngine.Scripting.Preserve]
    public static class ImageExtensions
    {
        public static void Alpha(this Image image, float alpha)
        {
            Color color = image.color;
            color.a = alpha;
            image.color = color;
        }
    }

    [UnityEngine.Scripting.Preserve]
    public static class AudioSourceExtensions
    {
        public static void SetSoundClip(this AudioSource audioSource, SoundClip soundClip, float volumeMul = 1f, bool play = false)
        {
            if (soundClip == null || soundClip.audioClip == null || audioSource == null)
            {
                return;
            }

            if (audioSource.clip != soundClip.audioClip)
            {
                audioSource.clip = soundClip.audioClip;
            }

            audioSource.volume = soundClip.volume * volumeMul;
            if (play && !audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }

        public static void PlayOneShotSoundClip(this AudioSource audioSource, SoundClip soundClip, float volumeMul = 1f)
        {
            if (soundClip == null || soundClip.audioClip == null || audioSource == null)
            {
                return;
            }

            audioSource.PlayOneShot(soundClip.audioClip, soundClip.volume * volumeMul);
        }
    }

    [UnityEngine.Scripting.Preserve]
    public static class LayerMaskExtensions
    {
        public static bool CompareLayer(this LayerMask layerMask, int layer)
        {
            return layerMask == (layerMask | (1 << layer));
        }
    }

    [UnityEngine.Scripting.Preserve]
    public static class AnimatorExtensions
    {
        public static bool IsAnyPlaying(this Animator animator)
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            return (stateInfo.length + 0.1f > stateInfo.normalizedTime || animator.IsInTransition(0)) && !stateInfo.IsName("Default");
        }
    }

    [UnityEngine.Scripting.Preserve]
    public static class RendererExtensions
    {
        public static bool IsVisibleFrom(this Renderer renderer, Camera camera)
        {
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            return GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
        }
    }

    [UnityEngine.Scripting.Preserve]
    public static class CameraExtensions
    {
        public static Texture2D GetCaptureScreenshot(this Camera camera, float scale = 0.5f)
        {
            Rect rect = new Rect(0f, 0f, Screen.width * scale, Screen.height * scale);
            string name = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
            RenderTexture renderTexture = RenderTexture.GetTemporary((int)rect.width, (int)rect.height, 0);
            renderTexture.name = SceneManager.GetActiveScene().name + "_" + renderTexture.width + "_" + renderTexture.height + "_" + name;
            camera.targetTexture = renderTexture;
            camera.Render();

            RenderTexture.active = renderTexture;
            Texture2D screenShot = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGB24, false)
            {
                name = renderTexture.name
            };
            screenShot.ReadPixels(rect, 0, 0);
            screenShot.Apply();
            camera.targetTexture = null;
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(renderTexture);
            return screenShot;
        }
    }

    [UnityEngine.Scripting.Preserve]
    public static class AngleExtensions
    {
        public static float FixAngle(this float angle, float min, float max)
        {
            if (angle < min)
            {
                angle += 360f;
            }

            if (angle > max)
            {
                angle -= 360f;
            }

            return angle;
        }

        public static float FixAngle180(this float angle)
        {
            if (angle < -180f)
            {
                angle += 360f;
            }

            if (angle > 180f)
            {
                angle -= 360f;
            }

            return angle;
        }

        public static float FixAngle(this float angle)
        {
            if (angle < -360f)
            {
                angle += 360f;
            }

            if (angle > 360f)
            {
                angle -= 360f;
            }

            return angle;
        }

        public static float FixAngle360(this float angle)
        {
            if (angle < 0f)
            {
                angle += 360f;
            }

            if (angle > 360f)
            {
                angle -= 360f;
            }

            return angle;
        }
    }
}
