using System;
using Cysharp.Text;
using AlicizaX;
using UnityEngine;
using UnityEngine.Serialization;
using YooAsset;

namespace AlicizaX.Resource.Runtime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game Framework/Resource")]
    [UnityEngine.Scripting.Preserve]
    [DefaultExecutionOrder(-700)]
    public class ResourceComponent : MonoBehaviour
    {
        #region Propreties

        private const int DEFAULT_PRIORITY = 0;

        private IResourceService _resourceService;

        private bool _forceUnloadUnusedAssets = false;

        private bool _forceSystemUnloadUnusedAssets = false;

        private bool _preorderUnloadUnusedAssets = false;

        private bool _performGCCollect = false;

        private AsyncOperation _asyncOperation = null;

        private float _lastUnloadUnusedAssetsOperationElapseSeconds = 0f;

        private float _lastGCCollectElapseSeconds = float.MaxValue;

        [SerializeField] private float minUnloadUnusedAssetsInterval = 60f;

        [SerializeField] private float maxUnloadUnusedAssetsInterval = 300f;

        [SerializeField] private bool useSystemUnloadUnusedAssets = true;

        [SerializeField] private float minGCCollectInterval = 30f;

        [SerializeField] private string decryptionServices = "";

        [SerializeField] public bool autoUnloadBundleWhenUnused = false;

        [SerializeField] private EPlayMode _playMode = EPlayMode.EditorSimulateMode;

        public string PackageVersion { set; get; }

        [SerializeField] private string packageName = "DefaultPackage";

        public string PackageName
        {
            get => packageName;
            set => packageName = value;
        }


        [SerializeField] public long milliseconds = 30;

        public int downloadingMaxNum = 10;


        public int DownloadingMaxNum
        {
            get => downloadingMaxNum;
            set => downloadingMaxNum = value;
        }

        [SerializeField] public int failedTryAgain = 3;

        public int FailedTryAgain
        {
            get => failedTryAgain;
            set => failedTryAgain = value;
        }


        public string ApplicableGameVersion => _resourceService.ApplicableGameVersion;


        public int InternalResourceVersion => _resourceService.InternalResourceVersion;

        public float MinUnloadUnusedAssetsInterval
        {
            get => minUnloadUnusedAssetsInterval;
            set => minUnloadUnusedAssetsInterval = value;
        }

        public float MaxUnloadUnusedAssetsInterval
        {
            get => maxUnloadUnusedAssetsInterval;
            set => maxUnloadUnusedAssetsInterval = value;
        }


        public bool UseSystemUnloadUnusedAssets
        {
            get => useSystemUnloadUnusedAssets;
            set => useSystemUnloadUnusedAssets = value;
        }

        public float LastUnloadUnusedAssetsOperationElapseSeconds => _lastUnloadUnusedAssetsOperationElapseSeconds;

        [SerializeField] private float assetAutoReleaseInterval = 60f;

        [SerializeField] private int assetCapacity = 64;

        [SerializeField] private float assetExpireTime = 60f;

        [SerializeField] private int assetPriority = 0;


        public float AssetAutoReleaseInterval
        {
            get => _resourceService.AssetAutoReleaseInterval;
            set => _resourceService.AssetAutoReleaseInterval = assetAutoReleaseInterval = value;
        }


        public int AssetCapacity
        {
            get => _resourceService.AssetCapacity;
            set => _resourceService.AssetCapacity = assetCapacity = value;
        }


        public float AssetExpireTime
        {
            get => _resourceService.AssetExpireTime;
            set => _resourceService.AssetExpireTime = assetExpireTime = value;
        }


        public int AssetPriority
        {
            get => _resourceService.AssetPriority;
            set => _resourceService.AssetPriority = assetPriority = value;
        }

        #endregion

        internal void SetPlayMode(int playMode)
        {
            _playMode = (EPlayMode)playMode;
        }

        internal void SetDecryptionServices(string decryption)
        {
            decryptionServices = decryption;
        }


#if UNITY_EDITOR
        public static string PrefsKey = ZString.Concat(Application.dataPath.GetHashCode(), "GamePlayMode");
#endif

        private void Awake()
        {
            _resourceService = AppServices.App.Register<IResourceService>(new ResourceService());

            Application.lowMemory += OnLowMemory;
#if UNITY_EDITOR
            _playMode = (EPlayMode)UnityEditor.EditorPrefs.GetInt(PrefsKey, 0);
#endif
            _resourceService.DefaultPackageName = PackageName;
            _resourceService.DecryptionServices = decryptionServices;
            _resourceService.AutoUnloadBundleWhenUnused = autoUnloadBundleWhenUnused;
            _resourceService.PlayMode = _playMode;
            _resourceService.Milliseconds = milliseconds;
            _resourceService.DownloadingMaxNum = DownloadingMaxNum;
            _resourceService.FailedTryAgain = FailedTryAgain;
            _resourceService.Initialize();
            _resourceService.AssetAutoReleaseInterval = assetAutoReleaseInterval;
            _resourceService.AssetCapacity = assetCapacity;
            _resourceService.AssetExpireTime = assetExpireTime;
            _resourceService.AssetPriority = assetPriority;
            _resourceService.SetForceUnloadUnusedAssetsAction(ForceUnloadUnusedAssets);
            Log.Info(ZString.Format("ResourceModule Run Mode {0}", _playMode));
        }

        private void OnApplicationQuit()
        {
            Application.lowMemory -= OnLowMemory;
        }

        #region 释放资源

        public void ForceUnloadUnusedAssets(bool performGCCollect)
        {
            _forceUnloadUnusedAssets = true;
            if (performGCCollect)
            {
                _performGCCollect = true;
                _forceSystemUnloadUnusedAssets = true;
            }
        }


        private void Update()
        {
            _lastUnloadUnusedAssetsOperationElapseSeconds += Time.unscaledDeltaTime;
            _lastGCCollectElapseSeconds += Time.unscaledDeltaTime;
            if (_asyncOperation == null && (_forceUnloadUnusedAssets || _lastUnloadUnusedAssetsOperationElapseSeconds >= maxUnloadUnusedAssetsInterval ||
                                            _preorderUnloadUnusedAssets && _lastUnloadUnusedAssetsOperationElapseSeconds >= minUnloadUnusedAssetsInterval))
            {
                bool useSystemUnload = _forceSystemUnloadUnusedAssets && useSystemUnloadUnusedAssets;
                _forceUnloadUnusedAssets = false;
                _forceSystemUnloadUnusedAssets = false;
                _preorderUnloadUnusedAssets = false;
                _lastUnloadUnusedAssetsOperationElapseSeconds = 0f;
                _resourceService.UnloadUnusedAssets();
                _asyncOperation = useSystemUnload ? Resources.UnloadUnusedAssets() : null;
            }

            if (_asyncOperation == null && _performGCCollect)
            {
                TryCollectGarbage();
            }

            if (_asyncOperation is { isDone: true })
            {
                _asyncOperation = null;
                if (_performGCCollect)
                {
                    TryCollectGarbage();
                }
            }
        }

        private void TryCollectGarbage()
        {
            if (_lastGCCollectElapseSeconds < minGCCollectInterval)
            {
                return;
            }

            Log.Info("GC.Collect...");
            _performGCCollect = false;
            _lastGCCollectElapseSeconds = 0f;
            GC.Collect();
        }

        private void OnLowMemory()
        {
            Log.Warning("Low memory reported...");
            if (_resourceService != null)
            {
                _resourceService.OnLowMemory();
            }
        }

        #endregion
    }
}
