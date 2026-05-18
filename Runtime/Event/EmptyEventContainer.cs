using System;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;

namespace AlicizaX
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.DivideByZeroChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public static class EmptyEventContainer<TPayload> where TPayload : struct, IEventArgs
    {
        private static readonly int InitialSize = EventInitialSize<TPayload>.Size;
        private static readonly int TypeId;

        private static Action[] _callbacks;
        private static int[] _versions;
        private static int[] _freeSlots;
        private static int[] _packedIndices;

        private static Action[] _packedCallbacks;
        private static int[] _packedSlots;
        private static int _packedCount;

        private static int _freeCount;
        private static int _version;
        private static int _publishDepth;

#if UNITY_EDITOR
        private static System.Collections.Generic.HashSet<Action> _activeHandlers;
#endif

        static EmptyEventContainer()
        {
            TypeId = UnsubscribeRegistry.Register(Unsubscribe);

            _callbacks = new Action[InitialSize];
            _versions = new int[InitialSize];
            _packedIndices = new int[InitialSize];
            _freeSlots = new int[InitialSize];
            _freeCount = InitialSize;

            _packedCallbacks = new Action[InitialSize];
            _packedSlots = new int[InitialSize];

            for (int i = 0; i < InitialSize; i++)
            {
                _freeSlots[i] = i;
            }

#if UNITY_EDITOR
            _activeHandlers = new System.Collections.Generic.HashSet<Action>();
            EventDebugRegistry.RegisterEmptyContainer<TPayload>(
                GetDebugSubscriberCount,
                GetDebugCapacity,
                GetDebugSubscribers);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventRuntimeHandle Subscribe(Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            ThrowIfMutatingDuringPublish("subscribe");

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode && _activeHandlers.Contains(callback))
            {
                Log.Warning($"重复订阅事件处理程序: {callback.Method.Name}");
                return default;
            }

            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                _activeHandlers.Add(callback);
            }
#endif

            int handlerIndex = GetFreeSlot();

            int version = ++_version;
            _callbacks[handlerIndex] = callback;
            _versions[handlerIndex] = version;

            int packedIdx = _packedCount++;
            EnsurePackedCapacity();
            _packedCallbacks[packedIdx] = callback;
            _packedSlots[packedIdx] = handlerIndex;
            _packedIndices[handlerIndex] = packedIdx;

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordSubscribe<TPayload>(_packedCount, _callbacks.Length);
            }
#endif

            return new EventRuntimeHandle(TypeId, handlerIndex, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsurePackedCapacity()
        {
            if (_packedCount <= _packedCallbacks.Length) return;

            int newSize = _packedCallbacks.Length == 0 ? 64 : _packedCallbacks.Length << 1;
            Array.Resize(ref _packedCallbacks, newSize);
            Array.Resize(ref _packedSlots, newSize);

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                Log.Warning($"EmptyEventContainer<{typeof(TPayload).Name}> Packed 进行了扩容到 {newSize} 容量");
            }
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetFreeSlot()
        {
            if (_freeCount > 0)
            {
                return _freeSlots[--_freeCount];
            }

            int oldLen = _callbacks.Length;
            int newSize = oldLen == 0 ? 64 : oldLen << 1;

            Array.Resize(ref _callbacks, newSize);
            Array.Resize(ref _versions, newSize);
            Array.Resize(ref _packedIndices, newSize);
            Array.Resize(ref _freeSlots, newSize);

            for (int i = newSize - 1; i >= oldLen; i--)
            {
                _freeSlots[_freeCount++] = i;
            }

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                Log.Warning($"EmptyEventContainer<{typeof(TPayload).Name}> Slot 进行了扩容到 {newSize} 容量");
                EventDebugRegistry.RecordResize<TPayload>(_packedCount, _callbacks.Length);
            }
#endif

            return _freeSlots[--_freeCount];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Unsubscribe(int handlerIndex, int version)
        {
            ThrowIfMutatingDuringPublish("unsubscribe");

            if ((uint)handlerIndex >= (uint)_versions.Length) return;
            if (_versions[handlerIndex] != version) return;

            int packedIdx = _packedIndices[handlerIndex];
            int lastIdx = --_packedCount;

            if (packedIdx != lastIdx)
            {
                _packedCallbacks[packedIdx] = _packedCallbacks[lastIdx];
                int movedSlot = _packedSlots[lastIdx];
                _packedSlots[packedIdx] = movedSlot;
                _packedIndices[movedSlot] = packedIdx;
            }
            _packedCallbacks[lastIdx] = null;
            _packedSlots[lastIdx] = 0;

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                RemoveActiveHandler(handlerIndex);
            }
#endif

            _callbacks[handlerIndex] = null;
            _versions[handlerIndex] = 0;
            _packedIndices[handlerIndex] = 0;
            _freeSlots[_freeCount++] = handlerIndex;

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordUnsubscribe<TPayload>(_packedCount, _callbacks.Length);
            }
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Publish()
        {
#if UNITY_EDITOR
            if (EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                PublishCore();
                return;
            }

            _publishDepth++;
            try
            {
                EventDebugRegistry.RecordPublish<TPayload>(_packedCount, _callbacks.Length);
                PublishCore();
            }
            finally
            {
                _publishDepth--;
            }
#else
            _publishDepth++;
            PublishCore();
            _publishDepth--;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PublishCore()
        {
            int count = _packedCount;
            if (count == 0) return;

            Action[] callbacks = _packedCallbacks;

            int i = 0;
            int unrolled = count & ~3;

            for (; i < unrolled; i += 4)
            {
                callbacks[i]();
                callbacks[i + 1]();
                callbacks[i + 2]();
                callbacks[i + 3]();
            }

            switch (count - i)
            {
                case 3:
                    callbacks[i]();
                    callbacks[i + 1]();
                    callbacks[i + 2]();
                    break;
                case 2:
                    callbacks[i]();
                    callbacks[i + 1]();
                    break;
                case 1:
                    callbacks[i]();
                    break;
            }
        }

        public static int SubscriberCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _packedCount;
        }

        public static void EnsureCapacity(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            ThrowIfMutatingDuringPublish("ensure capacity");

            if (_callbacks.Length >= capacity && _packedCallbacks.Length >= capacity) return;

            if (_callbacks.Length < capacity)
            {
                int oldLen = _callbacks.Length;
                Array.Resize(ref _callbacks, capacity);
                Array.Resize(ref _versions, capacity);
                Array.Resize(ref _packedIndices, capacity);
                Array.Resize(ref _freeSlots, capacity);

                for (int i = capacity - 1; i >= oldLen; i--)
                {
                    _freeSlots[_freeCount++] = i;
                }

#if UNITY_EDITOR
                if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
                {
                    Log.Warning($"EmptyEventContainer<{typeof(TPayload).Name}> Slot 进行了扩容到 {capacity} 容量");
                }
#endif
            }

            if (_packedCallbacks.Length < capacity)
            {
                Array.Resize(ref _packedCallbacks, capacity);
                Array.Resize(ref _packedSlots, capacity);

#if UNITY_EDITOR
                if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
                {
                    Log.Warning($"EmptyEventContainer<{typeof(TPayload).Name}> Packed 进行了扩容到 {capacity} 容量");
                }
#endif
            }

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordResize<TPayload>(_packedCount, _callbacks.Length);
            }
