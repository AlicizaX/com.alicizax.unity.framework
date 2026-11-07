using System;
using AlicizaX;
using UnityEngine;
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

        private ResourceService _resourceService;

        private bool _forceUnloadUnusedAssets = false;

        private bool _forceSystemUnloadUnusedAssets = false;

        private bool _performGCCollect = false;

        private AsyncOperation _asyncOperation = null;

        private float _lastUnloadUnusedAssetsOperationElapseSeconds = 0f;

        private float _lastGCCollectElapseSeconds = float.MaxValue;

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

        [SerializeField] private float idleAssetExpireTime = 60f;
        [SerializeField] private int idleAssetCapacity = 256;

        [SerializeField] private int expireProcessCountPerFrame = 16;

        public float IdleAssetExpireTime
        {
            get => _resourceService.IdleAssetExpireTime;
            set => _resourceService.IdleAssetExpireTime = idleAssetExpireTime = value;
        }

        public int IdleAssetCapacity
        {
            get => _resourceService.IdleAssetCapacity;
            set => _resourceService.IdleAssetCapacity = idleAssetCapacity = value;
        }

        public int ExpireProcessCountPerFrame
        {
            get => _resourceService.IdleReleaseCountPerTick;
            set => _resourceService.IdleReleaseCountPerTick = expireProcessCountPerFrame = Mathf.Max(0, value);
        }
        #endregion

        private void OnValidate()
        {
            expireProcessCountPerFrame = Mathf.Max(0, expireProcessCountPerFrame);
        }


#if UNITY_EDITOR
        public static string PrefsKey = string.Concat(Application.dataPath.GetHashCode(), "GamePlayMode");
#endif

        private void Awake()
        {
            _resourceService = new ResourceService();
            AppServices.Register<IResourceService, IResourcePackageService>(_resourceService);

            Application.lowMemory += OnLowMemory;
#if UNITY_EDITOR
            _playMode = (EPlayMode)UnityEditor.EditorPrefs.GetInt(PrefsKey, (int)EPlayMode.EditorSimulateMode);
#endif
            _resourceService.DefaultPackageName = PackageName;
            _resourceService.DecryptionServices = decryptionServices;
            _resourceService.AutoUnloadBundleWhenUnused = autoUnloadBundleWhenUnused;
            _resourceService.PlayMode = _playMode;
            _resourceService.Milliseconds = milliseconds;
            _resourceService.DownloadingMaxNum = DownloadingMaxNum;
            _resourceService.FailedTryAgain = FailedTryAgain;
            _resourceService.Initialize();
            _resourceService.IdleAssetExpireTime = idleAssetExpireTime;
            _resourceService.IdleAssetCapacity = idleAssetCapacity;
            _resourceService.IdleReleaseCountPerTick = expireProcessCountPerFrame;
            _resourceService.SetForceUnloadUnusedAssetsAction(ForceUnloadUnusedAssets);
            Log.Info("ResourceModule Run Mode {0}", _playMode);
        }

        private void OnDestroy()
        {
            Application.lowMemory -= OnLowMemory;
            AppServices.Unregister(_resourceService);
            _resourceService = null;
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
            if (_asyncOperation == null &&
                (_forceUnloadUnusedAssets || _lastUnloadUnusedAssetsOperationElapseSeconds >= maxUnloadUnusedAssetsInterval))
            {
                bool releaseIdleAssets = _forceUnloadUnusedAssets;
                bool useSystemUnload = _forceSystemUnloadUnusedAssets && useSystemUnloadUnusedAssets;
                _forceUnloadUnusedAssets = false;
                _forceSystemUnloadUnusedAssets = false;
                _lastUnloadUnusedAssetsOperationElapseSeconds = 0f;
                _resourceService.UnloadUnusedAssets(releaseIdleAssets);
                _asyncOperation = useSystemUnload ? Resources.UnloadUnusedAssets() : null;
            }

            if (_asyncOperation is { isDone: true })
            {
                _asyncOperation = null;
            }

            if (_asyncOperation == null && _performGCCollect && _lastGCCollectElapseSeconds >= minGCCollectInterval)
            {
                Log.Info("GC.Collect...");
                _performGCCollect = false;
                _lastGCCollectElapseSeconds = 0f;
                GC.Collect();
            }
        }

        private void OnLowMemory()
        {
            _resourceService.OnLowMemory();
        }

        #endregion
    }
}
