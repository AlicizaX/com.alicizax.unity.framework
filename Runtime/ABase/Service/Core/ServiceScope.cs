using System;
using System.Collections.Generic;
using Cysharp.Text;

namespace AlicizaX
{
    internal sealed class ServiceScope : IDisposable, IServiceRegistry
    {
        private const int MissingIndex = -1;

        private readonly Dictionary<RuntimeTypeHandle, IService> _servicesByContract = new Dictionary<RuntimeTypeHandle, IService>();
        private readonly Dictionary<IService, ServiceEntry> _entriesByService = new Dictionary<IService, ServiceEntry>(ReferenceComparer<IService>.Instance);
        private readonly List<IService> _registrationOrder = new List<IService>();

        private readonly List<IServiceTickable> _tickables = new List<IServiceTickable>();
        private readonly List<IServiceLateTickable> _lateTickables = new List<IServiceLateTickable>();
        private readonly List<IServiceFixedTickable> _fixedTickables = new List<IServiceFixedTickable>();
        private readonly List<IServiceGizmoDrawable> _gizmoDrawables = new List<IServiceGizmoDrawable>();

        private readonly List<PendingChange> _pendingChanges = new List<PendingChange>();

        private bool _tickablesDirty;
        private bool _lateTickablesDirty;
        private bool _fixedTickablesDirty;
        private bool _gizmoDrawablesDirty;
        private bool _isIterating;

        internal ServiceScope(ServiceWorld world, ServiceScopeKind kind, string name, int order, int creationIndex)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Kind = kind;
            Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Scope name cannot be empty.", nameof(name)) : name;
            Order = order;
            CreationIndex = creationIndex;
        }

        internal ServiceWorld World { get; }

        internal ServiceScopeKind Kind { get; }

        public string Name { get; }

        internal int Order { get; }

        internal int CreationIndex { get; }

        internal bool IsDisposed { get; private set; }

        public T RegisterSelf<T>(T service) where T : class, IService
            => RegisterInternal(service, ServiceContractUtility.Create(service != null ? service.GetType() : typeof(T)));

        public TContract Register<TContract>(IService service)
            where TContract : class, IService
            => RegisterInternal(service, ServiceContractUtility.Create(service != null ? service.GetType() : typeof(TContract), typeof(TContract))) as TContract;

        public TContract Register<TContract, TExtraContract>(IService service)
            where TContract : class, IService
            where TExtraContract : class, IService
            => RegisterInternal(service, ServiceContractUtility.CreateExplicit(
                typeof(TContract),
                typeof(TExtraContract))) as TContract;

        public bool Unregister<T>() where T : class, IService
        {
            if (!_servicesByContract.TryGetValue(typeof(T).TypeHandle, out var service))
                return false;
            return Unregister(service);
        }

        public bool Unregister(IService service)
        {
            if (service == null || !_entriesByService.TryGetValue(service, out var entry))
                return false;

            if (service is not IServiceLifecycle lifecycle)
                throw new InvalidOperationException(ZString.Format("Service {0} must implement {1}.", service.GetType().FullName, nameof(IServiceLifecycle)));

            if (_isIterating)
            {
                if (entry.PendingRemove) return true;
                entry.PendingRemove = true;
                _entriesByService[service] = entry;
                _pendingChanges.Add(PendingChange.Remove(service));
                return true;
            }

            RemoveEntry(service, entry, true);
            lifecycle.Destroy();
            return true;
        }

        public bool TryGet<T>(out T service) where T : class, IService
        {
            if (_servicesByContract.TryGetValue(typeof(T).TypeHandle, out var raw))
            {
                service = raw as T;
                return service != null;
            }

            service = null;
            return false;
        }

        internal bool TryGet(Type contract, out IService service)
            => _servicesByContract.TryGetValue(contract.TypeHandle, out service);

        public T Require<T>() where T : class, IService
        {
            if (TryGet(out T service)) return service;
            throw new InvalidOperationException(ZString.Format("Scope {0} does not contain service {1}.", Name, typeof(T).FullName));
        }

        public bool HasContract(Type contractType)
            => _servicesByContract.ContainsKey(contractType.TypeHandle);

        internal void Tick(float deltaTime)
        {
            SortTickablesIfDirty();
            _isIterating = true;
            for (var i = 0; i < _tickables.Count; i++) _tickables[i].Tick(deltaTime);
            _isIterating = false;
            FlushPendingChanges();
        }

