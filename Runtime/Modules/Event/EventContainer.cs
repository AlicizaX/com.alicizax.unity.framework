using System;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;

namespace AlicizaX
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.DivideByZeroChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public static class EventContainer<TPayload> where TPayload : struct, IEventArgs
    {
        private static readonly int InitialSize = EventInitialSize<TPayload>.Size;
        private static readonly int TypeId;

        private static Action<TPayload>[] _valueCallbacks;
        private static InEventHandler<TPayload>[] _inCallbacks;
        private static int[] _versions;
        private static int[] _freeSlots;
        private static int[] _packedIndices;

        private static Action<TPayload>[] _packedValueCallbacks;
        private static int[] _packedValueSlots;
        private static int _packedValueCount;
        private static InEventHandler<TPayload>[] _packedInCallbacks;
        private static int[] _packedInSlots;
        private static int _packedInCount;

        private static int _freeCount;
        private static int _version;
        private static int _publishDepth;

#if UNITY_EDITOR
        private static System.Collections.Generic.HashSet<Action<TPayload>> _activeValueHandlers;
        private static System.Collections.Generic.HashSet<InEventHandler<TPayload>> _activeInHandlers;
#endif

        static EventContainer()
        {
            TypeId = UnsubscribeRegistry.Register(Unsubscribe);

            _valueCallbacks = new Action<TPayload>[InitialSize];
            _inCallbacks = new InEventHandler<TPayload>[InitialSize];
            _versions = new int[InitialSize];
            _packedIndices = new int[InitialSize];
            _freeSlots = new int[InitialSize];
            _freeCount = InitialSize;

            _packedValueCallbacks = new Action<TPayload>[InitialSize];
            _packedValueSlots = new int[InitialSize];
            _packedInCallbacks = new InEventHandler<TPayload>[InitialSize];
            _packedInSlots = new int[InitialSize];

            for (int i = 0; i < InitialSize; i++)
            {
                _freeSlots[i] = i;
            }

#if UNITY_EDITOR
            _activeValueHandlers = new System.Collections.Generic.HashSet<Action<TPayload>>();
            _activeInHandlers = new System.Collections.Generic.HashSet<InEventHandler<TPayload>>();
            EventDebugRegistry.RegisterPayloadContainer<TPayload>(
                GetDebugSubscriberCount,
                GetDebugCapacity,
                GetDebugValueSubscriberCount,
                GetDebugInSubscriberCount,
                GetDebugSubscribers);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventRuntimeHandle Subscribe(Action<TPayload> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            ThrowIfMutatingDuringPublish("subscribe");

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode && _activeValueHandlers.Contains(callback))
            {
                Log.Warning($"重复订阅事件处理程序: {callback.Method.Name}");
                return default;
            }

            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                _activeValueHandlers.Add(callback);
            }
#endif

            return SubscribeCore(callback, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EventRuntimeHandle Subscribe(InEventHandler<TPayload> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            ThrowIfMutatingDuringPublish("subscribe");

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode && _activeInHandlers.Contains(callback))
            {
                Log.Warning($"重复订阅事件处理程序: {callback.Method.Name}");
                return default;
            }

            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                _activeInHandlers.Add(callback);
            }
#endif

            return SubscribeCore(null, callback);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static EventRuntimeHandle SubscribeCore(Action<TPayload> valueCallback, InEventHandler<TPayload> inCallback)
        {
            int handlerIndex = GetFreeSlot();

            int version = ++_version;
            _valueCallbacks[handlerIndex] = valueCallback;
            _inCallbacks[handlerIndex] = inCallback;
            _versions[handlerIndex] = version;

            if (inCallback != null)
            {
                int packedIdx = _packedInCount++;
                EnsurePackedInCapacity();
                _packedInCallbacks[packedIdx] = inCallback;
                _packedInSlots[packedIdx] = handlerIndex;
                _packedIndices[handlerIndex] = packedIdx;
            }
            else
            {
                int packedIdx = _packedValueCount++;
                EnsurePackedValueCapacity();
                _packedValueCallbacks[packedIdx] = valueCallback;
                _packedValueSlots[packedIdx] = handlerIndex;
                _packedIndices[handlerIndex] = packedIdx;
            }

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordSubscribe<TPayload>(SubscriberCount, _valueCallbacks.Length);
            }
#endif

            return new EventRuntimeHandle(TypeId, handlerIndex, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsurePackedValueCapacity()
        {
            if (_packedValueCount <= _packedValueCallbacks.Length) return;

            int newSize = _packedValueCallbacks.Length == 0 ? 64 : _packedValueCallbacks.Length << 1;
            Array.Resize(ref _packedValueCallbacks, newSize);
            Array.Resize(ref _packedValueSlots, newSize);

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                Log.Warning($"EventContainer<{typeof(TPayload).Name}> PackedValue 进行了扩容到 {newSize} 容量");
            }
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsurePackedInCapacity()
        {
            if (_packedInCount <= _packedInCallbacks.Length) return;

            int newSize = _packedInCallbacks.Length == 0 ? 64 : _packedInCallbacks.Length << 1;
            Array.Resize(ref _packedInCallbacks, newSize);
            Array.Resize(ref _packedInSlots, newSize);

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                Log.Warning($"EventContainer<{typeof(TPayload).Name}> PackedIn 进行了扩容到 {newSize} 容量");
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

            int oldLen = _valueCallbacks.Length;
            int newSize = oldLen == 0 ? 64 : oldLen << 1;

            Array.Resize(ref _valueCallbacks, newSize);
            Array.Resize(ref _inCallbacks, newSize);
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
                Log.Warning($"EventContainer<{typeof(TPayload).Name}> Slot 进行了扩容到 {newSize} 容量");
                EventDebugRegistry.RecordResize<TPayload>(SubscriberCount, _valueCallbacks.Length);
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

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                RemoveActiveHandler(handlerIndex);
            }
#endif

            if (_inCallbacks[handlerIndex] != null)
            {
                int lastIdx = --_packedInCount;
                if (packedIdx != lastIdx)
                {
                    _packedInCallbacks[packedIdx] = _packedInCallbacks[lastIdx];
                    int movedSlot = _packedInSlots[lastIdx];
                    _packedInSlots[packedIdx] = movedSlot;
                    _packedIndices[movedSlot] = packedIdx;
                }
                _packedInCallbacks[lastIdx] = null;
                _packedInSlots[lastIdx] = 0;
            }
            else
            {
                int lastIdx = --_packedValueCount;
                if (packedIdx != lastIdx)
                {
                    _packedValueCallbacks[packedIdx] = _packedValueCallbacks[lastIdx];
                    int movedSlot = _packedValueSlots[lastIdx];
                    _packedValueSlots[packedIdx] = movedSlot;
                    _packedIndices[movedSlot] = packedIdx;
                }
                _packedValueCallbacks[lastIdx] = null;
                _packedValueSlots[lastIdx] = 0;
            }

            _valueCallbacks[handlerIndex] = null;
            _inCallbacks[handlerIndex] = null;
            _versions[handlerIndex] = 0;
            _packedIndices[handlerIndex] = 0;
            _freeSlots[_freeCount++] = handlerIndex;

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordUnsubscribe<TPayload>(SubscriberCount, _valueCallbacks.Length);
            }
#endif
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

            _publishDepth++;
            try
            {
                EventDebugRegistry.RecordPublish<TPayload>(SubscriberCount, _valueCallbacks.Length);
                PublishCore(in payload);
            }
            finally
            {
                _publishDepth--;
            }
#else
            _publishDepth++;
            PublishCore(in payload);
            _publishDepth--;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PublishCore(in TPayload payload)
        {
            int vc = _packedValueCount;
            int ic = _packedInCount;
            if ((vc | ic) == 0) return;

            if (vc > 0)
            {
                Action<TPayload>[] arr = _packedValueCallbacks;
                int i = 0;
                int unrolled = vc & ~3;

                for (; i < unrolled; i += 4)
                {
                    arr[i](payload);
                    arr[i + 1](payload);
                    arr[i + 2](payload);
                    arr[i + 3](payload);
                }

                switch (vc - i)
                {
                    case 3:
                        arr[i](payload);
                        arr[i + 1](payload);
                        arr[i + 2](payload);
                        break;
                    case 2:
                        arr[i](payload);
                        arr[i + 1](payload);
                        break;
                    case 1:
                        arr[i](payload);
                        break;
                }
            }

            if (ic > 0)
            {
                InEventHandler<TPayload>[] arr = _packedInCallbacks;
                int i = 0;
                int unrolled = ic & ~3;

                for (; i < unrolled; i += 4)
                {
                    arr[i](in payload);
                    arr[i + 1](in payload);
                    arr[i + 2](in payload);
                    arr[i + 3](in payload);
                }

                switch (ic - i)
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
        }

        public static int SubscriberCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _packedValueCount + _packedInCount;
        }

        public static void EnsureCapacity(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            ThrowIfMutatingDuringPublish("ensure capacity");

            if (_valueCallbacks.Length >= capacity
                && _packedValueCallbacks.Length >= capacity
                && _packedInCallbacks.Length >= capacity)
            {
                return;
            }

            if (_valueCallbacks.Length < capacity)
            {
                int oldLen = _valueCallbacks.Length;
                Array.Resize(ref _valueCallbacks, capacity);
                Array.Resize(ref _inCallbacks, capacity);
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
                    Log.Warning($"EventContainer<{typeof(TPayload).Name}> Slot 进行了扩容到 {capacity} 容量");
                }
#endif
            }

            if (_packedValueCallbacks.Length < capacity)
            {
                Array.Resize(ref _packedValueCallbacks, capacity);
                Array.Resize(ref _packedValueSlots, capacity);

#if UNITY_EDITOR
                if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
                {
                    Log.Warning($"EventContainer<{typeof(TPayload).Name}> PackedValue 进行了扩容到 {capacity} 容量");
                }
#endif
            }

            if (_packedInCallbacks.Length < capacity)
            {
                Array.Resize(ref _packedInCallbacks, capacity);
                Array.Resize(ref _packedInSlots, capacity);

#if UNITY_EDITOR
                if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
                {
                    Log.Warning($"EventContainer<{typeof(TPayload).Name}> PackedIn 进行了扩容到 {capacity} 容量");
                }
#endif
            }

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordResize<TPayload>(SubscriberCount, _valueCallbacks.Length);
            }