#endif
        }

        public static void Clear()
        {
            ThrowIfMutatingDuringPublish("clear");

            for (int i = 0; i < _packedCount; i++)
            {
                int slotIdx = _packedSlots[i];

#if UNITY_EDITOR
                if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
                {
                    RemoveActiveHandler(slotIdx);
                }
#endif

                _callbacks[slotIdx] = null;
                _versions[slotIdx] = 0;
                _packedIndices[slotIdx] = 0;
                _freeSlots[_freeCount++] = slotIdx;
                _packedCallbacks[i] = null;
                _packedSlots[i] = 0;
            }

            _packedCount = 0;

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordClear<TPayload>(_packedCount, _callbacks.Length);
            }
            _activeHandlers.Clear();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ThrowIfMutatingDuringPublish(string operation)
        {
            if (_publishDepth <= 0) return;

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordMutationRejected<TPayload>(_packedCount, _callbacks.Length);
            }
#endif
            throw new InvalidOperationException(
                $"EmptyEventContainer<{typeof(TPayload).Name}> cannot {operation} while publishing. " +
                "This bus guarantees a stable subscriber set during dispatch. Apply the mutation after publish completes.");
        }

#if UNITY_EDITOR
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RemoveActiveHandler(int handlerIndex)
        {
            Action callback = _callbacks[handlerIndex];
            if (callback != null)
            {
                _activeHandlers.Remove(callback);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetDebugSubscriberCount() => _packedCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetDebugCapacity() => _callbacks.Length;

        private static EventDebugSubscriberInfo[] GetDebugSubscribers()
        {
            int count = _packedCount;
            if (count == 0)
            {
                return Array.Empty<EventDebugSubscriberInfo>();
            }

            EventDebugSubscriberInfo[] subscribers = new EventDebugSubscriberInfo[count];
            for (int i = 0; i < count; i++)
            {
                int handlerIndex = _packedSlots[i];
                Action callback = _packedCallbacks[i];
                object target = callback.Target;
                bool isStatic = target == null;
                bool isUnityObjectDestroyed = false;
                bool isCompilerGeneratedTarget = !isStatic && target.GetType().IsDefined(typeof(CompilerGeneratedAttribute), false);
                bool isCompilerGeneratedMethod = callback.Method.IsDefined(typeof(CompilerGeneratedAttribute), false);
                UnityEngine.Object unityTarget = null;

                if (!isStatic && target is UnityEngine.Object engineObject)
                {
                    unityTarget = engineObject;
                    isUnityObjectDestroyed = engineObject == null;
                }

                subscribers[i] = new EventDebugSubscriberInfo(
                    handlerIndex,
                    _versions[handlerIndex],
                    callback.Method.DeclaringType?.FullName ?? "<UnknownType>",
                    callback.Method.Name,
                    target?.GetType().FullName ?? "<Static>",
                    unityTarget,
                    isStatic,
                    isUnityObjectDestroyed,
                    true,
                    false,
                    isCompilerGeneratedTarget,
                    isCompilerGeneratedMethod);
            }

            return subscribers;
        }
#endif
    }
}
