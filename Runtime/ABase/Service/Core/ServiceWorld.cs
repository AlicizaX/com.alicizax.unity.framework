using System;
using System.Collections.Generic;
using Cysharp.Text;

namespace AlicizaX
{
    internal sealed class ServiceWorld : IDisposable
    {
        private const int ScopeSlotCount = 3;

        private readonly ServiceScope[] _scopesByKind = new ServiceScope[ScopeSlotCount];
        private readonly ServiceScope[] _activeScopes = new ServiceScope[ScopeSlotCount];
        private readonly Dictionary<RuntimeTypeHandle, ContractBindings> _servicesByContract = new Dictionary<RuntimeTypeHandle, ContractBindings>();

        private int _activeScopeCount;
        private int _nextScopeCreationIndex;
        private bool _scopesDirty;

        internal ServiceWorld(int appScopeOrder = ServiceDomainOrder.App)
        {
            App = CreateScopeInternal(ServiceScopeKind.App, nameof(App), appScopeOrder);
        }

        internal ServiceScope App { get; }

        internal bool HasScene => IsAlive(ServiceScopeKind.Scene);

        internal bool HasGameplay => IsAlive(ServiceScopeKind.Gameplay);

        internal ServiceScope Scene
        {
            get
            {
                if (!HasScene)
                    throw new InvalidOperationException("Scene scope has not been created yet.");
                return _scopesByKind[(int)ServiceScopeKind.Scene];
            }
        }

        internal ServiceScope Gameplay
        {
            get
            {
                if (!HasGameplay)
                    throw new InvalidOperationException("Gameplay scope has not been created yet.");
                return _scopesByKind[(int)ServiceScopeKind.Gameplay];
            }
        }

        internal ServiceScope EnsureScene(int order = ServiceDomainOrder.Scene)
            => IsAlive(ServiceScopeKind.Scene)
                ? _scopesByKind[(int)ServiceScopeKind.Scene]
                : CreateScopeInternal(ServiceScopeKind.Scene, nameof(SceneScope), order);

        internal bool TryGetScene(out ServiceScope scope)
        {
            scope = IsAlive(ServiceScopeKind.Scene) ? _scopesByKind[(int)ServiceScopeKind.Scene] : null;
            return scope != null;
        }

        internal ServiceScope EnsureGameplay(int order = ServiceDomainOrder.Gameplay)
            => IsAlive(ServiceScopeKind.Gameplay)
                ? _scopesByKind[(int)ServiceScopeKind.Gameplay]
                : CreateScopeInternal(ServiceScopeKind.Gameplay, nameof(GameplayScope), order);

        internal bool TryGetGameplay(out ServiceScope scope)
        {
            scope = IsAlive(ServiceScopeKind.Gameplay) ? _scopesByKind[(int)ServiceScopeKind.Gameplay] : null;
            return scope != null;
        }

        internal bool TryGet<T>(out T service) where T : class, IService
            => TryGet(null, out service);

        internal bool TryGet<T>(ServiceScope preferredScope, out T service) where T : class, IService
        {
            if (preferredScope != null && !preferredScope.IsDisposed && preferredScope.TryGet(out service))
                return true;

            if (_servicesByContract.TryGetValue(typeof(T).TypeHandle, out var bindings) && bindings.TryGetBest(out var raw))
            {
                service = raw as T;
                return service != null;
            }

            service = null;
            return false;
        }

        internal T Require<T>() where T : class, IService => Require<T>(null);

        internal T Require<T>(ServiceScope preferredScope) where T : class, IService
        {
            if (TryGet(preferredScope, out T service)) return service;
            throw new InvalidOperationException(ZString.Format("Service {0} was not found in any active scope.", typeof(T).FullName));
        }

        internal void Tick(float deltaTime)
        {
            SortScopesIfDirty();
            for (var i = 0; i < _activeScopeCount; i++) _activeScopes[i].Tick(deltaTime);
        }

        internal void LateTick(float deltaTime)
        {
            SortScopesIfDirty();
            for (var i = 0; i < _activeScopeCount; i++) _activeScopes[i].LateTick(deltaTime);
        }

        internal void FixedTick(float fixedDeltaTime)
        {
            SortScopesIfDirty();
            for (var i = 0; i < _activeScopeCount; i++) _activeScopes[i].FixedTick(fixedDeltaTime);
        }

