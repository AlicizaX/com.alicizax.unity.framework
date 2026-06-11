using System.IO;

using System.Text;
using UnityEngine;

namespace AlicizaX
{
    public static partial class Utility
    {
        /// <summary>
        /// 路径相关的实用函数。
        /// </summary>
        public static class Path
        {
            private static readonly StringBuilder CombineStringBuilder = new StringBuilder();

            /// <summary>
            /// 应用程序外部资源路径存放路径(热更新资源路径)。
            /// </summary>
            public static string AppHotfixResPath
            {
                get
                {
                    string game = Application.productName;
                    return $"{Application.persistentDataPath}/{game}/";
                }
            }

            /// <summary>
            /// 应用程序内部资源路径存放路径。
            /// </summary>
            public static string AppResPath => GetRegularPath(Application.streamingAssetsPath);

            /// <summary>
            /// 应用程序内部资源路径存放路径(www/webrequest专用)。
            /// </summary>
            public static string AppResPath4Web
            {
                get
                {
#if UNITY_IOS || UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN || UNITY_EDITOR
                    return $"file://{Application.streamingAssetsPath}";
#else
                    return GetRegularPath(Application.streamingAssetsPath);
#endif
                }
            }

            /// <summary>
            /// 获取平台名称。
            /// </summary>
            public static string PlatformName
            {
                get
                {
#if UNITY_ANDROID
                    return "Android";
#elif UNITY_STANDALONE_OSX
                    return "MacOs";
#elif UNITY_IOS || UNITY_IPHONE
                    return "iOS";
#elif UNITY_WEBGL
                    return "WebGL";
#elif UNITY_STANDALONE_WIN
                    return "Windows";
#else
                    return string.Empty;
#endif
                }
            }
            /// <summary>
            /// 获取规范的路径。
            /// </summary>
            /// <param name="path">要规范的路径。</param>
            /// <returns>规范的路径。</returns>
            public static string GetRegularPath(string path)
            {
                if (path == null)
                {
                    return null;
                }

                return path.Replace('\\', '/');
            }

            /// <summary>
            /// 获取规范的路径。
            /// </summary>
            /// <param name="path">要规范的路径。</param>
            /// <returns>规范的路径。</returns>
            public static string NormalizePath(string path)
            {
                return GetRegularPath(path);
            }

            /// <summary>
            /// 获取远程格式的路径（带有file:// 或 http:// 前缀）。
            /// </summary>
            /// <param name="path">原始路径。</param>
            /// <returns>远程格式路径。</returns>
            public static string GetRemotePath(string path)
            {
                string regularPath = GetRegularPath(path);
                if (regularPath == null)
                {
                    return null;
                }

                return regularPath.Contains("://") ? regularPath : ("file:///" + regularPath).Replace("file:////", "file:///");
            }

            /// <summary>
            /// 拼接路径。
            /// </summary>
            public static string Combine(params string[] paths)
            {
                if (paths == null || paths.Length == 0)
                {
                    return string.Empty;
                }

                CombineStringBuilder.Clear();
                for (int index = 0; index < paths.Length - 1; index++)
                {
                    string path = paths[index];
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    CombineStringBuilder.Append(path);
                    char lastChar = path[path.Length - 1];
                    if (lastChar != '/' && lastChar != '\\')
                    {
                        CombineStringBuilder.Append('/');
                    }
                }

                CombineStringBuilder.Append(paths[paths.Length - 1]);
                return CombineStringBuilder.ToString();
            }

            /// <summary>
            /// 移除空文件夹。
            /// </summary>
            /// <param name="directoryName">要处理的文件夹名称。</param>
            /// <returns>是否移除空文件夹成功。</returns>
            public static bool RemoveEmptyDirectory(string directoryName)
            {
                if (string.IsNullOrEmpty(directoryName))
                {
                    throw new GameFrameworkException("Directory name is invalid.");
                }

                try
                {
                    if (!Directory.Exists(directoryName))
                    {
                        return false;
                    }

                    // 不使用 SearchOption.AllDirectories，以便于在可能产生异常的环境下删除尽可能多的目录
                    string[] subDirectoryNames = Directory.GetDirectories(directoryName, "*");
                    int subDirectoryCount = subDirectoryNames.Length;
                    foreach (string subDirectoryName in subDirectoryNames)
                    {
                        if (RemoveEmptyDirectory(subDirectoryName))
                        {
                            subDirectoryCount--;
                        }
                    }

                    if (subDirectoryCount > 0)
                    {
                        return false;
                    }

                    if (Directory.GetFiles(directoryName, "*").Length > 0)
                    {
                        return false;
                    }

                    Directory.Delete(directoryName);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
