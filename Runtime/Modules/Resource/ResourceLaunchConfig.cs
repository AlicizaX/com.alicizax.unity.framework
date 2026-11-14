using System;
using UnityEngine;
using YooAsset;

namespace AlicizaX.Resource.Runtime
{
    /// <summary>
    /// 构建时写入 Resources 的启动配置，用于覆盖 ResourceComponent 上的序列化设置。
    /// </summary>
    [Serializable]
    public sealed class ResourceLaunchConfig
    {
        public int playMode;
        public string decryptionServices;
    }

    /// <summary>
    /// 读取 Resources 下的启动配置；文件缺失或内容非法时返回 false，调用方保留 Inspector 上的值。
    /// </summary>
    public static class ResourceLaunchConfigLoader
    {
        public const string ResourcePath = "AlicizaX/launch";

        public static bool TryLoad(out ResourceLaunchConfig config)
        {
            config = null;

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
                return false;

            ResourceLaunchConfig parsed;
            try
            {
                parsed = JsonUtility.FromJson<ResourceLaunchConfig>(asset.text);
            }
            catch (Exception)
            {
                return false;
            }

            if (parsed == null || !IsSupportedBuildMode(parsed.playMode))
                return false;

            config = parsed;
            return true;
        }

        private static bool IsSupportedBuildMode(int value)
        {
            EPlayMode mode = (EPlayMode)value;
            return mode == EPlayMode.OfflinePlayMode
                   || mode == EPlayMode.HostPlayMode
                   || mode == EPlayMode.WebPlayMode;
        }
    }
}