        internal void LateTick(float deltaTime)
        {
            SortLateTickablesIfDirty();
            _isIterating = true;
            for (var i = 0; i < _lateTickables.Count; i++) _lateTickables[i].LateTick(deltaTime);
            _isIterating = false;
            FlushPendingChanges();
        }

        internal void FixedTick(float fixedDeltaTime)
        {
            SortFixedTickablesIfDirty();
            _isIterating = true;
            for (var i = 0; i < _fixedTickables.Count; i++) _fixedTickables[i].FixedTick(fixedDeltaTime);
            _isIterating = false;
            FlushPendingChanges();
        }

        internal void DrawGizmos()
        {
            SortGizmoDrawablesIfDirty();
            _isIterating = true;
            for (var i = 0; i < _gizmoDrawables.Count; i++) _gizmoDrawables[i].DrawGizmos();
            _isIterating = false;
            FlushPendingChanges();
        }

        public void Dispose()
        {
            if (IsDisposed) return;

            _isIterating = false;
            _pendingChanges.Clear();

            for (var i = _registrationOrder.Count - 1; i >= 0; i--)
            {
                var service = _registrationOrder[i];
                if (service == null || !_entriesByService.TryGetValue(service, out var entry)) continue;
                if (service is not IServiceLifecycle lifecycle)
                    throw new InvalidOperationException(ZString.Format("Service {0} must implement {1}.", service.GetType().FullName, nameof(IServiceLifecycle)));

                RemoveEntry(service, entry, false);
                lifecycle.Destroy();
            }

            _registrationOrder.Clear();
            _tickables.Clear();
            _lateTickables.Clear();
            _fixedTickables.Clear();
            _gizmoDrawables.Clear();
            IsDisposed = true;
        }

        private T RegisterInternal<T>(T service, ServiceContracts contracts) where T : class, IService
        {
            EnsureNotDisposed();

            if (service == null)
                throw new ArgumentNullException(nameof(service));

            if (service is not IServiceLifecycle lifecycle)
                throw new InvalidOperationException(ZString.Format("Service {0} must implement {1}.", service.GetType().FullName, nameof(IServiceLifecycle)));

            ValidateService(service);

            if (_entriesByService.ContainsKey(service))
                throw new InvalidOperationException(ZString.Format("Service {0} is already registered in scope {1}.", service.GetType().FullName, Name));

            if (_isIterating)
            {
                ValidateContracts(contracts);
                _pendingChanges.Add(PendingChange.Add(service, contracts));
                return service;
            }

            ValidateContracts(contracts);
            lifecycle.Initialize(new ServiceContext(World, this));
            AddEntry(service, contracts);
            return service;
        }

        private void ValidateContracts(ServiceContracts contracts)
        {
            for (var i = 0; i < contracts.Count; i++)
            {
                var contract = contracts[i];
                if (_servicesByContract.TryGetValue(contract.TypeHandle, out var existing))
                {
                    throw new InvalidOperationException(
                        ZString.Format("Scope {0} already contains contract {1} bound to {2}.", Name, contract.FullName, existing.GetType().FullName));
                }
            }

            for (var i = 0; i < _pendingChanges.Count; i++)
            {
                var change = _pendingChanges[i];
                if (!change.IsAdd) continue;
                for (var contractIndex = 0; contractIndex < contracts.Count; contractIndex++)
                {
                    var contract = contracts[contractIndex];
                    for (var pendingContractIndex = 0; pendingContractIndex < change.Contracts.Count; pendingContractIndex++)
                    {
                        if (!change.Contracts[pendingContractIndex].TypeHandle.Equals(contract.TypeHandle)) continue;
                        throw new InvalidOperationException(
                            ZString.Format("Scope {0} already contains pending contract {1}.", Name, contract.FullName));
                    }
                }
            }
        }

        private void AddEntry(IService service, ServiceContracts contracts)
        {
            var entry = new ServiceEntry(contracts, _registrationOrder.Count);
            _registrationOrder.Add(service);

            for (var i = 0; i < contracts.Count; i++)
            {
                var contract = contracts[i];
                _servicesByContract.Add(contract.TypeHandle, service);
                World.AddContract(this, contract, service);
            }

            AddToLifecycleLists(service, ref entry);
            _entriesByService.Add(service, entry);
        }

