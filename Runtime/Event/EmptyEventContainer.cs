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
        private static int[] _activeSlots;
        private static int[] _freeSlots;
        private static int[] _activeIndices;

        private static int _freeCount;
        private static int _activeCount;
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
            _activeSlots = new int[InitialSize];
            _freeSlots = new int[InitialSize];
            _activeIndices = new int[InitialSize];
            _freeCount = InitialSize;

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
            EnsureActiveIndicesCapacity();

            int activeIndex = _activeCount++;
            _activeIndices[activeIndex] = handlerIndex;

            int version = ++_version;
            _callbacks[handlerIndex] = callback;
            _versions[handlerIndex] = version;
            _activeSlots[handlerIndex] = activeIndex;

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordSubscribe<TPayload>(_activeCount, _callbacks.Length);
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

            int oldLen = _callbacks.Length;
            int newSize = oldLen == 0 ? 64 : oldLen << 1;

            Array.Resize(ref _callbacks, newSize);
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
                EventDebugRegistry.RecordResize<TPayload>(_activeCount, _callbacks.Length);
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

            _callbacks[handlerIndex] = null;
            _versions[handlerIndex] = 0;
            _activeSlots[handlerIndex] = 0;
            _activeIndices[lastActiveIndex] = 0;
            _freeSlots[_freeCount++] = handlerIndex;

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordUnsubscribe<TPayload>(_activeCount, _callbacks.Length);
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
#endif

            _publishDepth++;
            try
            {
#if UNITY_EDITOR
                EventDebugRegistry.RecordPublish<TPayload>(_activeCount, _callbacks.Length);
#endif
                PublishCore();
            }
            finally
            {
                _publishDepth--;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PublishCore()
        {
            int count = _activeCount;
            if (count == 0) return;

            int[] indices = _activeIndices;
            Action[] callbacks = _callbacks;

            int i = 0;
            int unrolled = count & ~3;

            for (; i < unrolled; i += 4)
            {
                callbacks[indices[i]]();
                callbacks[indices[i + 1]]();
                callbacks[indices[i + 2]]();
                callbacks[indices[i + 3]]();
            }

            switch (count - i)
            {
                case 3:
                    callbacks[indices[i]]();
                    callbacks[indices[i + 1]]();
                    callbacks[indices[i + 2]]();
                    break;
                case 2:
                    callbacks[indices[i]]();
                    callbacks[indices[i + 1]]();
                    break;
                case 1:
                    callbacks[indices[i]]();
                    break;
            }
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

            if (_callbacks.Length >= capacity) return;

            int oldLen = _callbacks.Length;
            Array.Resize(ref _callbacks, capacity);
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
                EventDebugRegistry.RecordResize<TPayload>(_activeCount, _callbacks.Length);
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

                _callbacks[idx] = null;
                _versions[idx] = 0;
                _activeSlots[idx] = 0;
                _freeSlots[_freeCount++] = idx;
                _activeIndices[i] = 0;
            }

            _activeCount = 0;

#if UNITY_EDITOR
            if (!EventDebugRegistry.BenchmarkReleaseLikeMode)
            {
                EventDebugRegistry.RecordClear<TPayload>(_activeCount, _callbacks.Length);
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
                EventDebugRegistry.RecordMutationRejected<TPayload>(_activeCount, _callbacks.Length);
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
        private static int GetDebugSubscriberCount() => _activeCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetDebugCapacity() => _callbacks.Length;

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
                Action callback = _callbacks[handlerIndex];
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
