using AlicizaX;
using AlicizaX.Audio.Runtime;
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
                _audio = AppServices.Require<IAudioService>();
            }

            return _audio;
        }
    }

    private static IAudioService _audio;


    /// <summary>
    /// 获取Asset组件。
    /// </summary>
    public static IResourceService Resource
    {
        get
        {
            if (_resource == null)
            {
                _resource = AppServices.Require<IResourceService>();
            }

            return _resource;
        }
    }

    private static IResourceService _resource;

    public static IResourcePackageService ResourcePackage
    {
        get
        {
            if (_resourcePackage == null)
            {
                _resourcePackage = AppServices.Require<IResourcePackageService>();
            }

            return _resourcePackage;
        }
    }

    private static IResourcePackageService _resourcePackage;

    /// <summary>
    /// 获取场景组件。
    /// </summary>
    public static ISceneService Scene
    {
        get
        {
            if (_scene == null)
            {
                _scene = AppServices.Require<ISceneService>();
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
                _timer = AppServices.Require<ITimerService>();
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
                _ui = AppServices.Require<IUIService>();
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
                _gameObjectPool = AppServices.Require<IGameObjectPoolService>();
            }

            return _gameObjectPool;
        }
    }

    private static IGameObjectPoolService _gameObjectPool;

    /// <summary>
    /// 清空静态缓存。服务世界销毁后必须调用，否则旧服务实例会一直被静态字段 root 住，
    /// 且下次读取拿到的会是被销毁的实例。
    /// </summary>
    internal static void ResetCachedServices()
    {
        _audio = null;
        _resource = null;
        _resourcePackage = null;
        _scene = null;
        _timer = null;
        _ui = null;
        _gameObjectPool = null;
    }
}
