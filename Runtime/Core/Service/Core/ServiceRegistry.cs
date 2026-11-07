using System;
using System.Collections.Generic;
using Cysharp.Text;

namespace AlicizaX
{
    /// <summary>
    /// 应用级服务容器：契约注册、解析、tick 驱动与销毁。
    /// 契约在注册边界校验一次，解析路径不再做类型兜底。
    /// tick 顺序等于注册顺序；注册立即生效（新服务从下一帧开始 tick），注销延迟到当前 tick 循环结束。
    /// </summary>
    public sealed class ServiceRegistry : IDisposable
    {
        // 键必须是引用类型（Type）。Mono 下 EqualityComparer<RuntimeTypeHandle>.Default 每次查找都会装箱
        // 一个 24 B 的值类型，解析落在热路径上时必须为 0 分配（见 ResolvingServicesDoesNotAllocate）。
        private readonly Dictionary<Type, IService> _servicesByContract = new Dictionary<Type, IService>();
        private readonly Dictionary<IService, Type[]> _contractsByService = new Dictionary<IService, Type[]>(ReferenceComparer<IService>.Instance);
        private readonly List<IService> _registrationOrder = new List<IService>();

        private readonly List<IServiceTickable> _tickables = new List<IServiceTickable>();
        private readonly List<IServiceLateTickable> _lateTickables = new List<IServiceLateTickable>();
        private readonly List<IServiceFixedTickable> _fixedTickables = new List<IServiceFixedTickable>();
        private readonly List<IServiceGizmoDrawable> _gizmoDrawables = new List<IServiceGizmoDrawable>();

        private readonly List<IService> _pendingRemovals = new List<IService>();

        // 只用于「每个服务只记录一次 tick 异常」；健康运行时应为空集，因此几乎不产生哈希查找成本。
        private readonly HashSet<object> _faultedServices = new HashSet<object>(ReferenceComparer<object>.Instance);

        private bool _isTicking;
        private bool _isDisposed;

        /// <summary>
        /// 只按服务实现类型注册，不附加显式契约。
        /// </summary>
        public IService Register(IService service) => Register(service, Array.Empty<Type>());

        /// <summary>
        /// 以「具体类型 + 显式契约」注册。契约必须由该实例实现，重复项自动去重。
        /// MonoBehaviour 服务（<see cref="IMonoService"/>）同样走这里，解析方式与普通服务一致。
        /// </summary>
        public IService Register(IService service, Type[] contracts)
        {
            if (contracts == null)
                throw new ArgumentNullException(nameof(contracts));

            RegisterCore(service, contracts);
            return service;
        }

        /// <summary>
        /// 以 <typeparamref name="TContract"/> 为主契约注册；服务实现类型同时可解析。
        /// </summary>
        public TContract Register<TContract>(IService service) where TContract : class, IService
        {
            RegisterCore(service, new[] { typeof(TContract) });
            return (TContract)service;
        }

        /// <summary>
        /// 以两个显式契约注册；服务实现类型同时可解析。
        /// </summary>
        public TContract Register<TContract, TExtraContract>(IService service)
            where TContract : class, IService
            where TExtraContract : class, IService
        {
            RegisterCore(service, new[] { typeof(TContract), typeof(TExtraContract) });
            return (TContract)service;
        }

        public bool Unregister(IService service)
        {
            if (service == null || _isDisposed || !_contractsByService.ContainsKey(service))
                return false;

            if (_isTicking)
            {
                // 遍历期间不能从 tick 列表移除：RemoveAt 会让后续服务跳过本帧。
                if (!ContainsReference(_pendingRemovals, service))
                    _pendingRemovals.Add(service);
                return true;
            }

            RemoveEntry(service);
            if (service is IServiceLifecycle lifecycle)
                lifecycle.Dispose();
            return true;
        }

        public bool TryGet<T>(out T service) where T : class, IService
        {
            if (_servicesByContract.TryGetValue(typeof(T), out var registered))
            {
                // 注册时已校验实例实现该契约，这里不再 as + null 兜底。
                service = (T)registered;
                return true;
            }

            service = null;
            return false;
        }

        public T Require<T>() where T : class, IService
        {
            if (TryGet(out T service)) return service;
            throw new InvalidOperationException(ZString.Format("Service {0} is not registered.", typeof(T).FullName));
        }

