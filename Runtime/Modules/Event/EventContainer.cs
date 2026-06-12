using System;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;

namespace AlicizaX
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.DivideByZeroChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public static class EventContainer<TPayload> where TPayload : struct, IPayloadEventArgs
    {
        private enum PendingOperationKind : byte
        {
            Subscribe,
            Unsubscribe,
            Clear,
            EnsureCapacity
        }

        private struct PendingOperation
        {
            internal PendingOperationKind Kind;
            internal int HandlerIndex;
            internal int Version;
            internal int Capacity;

            internal PendingOperation(PendingOperationKind kind, int handlerIndex, int version, int capacity)
            {
                Kind = kind;
                HandlerIndex = handlerIndex;
                Version = version;
                Capacity = capacity;
            }
        }

        private static readonly int InitialSize = EventInitialSize<TPayload>.Size;
        private static readonly int TypeId;

        private static InEventHandler<TPayload>[] _callbacks;
        private static int[] _versions;
        private static int[] _freeSlots;
        private static int[] _packedIndices;

        private static InEventHandler<TPayload>[] _packedCallbacks;
        private static int[] _packedSlots;
        private static int _packedCount;

        private static PendingOperation[] _pendingOperations;
        private static int _pendingCount;

        private static int _freeCount;
        private static int _version;
        private static int _publishDepth;
        private static int _safePublishDepth;

#if UNITY_EDITOR
        private static System.Collections.Generic.Dictionary<InEventHandler<TPayload>, int> _activeHandlers;
#endif

        static EventContainer()
        {
#if UNITY_EDITOR
            EventArgsGuard.ThrowIfDualEventKind<TPayload>();
#endif
            TypeId = UnsubscribeRegistry.Register(Unsubscribe);

            _callbacks = new InEventHandler<TPayload>[InitialSize];
            _versions = new int[InitialSize];
            _packedIndices = new int[InitialSize];
            _freeSlots = new int[InitialSize];
            _freeCount = InitialSize;

            _packedCallbacks = new InEventHandler<TPayload>[InitialSize];
            _packedSlots = new int[InitialSize];

            for (int i = 0; i < InitialSize; i++)
            {
                _freeSlots[i] = i;
            }

#if UNITY_EDITOR
            _activeHandlers = new System.Collections.Generic.Dictionary<InEventHandler<TPayload>, int>();
            EventDebugRegistry.RegisterPayloadContainer<TPayload>(
                GetDebugSubscriberCount,
                GetDebugCapacity,
                GetDebugInSubscriberCount,
                GetDebugSubscribers);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventRuntimeHandle Subscribe(InEventHandler<TPayload> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            if (_safePublishDepth > 0)
            {
                return SubscribeDeferred(callback);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ThrowIfMutatingDuringPublish("subscribe");
#endif

            return SubscribeImmediate(callback);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static EventRuntimeHandle SubscribeImmediate(InEventHandler<TPayload> callback)
        {
#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                AddActiveHandler(callback);
            }
#endif

            int handlerIndex = GetFreeSlot();
            int version = ++_version;

            _callbacks[handlerIndex] = callback;
            _versions[handlerIndex] = version;

            AddPacked(handlerIndex, callback);

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordSubscribe<TPayload>(_packedCount, _callbacks.Length);
            }
#endif

            return new EventRuntimeHandle(TypeId, handlerIndex, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static EventRuntimeHandle SubscribeDeferred(InEventHandler<TPayload> callback)
        {
            int handlerIndex = GetFreeSlot();
            int version = ++_version;

            _callbacks[handlerIndex] = callback;
            _versions[handlerIndex] = version;
            _packedIndices[handlerIndex] = -1;

            EnqueuePending(PendingOperationKind.Subscribe, handlerIndex, version, 0);
            return new EventRuntimeHandle(TypeId, handlerIndex, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddPacked(int handlerIndex, InEventHandler<TPayload> callback)
        {
            int packedIdx = _packedCount++;
            EnsurePackedCapacity();
            _packedCallbacks[packedIdx] = callback;
            _packedSlots[packedIdx] = handlerIndex;
            _packedIndices[handlerIndex] = packedIdx;
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
                Log.Warning($"EventContainer<{typeof(TPayload).Name}> Packed resized to {newSize}. 建议调整 Prewarm 为 {newSize} size.");
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
                Log.Warning($"EventContainer<{typeof(TPayload).Name}> Slot resized to {newSize}. 建议调整 Prewarm 为 {newSize} size.");
                EventDebugRegistry.RecordResize<TPayload>(_packedCount, _callbacks.Length);
            }
#endif

            return _freeSlots[--_freeCount];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Unsubscribe(int handlerIndex, int version)
        {
            if ((uint)handlerIndex >= (uint)_versions.Length) return;
            if (_versions[handlerIndex] != version) return;

            if (_safePublishDepth > 0)
            {
                EnqueuePending(PendingOperationKind.Unsubscribe, handlerIndex, version, 0);
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ThrowIfMutatingDuringPublish("unsubscribe");
#endif

            UnsubscribeImmediate(handlerIndex, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UnsubscribeImmediate(int handlerIndex, int version)
        {
            if ((uint)handlerIndex >= (uint)_versions.Length) return;
            if (_versions[handlerIndex] != version) return;

            int packedIdx = _packedIndices[handlerIndex];
            if (packedIdx < 0)
            {
                ReleaseUnpackedSlot(handlerIndex);
                return;
            }

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                RemoveActiveHandler(handlerIndex);
            }
#endif

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

            ReleaseUnpackedSlot(handlerIndex);

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordUnsubscribe<TPayload>(_packedCount, _callbacks.Length);
            }
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReleaseUnpackedSlot(int handlerIndex)
        {
            _callbacks[handlerIndex] = null;
            _versions[handlerIndex] = 0;
            _packedIndices[handlerIndex] = 0;
            _freeSlots[_freeCount++] = handlerIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Publish(in TPayload payload)
        {
#if UNITY_EDITOR
            if (EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                PublishCore(in payload);
                return;
            }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _publishDepth++;
            try
            {
#if UNITY_EDITOR
                EventDebugRegistry.RecordPublish<TPayload>(_packedCount, _callbacks.Length);
#endif
                PublishCore(in payload);
            }
            finally
            {
                _publishDepth--;
            }
#else
            PublishCore(in payload);
#endif
        }

        public static void SafePublish(in TPayload payload)
        {
            _safePublishDepth++;
            try
            {
#if UNITY_EDITOR
                if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
                {
                    EventDebugRegistry.RecordSafePublish<TPayload>(_packedCount, _callbacks.Length);
                }
#endif
                SafePublishCore(in payload);
            }
            finally
            {
                _safePublishDepth--;
                if (_safePublishDepth == 0)
                {
                    FlushPendingOperations();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PublishCore(in TPayload payload)
        {
            int count = _packedCount;
            if (count == 0) return;

            InEventHandler<TPayload>[] arr = _packedCallbacks;
            int i = 0;
            int unrolled = count & ~3;

            for (; i < unrolled; i += 4)
            {
                arr[i](in payload);
                arr[i + 1](in payload);
                arr[i + 2](in payload);
                arr[i + 3](in payload);
            }

            switch (count - i)
            {
                case 3:
                    arr[i](in payload);
                    arr[i + 1](in payload);
                    arr[i + 2](in payload);
                    break;
                case 2:
                    arr[i](in payload);
                    arr[i + 1](in payload);
                    break;
                case 1:
                    arr[i](in payload);
                    break;
            }
        }

        private static void SafePublishCore(in TPayload payload)
        {
            int count = _packedCount;
            if (count == 0) return;

            InEventHandler<TPayload>[] arr = _packedCallbacks;
            for (int i = 0; i < count; i++)
            {
                InvokeSafely(arr[i], in payload);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InvokeSafely(InEventHandler<TPayload> callback, in TPayload payload)
        {
            try
            {
                callback(in payload);
            }
            catch (Exception exception)
            {
#if UNITY_EDITOR
                if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
                {
                    EventDebugRegistry.RecordHandlerException<TPayload>(_packedCount, _callbacks.Length);
                }
#endif
                Log.Exception(exception);
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

            if (_safePublishDepth > 0)
            {
                EnqueuePending(PendingOperationKind.EnsureCapacity, 0, 0, capacity);
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ThrowIfMutatingDuringPublish("ensure capacity");
#endif

            EnsureCapacityImmediate(capacity);
        }

        private static void EnsureCapacityImmediate(int capacity)
        {
            if (_callbacks.Length >= capacity && _packedCallbacks.Length >= capacity)
            {
                return;
            }

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

            }

            if (_packedCallbacks.Length < capacity)
            {
                Array.Resize(ref _packedCallbacks, capacity);
                Array.Resize(ref _packedSlots, capacity);
            }
        }

        public static void Clear()
        {
            if (_safePublishDepth > 0)
            {
                EnqueuePending(PendingOperationKind.Clear, 0, 0, 0);
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ThrowIfMutatingDuringPublish("clear");
#endif

            ClearImmediate();
        }

        private static void ClearImmediate()
        {
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

        private static void EnqueuePending(PendingOperationKind kind, int handlerIndex, int version, int capacity)
        {
            if (_pendingOperations == null)
            {
                int initialSize = InitialSize > 0 ? InitialSize : 4;
                _pendingOperations = new PendingOperation[initialSize];
            }
            else if (_pendingCount >= _pendingOperations.Length)
            {
                Array.Resize(ref _pendingOperations, _pendingOperations.Length << 1);
            }

            _pendingOperations[_pendingCount++] = new PendingOperation(kind, handlerIndex, version, capacity);

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordDeferredMutation<TPayload>(_pendingCount, _packedCount, _callbacks.Length);
            }
#endif
        }

        private static void FlushPendingOperations()
        {
            int count = _pendingCount;
            if (count == 0) return;

            PendingOperation[] operations = _pendingOperations;
            _pendingCount = 0;

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordFlush<TPayload>(count, _packedCount, _callbacks.Length);
            }
#endif

            for (int i = 0; i < count; i++)
            {
                PendingOperation operation = operations[i];
                operations[i] = default;

                switch (operation.Kind)
                {
                    case PendingOperationKind.Subscribe:
                        ApplyPendingSubscribe(operation.HandlerIndex, operation.Version);
                        break;
                    case PendingOperationKind.Unsubscribe:
                        UnsubscribeImmediate(operation.HandlerIndex, operation.Version);
                        break;
                    case PendingOperationKind.Clear:
                        ClearImmediate();
                        break;
                    case PendingOperationKind.EnsureCapacity:
                        EnsureCapacityImmediate(operation.Capacity);
                        break;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ApplyPendingSubscribe(int handlerIndex, int version)
        {
            if ((uint)handlerIndex >= (uint)_versions.Length) return;
            if (_versions[handlerIndex] != version) return;
            if (_packedIndices[handlerIndex] >= 0) return;

            InEventHandler<TPayload> callback = _callbacks[handlerIndex];
            if (callback == null) return;

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                AddActiveHandler(callback);
            }
#endif

            AddPacked(handlerIndex, callback);

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordSubscribe<TPayload>(_packedCount, _callbacks.Length);
            }
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
                $"EventContainer<{typeof(TPayload).Name}> cannot {operation} while publishing. " +
                "Use SafePublisher if mutations must be deferred until dispatch completes.");
        }
#endif

#if UNITY_EDITOR
        private static void AddActiveHandler(InEventHandler<TPayload> callback)
        {
            if (_activeHandlers.TryGetValue(callback, out int count))
            {
                Log.Warning($"Duplicate event handler subscription: {callback.Method.Name}");
                _activeHandlers[callback] = count + 1;
                return;
            }

            _activeHandlers.Add(callback, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RemoveActiveHandler(int handlerIndex)
        {
            InEventHandler<TPayload> callback = _callbacks[handlerIndex];
            if (callback == null) return;

            if (!_activeHandlers.TryGetValue(callback, out int count)) return;
            if (count <= 1)
            {
                _activeHandlers.Remove(callback);
                return;
            }

            _activeHandlers[callback] = count - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetDebugSubscriberCount() => _packedCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetDebugCapacity() => _callbacks.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetDebugInSubscriberCount() => _packedCount;

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
                Delegate callback = _packedCallbacks[i];
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
                    false,
                    isCompilerGeneratedTarget,
                    isCompilerGeneratedMethod);
            }

            return subscribers;
        }
#endif
    }
}
