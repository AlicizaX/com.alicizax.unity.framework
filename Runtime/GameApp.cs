using AlicizaX;
using AlicizaX.Audio.Runtime;
using AlicizaX.Localization.Runtime;
using AlicizaX.ObjectPool;
using AlicizaX.Resource.Runtime;
using AlicizaX.Scene.Runtime;
using AlicizaX.Timer.Runtime;
using AlicizaX.UI.Runtime;

public static partial class GameApp
{
    /// <summary>
    /// 获取游戏基础组件。
    /// </summary>
    public static RootModule Base => RootModule.Instance;


    /// <summary>
    /// 获取声音组件。
    /// </summary>
    public static IAudioService Audio
    {
        get
        {
            if (_audio == null)
            {
                _audio = AppServices.RequireApp<IAudioService>();
            }

            return _audio;
        }
    }

    private static IAudioService _audio;


    /// <summary>
    /// 获取本地化组件。
    /// </summary>
    public static ILocalizationService Localization
    {
        get
        {
            if (_localization == null)
            {
                _localization = AppServices.RequireApp<ILocalizationService>();
            }

            return _localization;
        }
    }

    private static ILocalizationService _localization;

    /// <summary>
    /// 获取对象池组件。
    /// </summary>
    public static IObjectPoolService ObjectPool
    {
        get
        {
            if (_objectPool == null)
            {
                _objectPool = AppServices.RequireApp<IObjectPoolService>();
            }

            return _objectPool;
        }
    }

    private static IObjectPoolService _objectPool;


    /// <summary>
    /// 获取Asset组件。
    /// </summary>
    public static IResourceService Resource
    {
        get
        {
            if (_resource == null)
            {
                _resource = AppServices.RequireApp<IResourceService>();
            }

            return _resource;
        }
    }

    private static IResourceService _resource;

    /// <summary>
    /// 获取场景组件。
    /// </summary>
    public static ISceneService Scene
    {
        get
        {
            if (_scene == null)
            {
                _scene = AppServices.RequireApp<ISceneService>();
            }

            return _scene;
        }
    }

    private static ISceneService _scene;

    /// <summary>
    /// 获取定时器组件。
    /// </summary>
    public static ITimerService Timer
    {
        get
        {
            if (_timer == null)
            {
                _timer = AppServices.RequireApp<ITimerService>();
            }

            return _timer;
        }
    }

    private static ITimerService _timer;


    /// <summary>
    /// 获取UI组件。
    /// </summary>
    public static IUIService UI
    {
        get
        {
            if (_ui == null)
            {
                _ui = AppServices.RequireApp<IUIService>();
            }

            return _ui;
        }
    }

    private static IUIService _ui;

    /// <summary>
    /// 获取GameObjectPool组件。
    /// </summary>
    public static IGameObjectPoolService GameObjectPool
    {
        get
        {
            if (_gameObjectPool == null)
            {
                _gameObjectPool = AppServices.RequireApp<IGameObjectPoolService>();
            }

            return _gameObjectPool;
        }
    }

    private static IGameObjectPoolService _gameObjectPool;
}
