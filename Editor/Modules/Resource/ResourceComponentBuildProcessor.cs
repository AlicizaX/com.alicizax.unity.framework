using System;
using System.IO;
using AlicizaX.Resource.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using YooAsset;

namespace AlicizaX.Resource.Editor
{
    /// <summary>
    /// 本次 Player 构建要写进 ResourceComponent 的资源配置。
    /// 构建入口在 BuildPipeline.BuildPlayer 之前调用 <see cref="Set"/>，构建结束后（finally）调用 <see cref="Clear"/>。
    /// </summary>
    public static class ResourcePlayerBuildConfig
    {
        public static bool HasValue { get; private set; }

        public static EPlayMode PlayMode { get; private set; }

        /// <summary>为空时保留 Inspector 上的解密服务。</summary>
        public static string DecryptionServices { get; private set; }

        /// <summary>
        /// 本次构建中被写入配置的 ResourceComponent 数量。构建成功但为 0 说明场景走了增量缓存或场景里没有 ResourceComponent，
        /// 构建入口必须据此兜底，否则包里是上一次构建的值。
        /// </summary>
        public static int InjectedCount { get; internal set; }

        /// <summary>
        /// 设置本次构建的资源配置，并清掉 Player 场景数据缓存，保证构建时会重新处理场景。
        /// </summary>
        public static void Set(EPlayMode playMode, string decryptionServices)
        {
            if (!IsPlayerMode(playMode))
                throw new ArgumentException($"Player 构建不支持资源模式 {playMode}", nameof(playMode));

            PlayMode = playMode;
            DecryptionServices = decryptionServices ?? string.Empty;
            HasValue = true;
            InjectedCount = 0;
            InvalidatePlayerDataCache();
        }

        public static void Clear()
        {
            HasValue = false;
            PlayMode = default;
            DecryptionServices = null;
            InjectedCount = 0;
        }

        /// <summary>
        /// Unity 2022.3 的 Player 构建把场景数据缓存在 Library/PlayerDataCache，缓存键只看场景及依赖文件的内容 hash、BuildOptions 等，
        /// 不包含这里注入的值；命中缓存时不会调用 IProcessSceneWithReport，包里沿用上一次的值。
        /// 只删数据缓存，脚本 / IL2CPP 缓存（Library/Bee）不受影响。删除失败时由调用方通过 InjectedCount 兜底。
        /// </summary>
        private static void InvalidatePlayerDataCache()
        {
            string cacheRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "PlayerDataCache"));
            if (!Directory.Exists(cacheRoot))
                return;

            try
            {
                Directory.Delete(cacheRoot, true);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"[ResourceBuild] 清理 Player 数据缓存失败: {cacheRoot} ({error.Message})");
            }
        }

        internal static bool IsPlayerMode(EPlayMode mode)
        {
            return mode == EPlayMode.OfflinePlayMode
                   || mode == EPlayMode.HostPlayMode
                   || mode == EPlayMode.WebPlayMode;
        }
    }

    /// <summary>
    /// 构建时把 <see cref="ResourcePlayerBuildConfig"/> 写进构建场景里的 ResourceComponent。
    /// 修改的是场景的构建副本，磁盘上的场景和 Entry.prefab 不会变。
    /// </summary>
    internal sealed class ResourceComponentBuildProcessor : IProcessSceneWithReport
    {
        private const string PlayModeField = "_playMode";
        private const string DecryptionServicesField = "decryptionServices";

        public int callbackOrder => 0;

        public void OnProcessScene(UnityEngine.SceneManagement.Scene scene, BuildReport report)
        {
            // 进入 Play Mode 加载场景时 report 为 null，只在真正构建 Player 时处理。
            if (report == null)
                return;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (ResourceComponent component in root.GetComponentsInChildren<ResourceComponent>(true))
                {
                    Apply(component, scene);
                }
            }
        }

        private static void Apply(ResourceComponent component, UnityEngine.SceneManagement.Scene scene)
        {
            var serializedObject = new SerializedObject(component);
            SerializedProperty playMode = serializedObject.FindProperty(PlayModeField);
            SerializedProperty decryptionServices = serializedObject.FindProperty(DecryptionServicesField);
            if (playMode == null || decryptionServices == null)
            {
                throw new BuildFailedException(
                    $"ResourceComponent 找不到序列化字段 {PlayModeField}/{DecryptionServicesField}，字段改名后需同步更新 {nameof(ResourceComponentBuildProcessor)}");
            }

            if (!ResourcePlayerBuildConfig.HasValue)
            {
                // HybridCLR 生成 AOT dll 等工具也会跑 Player 构建，这里不能让构建失败，只做提示。
                var current = (EPlayMode)playMode.intValue;
                if (!ResourcePlayerBuildConfig.IsPlayerMode(current))
                {
                    Debug.LogWarning(
                        $"[ResourceBuild] {scene.path}: 本次构建未注入资源模式，ResourceComponent 保持 {current}，该模式不能在 Player 中运行。正式出包请走 BuildCLI。");
                }

                return;
            }

            playMode.intValue = (int)ResourcePlayerBuildConfig.PlayMode;
            if (!string.IsNullOrEmpty(ResourcePlayerBuildConfig.DecryptionServices))
                decryptionServices.stringValue = ResourcePlayerBuildConfig.DecryptionServices;

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            ResourcePlayerBuildConfig.InjectedCount++;

            Debug.Log(
                $"[ResourceBuild] {scene.path}: ResourceComponent 资源模式 = {(EPlayMode)playMode.intValue}，解密服务 = {decryptionServices.stringValue}");
        }
    }
}
