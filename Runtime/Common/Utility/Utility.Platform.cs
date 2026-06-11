using UnityEngine;

namespace AlicizaX
{
    public static partial class Utility
    {
        /// <summary>
        /// 应用和平台相关的实用函数。
        /// </summary>
        public static class Platform
        {
            /// <summary>
            /// 是否是编辑器。
            /// </summary>
            public static bool IsEditor
            {
                get
                {
#if UNITY_EDITOR
                    return true;
#else
                    return false;
#endif
                }
            }

            /// <summary>
            /// 是否是安卓。
            /// </summary>
            public static bool IsAndroid
            {
                get
                {
#if UNITY_ANDROID
                    return true;
#else
                    return false;
#endif
                }
            }

            /// <summary>
            /// 是否是 WebGL 平台。
            /// </summary>
            public static bool IsWebGL => Application.platform == RuntimePlatform.WebGLPlayer;

            /// <summary>
            /// 是否是 Windows 平台。
            /// </summary>
            public static bool IsWindows => Application.platform == RuntimePlatform.WindowsPlayer;

            /// <summary>
            /// 是否是 Linux 平台。
            /// </summary>
            public static bool IsLinux => Application.platform == RuntimePlatform.LinuxPlayer;

            /// <summary>
            /// 是否是 Mac 平台。
            /// </summary>
            public static bool IsMacOsx => Application.platform == RuntimePlatform.OSXPlayer;

            /// <summary>
            /// 是否是 iOS 平台。
            /// </summary>
            public static bool IsIOS
            {
                get
                {
#if UNITY_IOS
                    return true;
#else
                    return false;
#endif
                }
            }

            /// <summary>
            /// 退出应用。
            /// </summary>
            public static void Quit()
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }

#if UNITY_IOS
            [System.Runtime.InteropServices.DllImport("__Internal")]
            private static extern void open_url(string url);
#endif

            /// <summary>
            /// 打开 URL。
            /// </summary>
            public static void OpenURL(string url)
            {
#if UNITY_EDITOR
                Application.OpenURL(url);
#elif UNITY_IOS
                open_url(url);
#else
                Application.OpenURL(url);
#endif
            }
        }
    }
}