#endif
        }

        public static void Clear()
        {
            ThrowIfMutatingDuringPublish("clear");

            for (int i = 0; i < _packedValueCount; i++)
            {
                int slotIdx = _packedValueSlots[i];

#if UNITY_EDITOR
                if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
                {
                    RemoveActiveHandler(slotIdx);
                }
#endif

                _valueCallbacks[slotIdx] = null;
                _inCallbacks[slotIdx] = null;
                _versions[slotIdx] = 0;
                _packedIndices[slotIdx] = 0;
                _freeSlots[_freeCount++] = slotIdx;
                _packedValueCallbacks[i] = null;
                _packedValueSlots[i] = 0;
            }

            for (int i = 0; i < _packedInCount; i++)
            {
                int slotIdx = _packedInSlots[i];

#if UNITY_EDITOR
                if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
                {
                    RemoveActiveHandler(slotIdx);
                }
#endif

                _valueCallbacks[slotIdx] = null;
                _inCallbacks[slotIdx] = null;
                _versions[slotIdx] = 0;
                _packedIndices[slotIdx] = 0;
                _freeSlots[_freeCount++] = slotIdx;
                _packedInCallbacks[i] = null;
                _packedInSlots[i] = 0;
            }

            _packedValueCount = 0;
            _packedInCount = 0;

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordClear<TPayload>(SubscriberCount, _valueCallbacks.Length);
            }
            _activeValueHandlers.Clear();
            _activeInHandlers.Clear();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ThrowIfMutatingDuringPublish(string operation)
        {
            if (_publishDepth <= 0) return;

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordMutationRejected<TPayload>(SubscriberCount, _valueCallbacks.Length);
            }