        internal void DrawGizmos()
        {
            SortScopesIfDirty();
            for (var i = 0; i < _activeScopeCount; i++) _activeScopes[i].DrawGizmos();
        }

        public void Dispose()
        {
            for (var i = _activeScopeCount - 1; i >= 0; i--)
                _activeScopes[i].Dispose();

            for (var i = 0; i < ScopeSlotCount; i++)
                _scopesByKind[i] = null;

            _activeScopeCount = 0;
            _servicesByContract.Clear();
        }

        internal void AddContract(ServiceScope scope, Type contract, IService service)
        {
            var contractHandle = contract.TypeHandle;
            if (!_servicesByContract.TryGetValue(contractHandle, out var bindings))
            {
                bindings = default;
                _servicesByContract.Add(contractHandle, bindings);
            }

            bindings.Set(scope.Kind, scope, service);
            _servicesByContract[contractHandle] = bindings;
        }

        internal void RemoveContract(ServiceScope scope, Type contract, IService service)
        {
            var contractHandle = contract.TypeHandle;
            if (!_servicesByContract.TryGetValue(contractHandle, out var bindings)) return;

            bindings.Clear(scope.Kind, service);
            if (bindings.IsEmpty)
                _servicesByContract.Remove(contractHandle);
            else
                _servicesByContract[contractHandle] = bindings;
        }

        private bool IsAlive(ServiceScopeKind kind)
        {
            var scope = _scopesByKind[(int)kind];
            return scope != null && !scope.IsDisposed;
        }

        private ServiceScope CreateScopeInternal(ServiceScopeKind kind, string scopeName, int order)
        {
            if (IsAlive(kind))
                throw new InvalidOperationException(ZString.Format("Scope {0} already exists.", scopeName));

            var scope = new ServiceScope(this, kind, scopeName, order, _nextScopeCreationIndex++);
            _scopesByKind[(int)kind] = scope;
            _activeScopes[_activeScopeCount++] = scope;
            _scopesDirty = true;
            return scope;
        }

        private void SortScopesIfDirty()
        {
            if (!_scopesDirty) return;

            for (var i = 1; i < _activeScopeCount; i++)
            {
                var scope = _activeScopes[i];
                var index = i - 1;
                while (index >= 0 && HasHigherPriority(_activeScopes[index], scope))
                {
                    _activeScopes[index + 1] = _activeScopes[index];
                    index--;
                }

                _activeScopes[index + 1] = scope;
            }

            _scopesDirty = false;
        }

        private static bool HasHigherPriority(ServiceScope left, ServiceScope right)
            => left.Order > right.Order || left.Order == right.Order && left.CreationIndex > right.CreationIndex;

        private struct ContractBindings
        {
            private ServiceBinding _app;
            private ServiceBinding _scene;
            private ServiceBinding _gameplay;

            public bool IsEmpty => !_app.HasValue && !_scene.HasValue && !_gameplay.HasValue;

            public void Set(ServiceScopeKind kind, ServiceScope scope, IService service)
            {
                var binding = new ServiceBinding(scope, service);
                if (kind == ServiceScopeKind.App)
                    _app = binding;
                else if (kind == ServiceScopeKind.Scene)
                    _scene = binding;
                else
                    _gameplay = binding;
            }

            public void Clear(ServiceScopeKind kind, IService service)
            {
                if (kind == ServiceScopeKind.App)
                {
                    if (_app.HasValue && ReferenceEquals(_app.Service, service)) _app = default;
                    return;
                }

                if (kind == ServiceScopeKind.Scene)
                {
                    if (_scene.HasValue && ReferenceEquals(_scene.Service, service)) _scene = default;
                    return;
                }

                if (_gameplay.HasValue && ReferenceEquals(_gameplay.Service, service)) _gameplay = default;
            }

            public bool TryGetBest(out IService service)
            {
                if (_gameplay.HasValue)
                {
                    service = _gameplay.Service;
                    return true;
                }

                if (_scene.HasValue)
                {
                    service = _scene.Service;
                    return true;
                }

                if (_app.HasValue)
                {
                    service = _app.Service;
                    return true;
                }

                service = null;
                return false;
            }
        }

        private struct ServiceBinding
        {
            public ServiceScope Scope;
            public IService Service;
            public bool HasValue;

            public ServiceBinding(ServiceScope scope, IService service)
            {
                Scope = scope;
                Service = service;
                HasValue = true;
            }
        }
    }
}
