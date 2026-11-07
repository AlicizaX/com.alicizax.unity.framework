using System;
using UnityEngine;

namespace AlicizaX
{
    /// <summary>
    /// 以 MonoBehaviour 形式存在的服务：Awake 注册、OnDestroy 注销，解析方式与普通 <see cref="IService"/> 完全一致。
    /// Unity 负责驱动 Update/LateUpdate/FixedUpdate，所以子类不能再实现 tick 生命周期接口。
    /// 子类重写 Awake/OnDestroy 时必须调用 base 对应实现。
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class MonoServiceBehaviour : MonoBehaviour, IMonoService
    {
        [SerializeField] private bool _dontDestroyOnLoad;

        protected virtual void Awake()
        {
            if (_dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);

            AppServices.Register(this, GetServiceContracts());
        }

        protected virtual void OnDestroy()
        {
            AppServices.Unregister(this);
        }

        /// <summary>
        /// 除具体类型外额外暴露的契约。默认只按具体类型可解析；
        /// 用 <see cref="MonoServiceBehaviour{TContract}"/> 或重写本方法声明接口契约。
        /// </summary>
        protected virtual Type[] GetServiceContracts() => Array.Empty<Type>();

        protected T Require<T>() where T : class, IService => AppServices.Require<T>();

        protected bool TryGet<T>(out T service) where T : class, IService => AppServices.TryGet(out service);
    }

    /// <summary>
    /// 额外暴露一个接口契约的 Mono 服务，等价于 <see cref="AppServices.Register{TContract}(IService)"/>。
    /// </summary>
    public abstract class MonoServiceBehaviour<TContract> : MonoServiceBehaviour
        where TContract : class, IService
    {
        protected override Type[] GetServiceContracts() => new[] { typeof(TContract) };
    }
}