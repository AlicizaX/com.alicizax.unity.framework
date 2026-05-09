using System;
using AlicizaX.ObjectPool;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX
{
    /// <summary>
    /// 基础组件。
    /// </summary>
    [DisallowMultipleComponent]
    [UnityEngine.Scripting.Preserve]
    public sealed class RootModule : AppServiceRoot
    {
        private static RootModule _instance = null;

        public static RootModule Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<RootModule>();
                }

                return _instance;
            }
        }

        private const int DEFAULT_DPI = 96;

        private float _gameSpeedBeforePause = 1f;

        [SerializeField] private int frameRate = 120;

        [SerializeField] private float gameSpeed = 1f;

        [SerializeField] private bool runInBackground = true;

        [SerializeField] private bool neverSleep = true;


        /// <summary>
        /// 获取或设置游戏帧率。
        /// </summary>
        public int FrameRate
        {
            get => frameRate;
            set => Application.targetFrameRate = frameRate = value;
        }

        /// <summary>
        /// 获取或设置游戏速度。
        /// </summary>
        public float GameSpeed
        {
            get => gameSpeed;
            set => Time.timeScale = gameSpeed = value >= 0f ? value : 0f;
        }

        /// <summary>
        /// 获取游戏是否暂停。
        /// </summary>
        public bool IsGamePaused => gameSpeed <= 0f;

        /// <summary>
        /// 获取是否正常游戏速度。
        /// </summary>
        public bool IsNormalGameSpeed => Math.Abs(gameSpeed - 1f) < 0.01f;

        /// <summary>
        /// 获取或设置是否允许后台运行。
        /// </summary>
        public bool RunInBackground
        {
            get => runInBackground;
            set => Application.runInBackground = runInBackground = value;
        }

        /// <summary>
        /// 获取或设置是否禁止休眠。
        /// </summary>
        public bool NeverSleep
        {
            get => neverSleep;
            set
            {
                neverSleep = value;
                Screen.sleepTimeout = value ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
            }
        }

        /// <summary>
        /// 暂停游戏。
        /// </summary>
        public void PauseGame()
        {
            if (IsGamePaused)
            {
                return;
            }

            _gameSpeedBeforePause = GameSpeed;
            GameSpeed = 0f;
        }

        /// <summary>
        /// 恢复游戏。
        /// </summary>
        public void ResumeGame()
        {
            if (!IsGamePaused)
            {
                return;
            }

            GameSpeed = _gameSpeedBeforePause;
        }

        /// <summary>
        /// 重置为正常游戏速度。
        /// </summary>
        public void ResetNormalGameSpeed()
        {
            if (IsNormalGameSpeed)
            {
                return;
            }

            GameSpeed = 1f;
        }


        /// <summary>
        /// 游戏框架组件初始化。
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            _instance = this;
            DontDestroyOnLoad(this);
            Utility.Unity.MakeEntity(transform);

            Log.Init();

            Log.Info("Game Version: {0}, Unity Version: {1}", AppVersion.GameVersion, Application.unityVersion);

            Utility.Converter.ScreenDpi = Screen.dpi;
            if (Utility.Converter.ScreenDpi <= 0)
            {
                Utility.Converter.ScreenDpi = DEFAULT_DPI;
            }

            Application.targetFrameRate = frameRate;
            Time.timeScale = gameSpeed;
            Application.runInBackground = runInBackground;
            Screen.sleepTimeout = neverSleep ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;

            Application.lowMemory += OnLowMemory;
        }

        private void OnApplicationQuit()
        {
            Application.lowMemory -= OnLowMemory;
            StopAllCoroutines();
            Shutdown();
        }


        private void Shutdown()
        {
            Destroy(gameObject);
            Utility.Unity.Shutdown();
            MemoryPool.ClearAll();
            Utility.Marshal.FreeCachedHGlobal();
        }

        private void OnLowMemory()
        {
            Log.Warning("Low memory reported...");

            IObjectPoolService objectPoolModule = AppServices.App.Require<IObjectPoolService>();
            if (objectPoolModule != null)
            {
                objectPoolModule.ReleaseAllUnused();
            }
        }
    }
}
