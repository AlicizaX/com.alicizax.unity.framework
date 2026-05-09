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
        private static int[] _activeSlots;
        private static int[] _freeSlots;
        private static int[] _activeIndices;

        private static int _freeCount;
        private static int _activeCount;
        private static int _valueSubscriberCount;
        private static int _inSubscriberCount;
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
            _activeSlots = new int[InitialSize];
            _freeSlots = new int[InitialSize];
            _activeIndices = new int[InitialSize];
            _freeCount = InitialSize;

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
            EnsureActiveIndicesCapacity();

            int activeIndex = _activeCount++;
            _activeIndices[activeIndex] = handlerIndex;

            int version = ++_version;
            _valueCallbacks[handlerIndex] = valueCallback;
            _inCallbacks[handlerIndex] = inCallback;
            _versions[handlerIndex] = version;
            _activeSlots[handlerIndex] = activeIndex;

            if (inCallback != null)
            {
                _inSubscriberCount++;
            }
            else
            {
                _valueSubscriberCount++;
            }

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordSubscribe<TPayload>(_activeCount, _valueCallbacks.Length);
            }
#endif

            return new EventRuntimeHandle(TypeId, handlerIndex, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureActiveIndicesCapacity()
        {
            if (_activeCount < _activeIndices.Length)
            {
                return;
            }

            int newSize = _activeIndices.Length == 0 ? 64 : _activeIndices.Length << 1;
            Array.Resize(ref _activeIndices, newSize);
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
            Array.Resize(ref _activeSlots, newSize);
            Array.Resize(ref _freeSlots, newSize);

            for (int i = newSize - 1; i >= oldLen; i--)
            {
                _freeSlots[_freeCount++] = i;
            }

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordResize<TPayload>(_activeCount, _valueCallbacks.Length);
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

            int currentActiveIndex = _activeSlots[handlerIndex];
            int lastActiveIndex = --_activeCount;

            if (currentActiveIndex != lastActiveIndex)
            {
                int lastHandlerIndex = _activeIndices[lastActiveIndex];
                _activeIndices[currentActiveIndex] = lastHandlerIndex;
                _activeSlots[lastHandlerIndex] = currentActiveIndex;
            }

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                RemoveActiveHandler(handlerIndex);
            }
#endif

            if (_inCallbacks[handlerIndex] != null)
            {
                _inSubscriberCount--;
            }
            else
            {
                _valueSubscriberCount--;
            }

            _valueCallbacks[handlerIndex] = null;
            _inCallbacks[handlerIndex] = null;
            _versions[handlerIndex] = 0;
            _activeSlots[handlerIndex] = 0;
            _activeIndices[lastActiveIndex] = 0;
            _freeSlots[_freeCount++] = handlerIndex;

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordUnsubscribe<TPayload>(_activeCount, _valueCallbacks.Length);
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
#endif

            _publishDepth++;
            try
            {
#if UNITY_EDITOR
                EventDebugRegistry.RecordPublish<TPayload>(_activeCount, _valueCallbacks.Length);
#endif
                PublishCore(in payload);
            }
            finally
            {
                _publishDepth--;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PublishCore(in TPayload payload)
        {
            int count = _activeCount;
            if (count == 0) return;

            int[] indices = _activeIndices;
            Action<TPayload>[] valueCallbacks = _valueCallbacks;
            InEventHandler<TPayload>[] inCallbacks = _inCallbacks;

            if (_inSubscriberCount == 0)
            {
                PublishValueOnly(valueCallbacks, indices, count, in payload);
                return;
            }

            if (_valueSubscriberCount == 0)
            {
                PublishInOnly(inCallbacks, indices, count, in payload);
                return;
            }

            PublishMixed(valueCallbacks, inCallbacks, indices, count, in payload);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PublishValueOnly(Action<TPayload>[] valueCallbacks, int[] indices, int count, in TPayload payload)
        {
            int i = 0;
            int unrolled = count & ~3;

            for (; i < unrolled; i += 4)
            {
                valueCallbacks[indices[i]](payload);
                valueCallbacks[indices[i + 1]](payload);
                valueCallbacks[indices[i + 2]](payload);
                valueCallbacks[indices[i + 3]](payload);
            }

            switch (count - i)
            {
                case 3:
                    valueCallbacks[indices[i]](payload);
                    valueCallbacks[indices[i + 1]](payload);
                    valueCallbacks[indices[i + 2]](payload);
                    break;
                case 2:
                    valueCallbacks[indices[i]](payload);
                    valueCallbacks[indices[i + 1]](payload);
                    break;
                case 1:
                    valueCallbacks[indices[i]](payload);
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PublishInOnly(InEventHandler<TPayload>[] inCallbacks, int[] indices, int count, in TPayload payload)
        {
            int i = 0;
            int unrolled = count & ~3;

            for (; i < unrolled; i += 4)
            {
                inCallbacks[indices[i]](in payload);
                inCallbacks[indices[i + 1]](in payload);
                inCallbacks[indices[i + 2]](in payload);
                inCallbacks[indices[i + 3]](in payload);
            }

            switch (count - i)
            {
                case 3:
                    inCallbacks[indices[i]](in payload);
                    inCallbacks[indices[i + 1]](in payload);
                    inCallbacks[indices[i + 2]](in payload);
                    break;
                case 2:
                    inCallbacks[indices[i]](in payload);
                    inCallbacks[indices[i + 1]](in payload);
                    break;
                case 1:
                    inCallbacks[indices[i]](in payload);
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PublishMixed(Action<TPayload>[] valueCallbacks, InEventHandler<TPayload>[] inCallbacks, int[] indices, int count, in TPayload payload)
        {
            int i = 0;
            int unrolled = count & ~3;

            for (; i < unrolled; i += 4)
            {
                InvokeHandler(valueCallbacks, inCallbacks, indices[i], in payload);
                InvokeHandler(valueCallbacks, inCallbacks, indices[i + 1], in payload);
                InvokeHandler(valueCallbacks, inCallbacks, indices[i + 2], in payload);
                InvokeHandler(valueCallbacks, inCallbacks, indices[i + 3], in payload);
            }

            switch (count - i)
            {
                case 3:
                    InvokeHandler(valueCallbacks, inCallbacks, indices[i], in payload);
                    InvokeHandler(valueCallbacks, inCallbacks, indices[i + 1], in payload);
                    InvokeHandler(valueCallbacks, inCallbacks, indices[i + 2], in payload);
                    break;
                case 2:
                    InvokeHandler(valueCallbacks, inCallbacks, indices[i], in payload);
                    InvokeHandler(valueCallbacks, inCallbacks, indices[i + 1], in payload);
                    break;
                case 1:
                    InvokeHandler(valueCallbacks, inCallbacks, indices[i], in payload);
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InvokeHandler(Action<TPayload>[] valueCallbacks, InEventHandler<TPayload>[] inCallbacks, int handlerIndex, in TPayload payload)
        {
            InEventHandler<TPayload> inCallback = inCallbacks[handlerIndex];
            if (inCallback != null)
            {
                inCallback(in payload);
                return;
            }

            valueCallbacks[handlerIndex](payload);
        }

        public static int SubscriberCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _activeCount;
        }

        public static void EnsureCapacity(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            ThrowIfMutatingDuringPublish("ensure capacity");

            if (_valueCallbacks.Length >= capacity) return;

            int oldLen = _valueCallbacks.Length;
            Array.Resize(ref _valueCallbacks, capacity);
            Array.Resize(ref _inCallbacks, capacity);
            Array.Resize(ref _versions, capacity);
            Array.Resize(ref _activeSlots, capacity);
            Array.Resize(ref _freeSlots, capacity);
            Array.Resize(ref _activeIndices, capacity);

            for (int i = capacity - 1; i >= oldLen; i--)
            {
                _freeSlots[_freeCount++] = i;
            }

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordResize<TPayload>(_activeCount, _valueCallbacks.Length);
            }
#endif
        }

        public static void Clear()
        {
            ThrowIfMutatingDuringPublish("clear");

            for (int i = 0; i < _activeCount; i++)
            {
                int idx = _activeIndices[i];

#if UNITY_EDITOR
                if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
                {
                    RemoveActiveHandler(idx);
                }
#endif

                _valueCallbacks[idx] = null;
                _inCallbacks[idx] = null;
                _versions[idx] = 0;
                _activeSlots[idx] = 0;
                _freeSlots[_freeCount++] = idx;
                _activeIndices[i] = 0;
            }

            _activeCount = 0;
            _valueSubscriberCount = 0;
            _inSubscriberCount = 0;

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordClear<TPayload>(_activeCount, _valueCallbacks.Length);
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
                EventDebugRegistry.RecordMutationRejected<TPayload>(_activeCount, _valueCallbacks.Length);
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
        private static int GetDebugSubscriberCount() => _activeCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetDebugCapacity() => _valueCallbacks.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetDebugValueSubscriberCount() => _valueSubscriberCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetDebugInSubscriberCount() => _inSubscriberCount;

        private static EventDebugSubscriberInfo[] GetDebugSubscribers()
        {
            int count = _activeCount;
            if (count == 0)
            {
                return Array.Empty<EventDebugSubscriberInfo>();
            }

            EventDebugSubscriberInfo[] subscribers = new EventDebugSubscriberInfo[count];
            for (int i = 0; i < count; i++)
            {
                int handlerIndex = _activeIndices[i];
                Action<TPayload> valueCallback = _valueCallbacks[handlerIndex];
                InEventHandler<TPayload> inCallback = _inCallbacks[handlerIndex];
                Delegate callback = (Delegate)inCallback ?? valueCallback;
                object target = callback.Target;
                bool isStatic = target == null;
                bool isUnityObjectDestroyed = false;
                bool usesInParameter = inCallback != null;
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
                    usesInParameter,
                    isCompilerGeneratedTarget,
                    isCompilerGeneratedMethod);
            }

            return subscribers;
        }
#endif
    }
}