        private void RemoveEntry(IService service, ServiceEntry entry, bool removeRegistrationOrder)
        {
            _entriesByService.Remove(service);
            RemoveFromLifecycleLists(service, entry);

            for (var i = 0; i < entry.Contracts.Count; i++)
            {
                var contract = entry.Contracts[i];
                _servicesByContract.Remove(contract.TypeHandle);
                World.RemoveContract(this, contract, service);
            }

            if (!removeRegistrationOrder) return;

            var lastIndex = _registrationOrder.Count - 1;
            var removedIndex = entry.RegistrationIndex;
            var lastService = _registrationOrder[lastIndex];
            _registrationOrder[removedIndex] = lastService;
            _registrationOrder.RemoveAt(lastIndex);

            if (removedIndex != lastIndex && lastService != null && _entriesByService.TryGetValue(lastService, out var lastEntry))
            {
                lastEntry.RegistrationIndex = removedIndex;
                _entriesByService[lastService] = lastEntry;
            }
        }

        private void EnsureNotDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(Name);
        }

        private static void ValidateService(IService service)
        {
            if (service is IMonoService &&
                (service is IServiceTickable ||
                 service is IServiceLateTickable ||
                 service is IServiceFixedTickable ||
                 service is IServiceGizmoDrawable))
            {
                throw new InvalidOperationException(
                    ZString.Format("Mono service {0} cannot implement tick lifecycle interfaces.", service.GetType().FullName));
            }
        }

        private void AddToLifecycleLists(IService service, ref ServiceEntry entry)
        {
            if (service is IServiceTickable tickable)
            {
                entry.TickIndex = _tickables.Count;
                _tickables.Add(tickable);
                _tickablesDirty = true;
            }

            if (service is IServiceLateTickable late)
            {
                entry.LateTickIndex = _lateTickables.Count;
                _lateTickables.Add(late);
                _lateTickablesDirty = true;
            }

            if (service is IServiceFixedTickable fixed_)
            {
                entry.FixedTickIndex = _fixedTickables.Count;
                _fixedTickables.Add(fixed_);
                _fixedTickablesDirty = true;
            }

            if (service is IServiceGizmoDrawable gizmo)
            {
                entry.GizmoIndex = _gizmoDrawables.Count;
                _gizmoDrawables.Add(gizmo);
                _gizmoDrawablesDirty = true;
            }
        }

        private void RemoveFromLifecycleLists(IService service, ServiceEntry entry)
        {
            if (entry.TickIndex != MissingIndex) RemoveTickableAt(entry.TickIndex);
            if (entry.LateTickIndex != MissingIndex) RemoveLateTickableAt(entry.LateTickIndex);
            if (entry.FixedTickIndex != MissingIndex) RemoveFixedTickableAt(entry.FixedTickIndex);
            if (entry.GizmoIndex != MissingIndex) RemoveGizmoDrawableAt(entry.GizmoIndex);
        }

        private void RemoveTickableAt(int index)
        {
            var lastIndex = _tickables.Count - 1;
            var moved = _tickables[lastIndex];
            _tickables[index] = moved;
            _tickables.RemoveAt(lastIndex);
            if (index != lastIndex) UpdateTickIndex(moved, index);
            _tickablesDirty = true;
        }

        private void RemoveLateTickableAt(int index)
        {
            var lastIndex = _lateTickables.Count - 1;
            var moved = _lateTickables[lastIndex];
            _lateTickables[index] = moved;
            _lateTickables.RemoveAt(lastIndex);
            if (index != lastIndex) UpdateLateTickIndex(moved, index);
            _lateTickablesDirty = true;
        }

        private void RemoveFixedTickableAt(int index)
        {
            var lastIndex = _fixedTickables.Count - 1;
            var moved = _fixedTickables[lastIndex];
            _fixedTickables[index] = moved;
            _fixedTickables.RemoveAt(lastIndex);
            if (index != lastIndex) UpdateFixedTickIndex(moved, index);
            _fixedTickablesDirty = true;
        }

        private void RemoveGizmoDrawableAt(int index)
        {
            var lastIndex = _gizmoDrawables.Count - 1;
            var moved = _gizmoDrawables[lastIndex];
            _gizmoDrawables[index] = moved;
            _gizmoDrawables.RemoveAt(lastIndex);
            if (index != lastIndex) UpdateGizmoIndex(moved, index);
            _gizmoDrawablesDirty = true;
        }

        private void UpdateTickIndex(IServiceTickable service, int index)
        {
            if (!_entriesByService.TryGetValue((IService)service, out var entry)) return;
            entry.TickIndex = index;
            _entriesByService[(IService)service] = entry;
        }

        private void UpdateLateTickIndex(IServiceLateTickable service, int index)
        {
            if (!_entriesByService.TryGetValue((IService)service, out var entry)) return;
            entry.LateTickIndex = index;
            _entriesByService[(IService)service] = entry;
        }

        private void UpdateFixedTickIndex(IServiceFixedTickable service, int index)
        {
            if (!_entriesByService.TryGetValue((IService)service, out var entry)) return;
            entry.FixedTickIndex = index;
            _entriesByService[(IService)service] = entry;
        }

        private void UpdateGizmoIndex(IServiceGizmoDrawable service, int index)
        {
            if (!_entriesByService.TryGetValue((IService)service, out var entry)) return;
            entry.GizmoIndex = index;
            _entriesByService[(IService)service] = entry;
        }

        private void FlushPendingChanges()
        {
            if (_pendingChanges.Count == 0) return;

            for (var i = 0; i < _pendingChanges.Count; i++)
            {
                var change = _pendingChanges[i];
                if (change.IsAdd)
                {
                    if (!_entriesByService.ContainsKey(change.Service))
                    {
                        ((IServiceLifecycle)change.Service).Initialize(new ServiceContext(World, this));
                        AddEntry(change.Service, change.Contracts);
                    }
                    continue;
                }

                if (_entriesByService.TryGetValue(change.Service, out var entry))
                {
                    RemoveEntry(change.Service, entry, true);
                    ((IServiceLifecycle)change.Service).Destroy();
                }
            }

            _pendingChanges.Clear();
        }

        private void SortTickablesIfDirty()
        {
            if (_tickablesDirty)
            {
                _tickables.Sort(CompareByOrder);
                RebuildTickIndices();
                _tickablesDirty = false;
            }
        }

        private void SortLateTickablesIfDirty()
        {
            if (_lateTickablesDirty)
            {
                _lateTickables.Sort(CompareByOrder);
                RebuildLateTickIndices();
                _lateTickablesDirty = false;
            }
        }

        private void SortFixedTickablesIfDirty()
        {
            if (_fixedTickablesDirty)
            {
                _fixedTickables.Sort(CompareByOrder);
                RebuildFixedTickIndices();
                _fixedTickablesDirty = false;
            }
        }

        private void SortGizmoDrawablesIfDirty()
        {
            if (_gizmoDrawablesDirty)
            {
                _gizmoDrawables.Sort(CompareByOrder);
                RebuildGizmoIndices();
                _gizmoDrawablesDirty = false;
            }
        }

        private void RebuildTickIndices()
        {
            for (var i = 0; i < _tickables.Count; i++) UpdateTickIndex(_tickables[i], i);
        }

        private void RebuildLateTickIndices()
        {
            for (var i = 0; i < _lateTickables.Count; i++) UpdateLateTickIndex(_lateTickables[i], i);
        }

        private void RebuildFixedTickIndices()
        {
            for (var i = 0; i < _fixedTickables.Count; i++) UpdateFixedTickIndex(_fixedTickables[i], i);
        }

        private void RebuildGizmoIndices()
        {
            for (var i = 0; i < _gizmoDrawables.Count; i++) UpdateGizmoIndex(_gizmoDrawables[i], i);
        }

        private static int CompareByOrder<T>(T a, T b)
        {
            var left = a is IServiceOrder oa ? oa.Order : 0;
            var right = b is IServiceOrder ob ? ob.Order : 0;
            return left.CompareTo(right);
        }

        private struct ServiceEntry
        {
            public readonly ServiceContracts Contracts;
            public int RegistrationIndex;
            public int TickIndex;
            public int LateTickIndex;
            public int FixedTickIndex;
            public int GizmoIndex;
            public bool PendingRemove;

            public ServiceEntry(ServiceContracts contracts, int registrationIndex)
            {
                Contracts = contracts;
                RegistrationIndex = registrationIndex;
                TickIndex = MissingIndex;
                LateTickIndex = MissingIndex;
                FixedTickIndex = MissingIndex;
                GizmoIndex = MissingIndex;
                PendingRemove = false;
            }
        }

        private readonly struct PendingChange
        {
            public readonly bool IsAdd;
            public readonly IService Service;
            public readonly ServiceContracts Contracts;

            private PendingChange(bool isAdd, IService service, ServiceContracts contracts)
            {
                IsAdd = isAdd;
                Service = service;
                Contracts = contracts;
            }

            public static PendingChange Add(IService service, ServiceContracts contracts)
                => new PendingChange(true, service, contracts);

            public static PendingChange Remove(IService service)
                => new PendingChange(false, service, default);
        }
    }
}
