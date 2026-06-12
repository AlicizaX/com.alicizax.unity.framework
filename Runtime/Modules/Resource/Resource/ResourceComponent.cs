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

        [SerializeField] private int assetRecordCapacity = 64;

        [SerializeField] private int assetLeaseCapacity = 128;

        [SerializeField] private int bindingOwnerCapacity = 64;

        [SerializeField] private int bindingSlotCapacity = 128;

        [SerializeField] private int registeredTargetCapacity = 128;

        [SerializeField] private float idleAssetExpireTime = 60f;

        [SerializeField] private int expireProcessCountPerFrame = 16;

        [SerializeField] private int expireProcessCountWhenUnloading = 256;

        public int AssetRecordCapacity
        {
            get => _resourceService.AssetRecordCapacity;
            set => _resourceService.AssetRecordCapacity = assetRecordCapacity = value;
        }

        public int AssetLeaseCapacity
        {
            get => _resourceService.AssetLeaseCapacity;
            set => _resourceService.AssetLeaseCapacity = assetLeaseCapacity = value;
        }

        public int BindingOwnerCapacity
        {
            get => _resourceService.BindingOwnerCapacity;
            set => _resourceService.BindingOwnerCapacity = bindingOwnerCapacity = value;
        }

        public int BindingSlotCapacity
        {
            get => _resourceService.BindingSlotCapacity;
            set => _resourceService.BindingSlotCapacity = bindingSlotCapacity = value;
        }

        public int RegisteredTargetCapacity
        {
            get => _resourceService.RegisteredTargetCapacity;
            set => _resourceService.RegisteredTargetCapacity = registeredTargetCapacity = value;
        }

        public float IdleAssetExpireTime
        {
            get => _resourceService.IdleAssetExpireTime;
            set => _resourceService.IdleAssetExpireTime = idleAssetExpireTime = value;
        }

        public int ExpireProcessCountPerFrame
        {
            get => expireProcessCountPerFrame;
            set => expireProcessCountPerFrame = Mathf.Max(0, value);
        }

        public int ExpireProcessCountWhenUnloading
        {
            get => expireProcessCountWhenUnloading;
            set => expireProcessCountWhenUnloading = Mathf.Max(expireProcessCountPerFrame, value);
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

        private void OnValidate()
        {
            expireProcessCountPerFrame = Mathf.Max(0, expireProcessCountPerFrame);
            expireProcessCountWhenUnloading = Mathf.Max(expireProcessCountPerFrame, expireProcessCountWhenUnloading);
        }


#if UNITY_EDITOR
        public static string PrefsKey = string.Concat(Application.dataPath.GetHashCode(), "GamePlayMode");
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
            _resourceService.AssetRecordCapacity = assetRecordCapacity;
            _resourceService.AssetLeaseCapacity = assetLeaseCapacity;
            _resourceService.BindingOwnerCapacity = bindingOwnerCapacity;
            _resourceService.BindingSlotCapacity = bindingSlotCapacity;
            _resourceService.RegisteredTargetCapacity = registeredTargetCapacity;
            _resourceService.IdleAssetExpireTime = idleAssetExpireTime;
            _resourceService.SetForceUnloadUnusedAssetsAction(ForceUnloadUnusedAssets);
            Log.Info("ResourceModule Run Mode {0}", _playMode);
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
            bool shouldUnloadUnusedAssets = _asyncOperation == null &&
                                            (_forceUnloadUnusedAssets ||
                                             _lastUnloadUnusedAssetsOperationElapseSeconds >= maxUnloadUnusedAssetsInterval ||
                                             _preorderUnloadUnusedAssets && _lastUnloadUnusedAssetsOperationElapseSeconds >= minUnloadUnusedAssetsInterval);

            if (_resourceService is ResourceService resourceService)
            {
                int expireProcessCount = shouldUnloadUnusedAssets
                    ? Mathf.Max(expireProcessCountPerFrame, expireProcessCountWhenUnloading)
                    : Mathf.Max(0, expireProcessCountPerFrame);
                resourceService.ProcessKeepAlive(Time.unscaledTime, expireProcessCount);
            }

            _lastUnloadUnusedAssetsOperationElapseSeconds += Time.unscaledDeltaTime;
            _lastGCCollectElapseSeconds += Time.unscaledDeltaTime;
            if (shouldUnloadUnusedAssets)
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