#endif
            throw new InvalidOperationException(
                $"EventContainer<{typeof(TPayload).Name}> cannot {operation} while publishing. " +
                "This bus guarantees a stable subscriber set during dispatch. Apply the mutation after publish completes.");
        }

#if UNITY_EDITOR
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RemoveActiveHandler(int handlerIndex)
        {
            Action<TPayload> valueCallback = _valueCallbacks[handlerIndex];
            if (valueCallback != null)
            {
                _activeValueHandlers.Remove(valueCallback);
                return;
            }

            InEventHandler<TPayload> inCallback = _inCallbacks[handlerIndex];
            if (inCallback != null)
            {
                _activeInHandlers.Remove(inCallback);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetDebugSubscriberCount() => _packedValueCount + _packedInCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetDebugCapacity() => _valueCallbacks.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetDebugValueSubscriberCount() => _packedValueCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetDebugInSubscriberCount() => _packedInCount;

        private static EventDebugSubscriberInfo[] GetDebugSubscribers()
        {
            int count = _packedValueCount + _packedInCount;
            if (count == 0)
            {
                return Array.Empty<EventDebugSubscriberInfo>();
            }

            EventDebugSubscriberInfo[] subscribers = new EventDebugSubscriberInfo[count];
            int idx = 0;

            for (int i = 0; i < _packedValueCount; i++)
            {
                int handlerIndex = _packedValueSlots[i];
                Action<TPayload> callback = _packedValueCallbacks[i];
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

                subscribers[idx++] = new EventDebugSubscriberInfo(
                    handlerIndex,
                    _versions[handlerIndex],
                    callback.Method.DeclaringType?.FullName ?? "<UnknownType>",
                    callback.Method.Name,
                    target?.GetType().FullName ?? "<Static>",
                    unityTarget,
                    isStatic,
                    isUnityObjectDestroyed,
                    false,
                    false,
                    isCompilerGeneratedTarget,
                    isCompilerGeneratedMethod);
            }

            for (int i = 0; i < _packedInCount; i++)
            {
                int handlerIndex = _packedInSlots[i];
                Delegate callback = _packedInCallbacks[i];
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

                subscribers[idx++] = new EventDebugSubscriberInfo(
                    handlerIndex,
                    _versions[handlerIndex],
                    callback.Method.DeclaringType?.FullName ?? "<UnknownType>",
                    callback.Method.Name,
                    target?.GetType().FullName ?? "<Static>",
                    unityTarget,
                    isStatic,
                    isUnityObjectDestroyed,
                    false,
                    true,
                    isCompilerGeneratedTarget,
                    isCompilerGeneratedMethod);
            }

            return subscribers;
        }
#endif
    }
}
