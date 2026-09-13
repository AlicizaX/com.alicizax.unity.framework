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
        private InitializePackageOperation CreateInitializationOperationHandler(ResourcePackage resourcePackage, string hostServerURL, string fallbackHostServerURL, string decryptionServicesName)
        {
            IBundleDecryptor bundleDecryptor = CreateBundleDecryptor(decryptionServicesName);
            InitializePackageOptions initializeOptions = null;
            switch (PlayMode)
            {
                case EPlayMode.EditorSimulateMode:
                {
                    initializeOptions = InitializeYooAssetEditorSimulateMode(resourcePackage.PackageName);
                    break;
                }
                case EPlayMode.OfflinePlayMode:
                {
                    // 单机运行模式
                    initializeOptions = InitializeYooAssetOfflinePlayMode(bundleDecryptor);
                    break;
                }
                case EPlayMode.HostPlayMode:
                {
                    // 联机运行模式
                    initializeOptions = InitializeYooAssetHostPlayMode(hostServerURL, fallbackHostServerURL, bundleDecryptor);
                    break;
                }
                case EPlayMode.WebPlayMode:
                {
                    // WebGL运行模式
                    initializeOptions = InitializeYooAssetWebPlayMode(hostServerURL, fallbackHostServerURL, bundleDecryptor);
                    break;
                }
            }


            if (initializeOptions == null) return null;

            initializeOptions.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
            return resourcePackage.InitializePackageAsync(initializeOptions);
        }

        private IBundleDecryptor CreateBundleDecryptor(string decryptionServicesName)
        {
            if (string.IsNullOrEmpty(decryptionServicesName))
            {
                return null;
            }

            var decryptorType = AlicizaX.Utility.Assembly.GetType(decryptionServicesName);
            return (IBundleDecryptor)Activator.CreateInstance(decryptorType);
        }

        private InitializePackageOptions InitializeYooAssetEditorSimulateMode(string packageName)
        {
            var buildResult = EditorSimulateBuildInvoker.Build(packageName, (int)EBundleType.VirtualAssetBundle);
            var packageRoot = buildResult.PackageRootDirectory;
            return new EditorSimulateModeOptions
            {
                EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot)
            };
        }

        private InitializePackageOptions InitializeYooAssetOfflinePlayMode(IBundleDecryptor bundleDecryptor)
        {
            var builtinFileSystem = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
            ConfigureBundleDecryptor(builtinFileSystem, bundleDecryptor);
            return new OfflinePlayModeOptions
            {
                BuiltinFileSystemParameters = builtinFileSystem
            };
        }

        private InitializePackageOptions InitializeYooAssetWebPlayMode(string hostServerURL, string fallbackHostServerURL, IBundleDecryptor bundleDecryptor)
        {
            FileSystemParameters webNetworkFileSystemParameters = null;
            IRemoteService remoteService = new RemoteServices(hostServerURL, fallbackHostServerURL);
            var webServerFileSystemParams = FileSystemParameters.CreateDefaultWebServerFileSystemParameters();
#if UNITY_WEBGL
#if WEIXINMINIGAME


            // 小游戏缓存根目录
            // 注意：此处代码根据微信插件配置来填写！
            WeChatWASM.WXBase.PreloadConcurrent(10);
            string packageRoot = ZString.Concat(WeChatWASM.WX.env.USER_DATA_PATH, "/__GAME_FILE_CACHE/yoo");
            webNetworkFileSystemParameters = WechatFileSystemCreater.CreateFileSystemParameters(packageRoot, remoteService, bundleDecryptor);
#endif

#endif
            return new WebPlayModeOptions
            {
                WebServerFileSystemParameters = webServerFileSystemParams,
                WebNetworkFileSystemParameters = webNetworkFileSystemParameters
            };
        }

        private InitializePackageOptions InitializeYooAssetHostPlayMode(string hostServerURL, string fallbackHostServerURL, IBundleDecryptor bundleDecryptor)
        {
            IRemoteService remoteService = new RemoteServices(hostServerURL, fallbackHostServerURL);
            var cacheFileSystem = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService);
            var builtinFileSystem = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
            ConfigureBundleDecryptor(cacheFileSystem, bundleDecryptor);
            ConfigureBundleDecryptor(builtinFileSystem, bundleDecryptor);
            return new HostPlayModeOptions
            {
                BuiltinFileSystemParameters = builtinFileSystem,
                CacheFileSystemParameters = cacheFileSystem
            };
        }

        private static void ConfigureBundleDecryptor(FileSystemParameters fileSystemParameters, IBundleDecryptor bundleDecryptor)
        {
            if (bundleDecryptor == null)
            {
                return;
            }

            fileSystemParameters.AddParameter(EFileSystemParameter.AssetBundleDecryptor, bundleDecryptor);
            if (bundleDecryptor is IBundleMemoryDecryptor fallbackDecryptor)
            {
                fileSystemParameters.AddParameter(EFileSystemParameter.AssetBundleFallbackDecryptor, fallbackDecryptor);
            }
        }
    }
}
