using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlicizaX
{
    public static class ProcedureBuilder
    {
        private const string ProcedureRunnerName = "[ProcedureRunner]";

        private static ProcedureRunner _runner;

        public static Type CurrentProcedureType => _runner != null ? _runner.CurrentProcedureType : null;

        public static bool IsRunning => _runner != null;

        public static void InitializeProcedure(IEnumerable<ProcedureBase> availableProcedures, Type defaultProcedureType)
        {
            DestroyProcedure();

            var gameObject = new GameObject(ProcedureRunnerName);
            UnityEngine.Object.DontDestroyOnLoad(gameObject);

            _runner = gameObject.AddComponent<ProcedureRunner>();
            _runner.InitializeProcedure(availableProcedures, defaultProcedureType);
        }

        public static void InitializeProcedure(ProcedureBase[] availableProcedures, Type defaultProcedureType)
        {
            DestroyProcedure();

            var gameObject = new GameObject(ProcedureRunnerName);
            UnityEngine.Object.DontDestroyOnLoad(gameObject);

            _runner = gameObject.AddComponent<ProcedureRunner>();
            _runner.InitializeProcedure(availableProcedures, defaultProcedureType);
        }

        public static void InitializeProcedure(List<ProcedureBase> availableProcedures, Type defaultProcedureType)
        {
            DestroyProcedure();

            var gameObject = new GameObject(ProcedureRunnerName);
            UnityEngine.Object.DontDestroyOnLoad(gameObject);

            _runner = gameObject.AddComponent<ProcedureRunner>();
            _runner.InitializeProcedure(availableProcedures, defaultProcedureType);
        }

        public static bool SwitchProcedure<T>() where T : ProcedureBase
        {
            return _runner != null && _runner.TrySwitchProcedure(typeof(T));
        }

        public static bool SwitchProcedure(Type procedureType)
        {
            return _runner != null && _runner.TrySwitchProcedure(procedureType);
        }

        public static bool ContainsProcedure<T>() where T : ProcedureBase
        {
            return _runner != null && _runner.ContainsProcedure(typeof(T));
        }

        public static bool ContainsProcedure(Type procedureType)
        {
            return _runner != null && _runner.ContainsProcedure(procedureType);
        }

        public static void DestroyProcedure()
        {
            if (_runner == null)
            {
                return;
            }

            var gameObject = _runner.gameObject;
            _runner.Shutdown();
            _runner = null;

            if (gameObject != null)
            {
                UnityEngine.Object.Destroy(gameObject);
            }
        }

        private sealed class ProcedureRunner : MonoBehaviour
        {
            private const int MaxLoadFactorNumerator = 7;
            private const int MaxLoadFactorDenominator = 10;

            private Type[] _types;
            private ProcedureBase[] _procedures;
            private int[] _orderedIndices;
            private ProcedureBase[] _orderedProcedures;
            private int _procedureCount;
            private int _bucketCount;

            private ProcedureBase _currentProcedure;
            private ProcedureBase _defaultProcedure;
            private bool _isDestroying;

            public Type CurrentProcedureType => _currentProcedure != null ? _currentProcedure.GetType() : null;

            public void InitializeProcedure(IEnumerable<ProcedureBase> availableProcedures, Type defaultProcedureType)
            {
                ClearAllProcedures();

                if (availableProcedures == null || defaultProcedureType == null)
                {
                    return;
                }

                foreach (var procedure in availableProcedures)
                {
                    AddProcedure(procedure);
                }

                if (TryGetProcedure(defaultProcedureType, out var defaultProcedure))
                {
                    _defaultProcedure = defaultProcedure;
                    TrySwitchProcedure(defaultProcedureType);
                }
            }

            public void InitializeProcedure(ProcedureBase[] availableProcedures, Type defaultProcedureType)
            {
                ClearAllProcedures();

                if (availableProcedures == null || defaultProcedureType == null)
                {
                    return;
                }

                EnsureCapacity(availableProcedures.Length);

                for (var i = 0; i < availableProcedures.Length; i++)
                {
                    AddProcedure(availableProcedures[i]);
                }

                if (TryGetProcedure(defaultProcedureType, out var defaultProcedure))
                {
                    _defaultProcedure = defaultProcedure;
                    TrySwitchProcedure(defaultProcedureType);
                }
            }

            public void InitializeProcedure(List<ProcedureBase> availableProcedures, Type defaultProcedureType)
            {
                ClearAllProcedures();

                if (availableProcedures == null || defaultProcedureType == null)
                {
                    return;
                }

                EnsureCapacity(availableProcedures.Count);

                for (var i = 0; i < availableProcedures.Count; i++)
                {
                    AddProcedure(availableProcedures[i]);
                }

                if (TryGetProcedure(defaultProcedureType, out var defaultProcedure))
                {
                    _defaultProcedure = defaultProcedure;
                    TrySwitchProcedure(defaultProcedureType);
                }
            }

            public void ClearAllProcedures()
            {
                if (_currentProcedure != null)
                {
                    _currentProcedure.OnLeave();
                    _currentProcedure = null;
                }

                for (var i = 0; i < _procedureCount; i++)
                {
                    var procedure = _orderedProcedures[i];
                    if (procedure == null)
                    {
                        continue;
                    }

                    procedure.OnDestroy();
                    _orderedProcedures[i] = null;
                }

                if (_types != null)
                {
                    for (var i = 0; i < _types.Length; i++)
                    {
                        _types[i] = null;
                        _procedures[i] = null;
                    }
                }

                _procedureCount = 0;
                _bucketCount = 0;
                _defaultProcedure = null;
            }

            public bool ContainsProcedure(Type procedureType)
            {
                return TryGetProcedure(procedureType, out _);
            }

            public bool TrySwitchProcedure(Type procedureType)
            {
                if (procedureType == null || _isDestroying)
                {
                    return false;
                }

                if (!TryGetProcedure(procedureType, out var nextProcedure))
                {
                    nextProcedure = _defaultProcedure;
                }

                if (nextProcedure == null)
                {
                    return false;
                }

                if (ReferenceEquals(_currentProcedure, nextProcedure))
                {
                    return true;
                }

                var previousProcedure = _currentProcedure;
                _currentProcedure = nextProcedure;
                previousProcedure?.OnLeave();
                nextProcedure.OnEnter();
                return true;
            }

            public void Shutdown()
            {
                _isDestroying = true;
                ClearAllProcedures();
            }

            private void AddProcedure(ProcedureBase procedure)
            {
                if (procedure == null)
                {
                    return;
                }

                var type = procedure.GetType();
                EnsureCapacity(_procedureCount + 1);

                var index = FindIndex(type);

                if (index >= 0)
                {
                    var previousProcedure = _procedures[index];
                    if (previousProcedure != null)
                    {
                        if (ReferenceEquals(_currentProcedure, previousProcedure))
                        {
                            _currentProcedure = null;
                        }

                        if (ReferenceEquals(_defaultProcedure, previousProcedure))
                        {
                            _defaultProcedure = procedure;
                        }

                        previousProcedure.OnDestroy();
                    }

                    _procedures[index] = procedure;
                    _orderedProcedures[_orderedIndices[index]] = procedure;
                    procedure.OnInit();
                    return;
                }

                var insertIndex = FindInsertIndex(type);
                _types[insertIndex] = type;
                _procedures[insertIndex] = procedure;
                _orderedIndices[insertIndex] = _procedureCount;
                _orderedProcedures[_procedureCount] = procedure;
                _procedureCount++;
                _bucketCount++;
                procedure.OnInit();
            }

            private bool TryGetProcedure(Type procedureType, out ProcedureBase procedure)
            {
                var index = FindIndex(procedureType);
                if (index >= 0)
                {
                    procedure = _procedures[index];
                    return true;
                }

                procedure = null;
                return false;
            }

            private int FindIndex(Type procedureType)
            {
                if (procedureType == null || _types == null)
                {
                    return -1;
                }

                var mask = _types.Length - 1;
                var index = procedureType.GetHashCode() & mask;

                for (var i = 0; i < _types.Length; i++)
                {
                    var type = _types[index];
                    if (type == null)
                    {
                        return -1;
                    }

                    if (type == procedureType)
                    {
                        return index;
                    }

                    index = (index + 1) & mask;
                }

                return -1;
            }

            private int FindInsertIndex(Type procedureType)
            {
                var mask = _types.Length - 1;
                var index = procedureType.GetHashCode() & mask;

                while (_types[index] != null)
                {
                    index = (index + 1) & mask;
                }

                return index;
            }

            private void EnsureCapacity(int targetCapacity)
            {
                if (_types != null && _orderedProcedures.Length >= targetCapacity && (_bucketCount + 1) * MaxLoadFactorDenominator < _types.Length * MaxLoadFactorNumerator)
                {
                    return;
                }

                var newProcedureCapacity = _orderedProcedures == null ? 4 : _orderedProcedures.Length;
                while (newProcedureCapacity < targetCapacity)
                {
                    newProcedureCapacity <<= 1;
                }

                var newBucketCapacity = _types == null ? 8 : _types.Length;
                while (targetCapacity * MaxLoadFactorDenominator >= newBucketCapacity * MaxLoadFactorNumerator)
                {
                    newBucketCapacity <<= 1;
                }

                var newTypes = new Type[newBucketCapacity];
                var newProcedures = new ProcedureBase[newBucketCapacity];
                var newOrderedIndices = new int[newBucketCapacity];
                var newOrderedProcedures = new ProcedureBase[newProcedureCapacity];

                for (var i = 0; i < _procedureCount; i++)
                {
                    var procedure = _orderedProcedures[i];
                    var type = procedure.GetType();
                    var insertIndex = FindInsertIndex(newTypes, type);
                    newTypes[insertIndex] = type;
                    newProcedures[insertIndex] = procedure;
                    newOrderedIndices[insertIndex] = i;
                    newOrderedProcedures[i] = procedure;
                }

                _types = newTypes;
                _procedures = newProcedures;
                _orderedIndices = newOrderedIndices;
                _orderedProcedures = newOrderedProcedures;
                _bucketCount = _procedureCount;
            }

            private static int FindInsertIndex(Type[] types, Type procedureType)
            {
                var mask = types.Length - 1;
                var index = procedureType.GetHashCode() & mask;

                while (types[index] != null)
                {
                    index = (index + 1) & mask;
                }

                return index;
            }

            private void Update()
            {
                _currentProcedure?.OnUpdate();
            }

            private void OnDestroy()
            {
                if (_runner == this)
                {
                    _runner = null;
                }

                Shutdown();
            }
        }
    }
}