        internal void Tick(float deltaTime)
        {
            // 快照长度：tick 中注册的服务本帧不参与，避免刚刚 Initialize 的服务与正在运行的服务交错。
            var count = _tickables.Count;
            _isTicking = true;
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var service = _tickables[i];
                    try
                    {
                        service.Tick(deltaTime);
                    }
                    catch (Exception exception)
                    {
                        RecordFault(service, exception);
                    }
                }
            }
            finally
            {
                _isTicking = false;
                FlushPendingRemovals();
            }
        }

        internal void LateTick(float deltaTime)
        {
            var count = _lateTickables.Count;
            _isTicking = true;
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var service = _lateTickables[i];
                    try
                    {
                        service.LateTick(deltaTime);
                    }
                    catch (Exception exception)
                    {
                        RecordFault(service, exception);
                    }
                }
            }
            finally
            {
                _isTicking = false;
                FlushPendingRemovals();
            }
        }

        internal void FixedTick(float fixedDeltaTime)
        {
            var count = _fixedTickables.Count;
            _isTicking = true;
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var service = _fixedTickables[i];
                    try
                    {
                        service.FixedTick(fixedDeltaTime);
                    }
                    catch (Exception exception)
                    {
                        RecordFault(service, exception);
                    }
                }
            }
            finally
            {
                _isTicking = false;
                FlushPendingRemovals();
            }
        }

        internal void DrawGizmos()
        {
            var count = _gizmoDrawables.Count;
            _isTicking = true;
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var service = _gizmoDrawables[i];
                    try
                    {
                        service.DrawGizmos();
                    }
                    catch (Exception exception)
                    {
                        RecordFault(service, exception);
                    }
                }
            }
            finally
            {
                _isTicking = false;
                FlushPendingRemovals();
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            // 先置位再销毁：服务的 Dispose 若重入注册会立刻失败，
            // 否则新服务会被追加到正在倒序遍历的 _registrationOrder 尾部而永不销毁。
            _isDisposed = true;
            _isTicking = false;
            _pendingRemovals.Clear();

            for (var i = _registrationOrder.Count - 1; i >= 0; i--)
            {
                var service = _registrationOrder[i];
                RemoveEntry(service);

                if (!(service is IServiceLifecycle lifecycle)) continue;

                try
                {
                    lifecycle.Dispose();
                }
                catch (Exception exception)
                {
                    // 一个服务的销毁失败不能阻止其余服务销毁；记录后继续。
                    AlicizaX.Log.Exception(exception);
                }
            }

            _registrationOrder.Clear();
            _tickables.Clear();
            _lateTickables.Clear();
            _fixedTickables.Clear();
            _gizmoDrawables.Clear();
            _faultedServices.Clear();
        }

        private void RegisterCore(IService service, Type[] contracts)
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ServiceRegistry));

            if (_contractsByService.ContainsKey(service))
                throw new InvalidOperationException(ZString.Format("Service {0} is already registered.", service.GetType().FullName));

            ValidateServiceKind(service);

            var resolved = BuildContracts(service, contracts);
            ValidateContracts(resolved);
            AddEntry(service, resolved);

            if (!(service is IServiceLifecycle lifecycle)) return;

            try
            {
                lifecycle.Initialize(this);
            }
            catch
            {
                // 初始化失败时回滚契约与 tick 列表，再把异常交给注册调用方。
                RemoveEntry(service);
                throw;
            }
        }

        /// <summary>
        /// Mono 服务的 tick 由 Unity 驱动，注册时若同时实现 tick 接口会被驱动两次。
        /// </summary>
        private static void ValidateServiceKind(IService service)
        {
            if (!(service is IMonoService)) return;
            if (!(service is IServiceTickable ||
                  service is IServiceLateTickable ||
                  service is IServiceFixedTickable ||
                  service is IServiceGizmoDrawable)) return;

            throw new InvalidOperationException(ZString.Format(
                "{0} is a Mono service; Unity drives its tick, so it cannot implement tick lifecycle interfaces.",
                service.GetType().FullName));
        }

        /// <summary>
        /// 契约 = 具体类型 + 请求的契约（去重、并校验实例确实实现该契约）。注册是冷路径：
        /// 每个服务一个 <see cref="Type"/>[]（通常 1~3 项，24~40 B），由容器在注销/销毁时丢弃。
        /// </summary>
        private static Type[] BuildContracts(IService service, Type[] requested)
        {
            var contracts = new Type[requested.Length + 1];
            contracts[0] = service.GetType();
            var count = 1;

            for (var i = 0; i < requested.Length; i++)
            {
                var contract = requested[i];
                if (contract == null)
                    throw new ArgumentException("Contract type cannot be null.", nameof(requested));
                if (ContainsType(contracts, count, contract)) continue;

                if (!contract.IsInstanceOfType(service))
                    throw new ArgumentException(
                        ZString.Format("{0} does not implement contract {1}.", service.GetType().FullName, contract.FullName),
                        nameof(service));

                contracts[count++] = contract;
            }

            if (count == contracts.Length) return contracts;

            Array.Resize(ref contracts, count);
            return contracts;
        }

        private static bool ContainsType(Type[] types, int count, Type type)
        {
            for (var i = 0; i < count; i++)
            {
                if (types[i] == type) return true;
            }

            return false;
        }

        private void ValidateContracts(Type[] contracts)
        {
            for (var i = 0; i < contracts.Length; i++)
            {
                if (!_servicesByContract.TryGetValue(contracts[i], out var existing)) continue;

                throw new InvalidOperationException(
                    ZString.Format("Contract {0} is already bound to {1}.", contracts[i].FullName, existing.GetType().FullName));
            }
        }

        private void AddEntry(IService service, Type[] contracts)
        {
            _faultedServices.Remove(service);
            _contractsByService.Add(service, contracts);
            _registrationOrder.Add(service);

            for (var i = 0; i < contracts.Length; i++)
                _servicesByContract.Add(contracts[i], service);

            AddToTickLists(service);
        }

        private void RemoveEntry(IService service)
        {
            if (!_contractsByService.TryGetValue(service, out var contracts)) return;

            _contractsByService.Remove(service);
            _faultedServices.Remove(service);
            RemoveFromTickLists(service);
            RemoveReference(_registrationOrder, service);

            for (var i = 0; i < contracts.Length; i++)
                _servicesByContract.Remove(contracts[i]);
        }

        private void AddToTickLists(IService service)
        {
            if (service is IServiceTickable tickable) _tickables.Add(tickable);
            if (service is IServiceLateTickable lateTickable) _lateTickables.Add(lateTickable);
            if (service is IServiceFixedTickable fixedTickable) _fixedTickables.Add(fixedTickable);
            if (service is IServiceGizmoDrawable gizmoDrawable) _gizmoDrawables.Add(gizmoDrawable);
        }

        private void RemoveFromTickLists(IService service)
        {
            if (service is IServiceTickable tickable) RemoveReference(_tickables, tickable);
            if (service is IServiceLateTickable lateTickable) RemoveReference(_lateTickables, lateTickable);
            if (service is IServiceFixedTickable fixedTickable) RemoveReference(_fixedTickables, fixedTickable);
            if (service is IServiceGizmoDrawable gizmoDrawable) RemoveReference(_gizmoDrawables, gizmoDrawable);
        }

        private void FlushPendingRemovals()
        {
            if (_pendingRemovals.Count == 0) return;

            try
            {
                for (var i = 0; i < _pendingRemovals.Count; i++)
                {
                    var service = _pendingRemovals[i];
                    if (!_contractsByService.ContainsKey(service)) continue;

                    RemoveEntry(service);

                    if (!(service is IServiceLifecycle lifecycle)) continue;

                    try
                    {
                        lifecycle.Dispose();
                    }
                    catch (Exception exception)
                    {
                        // 注销发生在 tick 内的其他服务里；一个服务的销毁失败不应中断本帧剩余注销。
                        AlicizaX.Log.Exception(exception);
                    }
                }
            }
            finally
            {
                _pendingRemovals.Clear();
            }
        }

        private void RecordFault(object service, Exception exception)
        {
            // 每个服务只记录首次异常：连续失败的服务不刷屏，也不中断其余服务；
            // 该服务仍会继续 tick，由服务自身保证失败后状态可继续使用。
            if (_faultedServices.Add(service))
                AlicizaX.Log.Exception(exception);
        }

        private static bool ContainsReference<T>(List<T> list, T item) where T : class
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], item)) return true;
            }

            return false;
        }

        /// <summary>
        /// 注销只发生在模块/场景销毁，n 为 0~几十，线性移除的正确性远比 O(1) 的索引簿记重要。
        /// </summary>
        private static void RemoveReference<T>(List<T> list, T item) where T : class
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (!ReferenceEquals(list[i], item)) continue;
                list.RemoveAt(i);
                return;
            }
        }
    }
}
