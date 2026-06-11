using System;
using Cysharp.Text;
using UnityEngine;
using YooAsset;

namespace AlicizaX.Resource.Runtime
{
    internal partial class ResourceService
    {
        /// <summary>
        /// 根据运行模式创建初始化操作数据
        /// </summary>
        /// <returns></returns>
        private InitializationOperation CreateInitializationOperationHandler(ResourcePackage resourcePackage, string hostServerURL, string fallbackHostServerURL, string decryptionServicesName)
        {
            IDecryptionServices decryptionServices = CreateDecryptionServices(decryptionServicesName);
            InitializeParameters initializeParameters = null;
            switch (PlayMode)
            {
                case EPlayMode.EditorSimulateMode:
                {
                    initializeParameters = InitializeYooAssetEditorSimulateMode(DefaultPackageName);
                    break;
                }
                case EPlayMode.OfflinePlayMode:
                {
                    // 单机运行模式
                    initializeParameters = InitializeYooAssetOfflinePlayMode(decryptionServices);
                    break;
                }
                case EPlayMode.HostPlayMode:
                {
                    // 联机运行模式
                    initializeParameters = InitializeYooAssetHostPlayMode(hostServerURL, fallbackHostServerURL, decryptionServices);
                    break;
                }
                case EPlayMode.WebPlayMode:
                {
                    // WebGL运行模式
                    initializeParameters = InitializeYooAssetWebPlayMode(hostServerURL, fallbackHostServerURL);
                    break;
                }
            }


            if (initializeParameters == null) return null;

            initializeParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
            return resourcePackage.InitializeAsync(initializeParameters);
        }

        private IDecryptionServices CreateDecryptionServices(string decryptionServicesName)
        {
            IDecryptionServices decryptionServices = null;
            if (!string.IsNullOrEmpty(decryptionServicesName))
            {
                var decryptionServicesType = AlicizaX.Utility.Assembly.GetType(decryptionServicesName);
                decryptionServices = (IDecryptionServices)Activator.CreateInstance(decryptionServicesType);
            }

            return decryptionServices;
        }

        private InitializeParameters InitializeYooAssetEditorSimulateMode(string packageName)
        {
            var buildResult = EditorSimulateModeHelper.SimulateBuild(packageName);
            var packageRoot = buildResult.PackageRootDirectory;
            var initializeParameters = new EditorSimulateModeParameters();
            initializeParameters.EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);
            return initializeParameters;
        }

        private InitializeParameters InitializeYooAssetOfflinePlayMode(IDecryptionServices decryptionServices = null)
        {
            var buildinFileSystem = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(decryptionServices);
            var initializeParameters = new OfflinePlayModeParameters();
            initializeParameters.BuildinFileSystemParameters = buildinFileSystem;
            return initializeParameters;
        }

        private InitializeParameters InitializeYooAssetWebPlayMode(string hostServerURL, string fallbackHostServerURL)
        {
            var initializeParameters = new WebPlayModeParameters();
            FileSystemParameters webRemoteFileSystemParams = null;
            IRemoteServices remoteServices = new RemoteServices(hostServerURL, fallbackHostServerURL);
            var webServerFileSystemParams = FileSystemParameters.CreateDefaultWebServerFileSystemParameters();
#if UNITY_WEBGL
#if WEIXINMINIGAME


            // 小游戏缓存根目录
            // 注意：此处代码根据微信插件配置来填写！
            WeChatWASM.WXBase.PreloadConcurrent(10);
            string packageRoot = ZString.Concat(WeChatWASM.WX.env.USER_DATA_PATH, "/__GAME_FILE_CACHE/yoo");
            webRemoteFileSystemParams = WechatFileSystemCreater.CreateFileSystemParameters(packageRoot, remoteServices, null);
#endif

#endif
            initializeParameters.WebServerFileSystemParameters = webServerFileSystemParams;
            initializeParameters.WebRemoteFileSystemParameters = webRemoteFileSystemParams;
            return initializeParameters;
        }

        private InitializeParameters InitializeYooAssetHostPlayMode(string hostServerURL, string fallbackHostServerURL, IDecryptionServices decryptionServices = null)
        {
            IRemoteServices remoteServices = new RemoteServices(hostServerURL, fallbackHostServerURL);
            var cacheFileSystem = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices);
            var buildinFileSystem = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(decryptionServices);
            var initializeParameters = new HostPlayModeParameters();
            initializeParameters.BuildinFileSystemParameters = buildinFileSystem;
            initializeParameters.CacheFileSystemParameters = cacheFileSystem;
            return initializeParameters;
        }
    }
}
