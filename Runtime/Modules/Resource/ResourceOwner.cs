using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace AlicizaX.Resource.Runtime
{
    [DisallowMultipleComponent]
    public sealed class ResourceOwner : MonoBehaviour
    {
        private static readonly Stack<List<ResourceOwner>> releaseBuffers = new Stack<List<ResourceOwner>>();
        private static int releaseBufferCapacity = 64;
        private int ownerId;
        private ulong gameObjectId;
        private uint generation;

        public int OwnerId => ownerId;
        public ulong GameObjectId => gameObjectId;
        public uint Generation => generation;
        public bool IsRegistered => BindingService != null;
        internal ResourceBindingService BindingService { get; private set; }

        internal void SetRegistered(ResourceBindingService service, int newOwnerId, ulong newGameObjectId, uint newGeneration)
        {
            BindingService = service;
            ownerId = newOwnerId;
            gameObjectId = newGameObjectId;
            generation = newGeneration;
        }

        internal void ClearRegistered()
        {
            BindingService = null;
            ownerId = 0;
            gameObjectId = 0;
            generation = 0;
        }

        public ResourceBindStatus ReleaseBindings()
        {
            return BindingService == null ? ResourceBindStatus.MissingOwner : BindingService.ReleaseOwner(ownerId, generation, true);
        }

        public static int ReleaseBindingsInHierarchy(GameObject root)
        {
            if (root == null) return 0;
            List<ResourceOwner> buffer = releaseBuffers.Count > 0 ? releaseBuffers.Pop() : new List<ResourceOwner>(releaseBufferCapacity);
            int releasedCount = 0;
            Exception error = null;
            try
            {
                root.GetComponentsInChildren(true, buffer);
                for (int i = 0; i < buffer.Count; i++)
                {
                    ResourceOwner owner = buffer[i];
                    if (owner == null || !owner.IsRegistered) continue;
                    try { owner.ReleaseBindings(); }
                    catch (Exception exception) { error = error == null ? exception : new AggregateException(error, exception); }
                    releasedCount++;
                }
            }
            finally
            {
                buffer.Clear();
                releaseBuffers.Push(buffer);
            }
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
            return releasedCount;
        }

        public static void WarmupReleaseBuffer(int capacity)
        {
            if (capacity > releaseBufferCapacity) releaseBufferCapacity = capacity;
            if (releaseBuffers.Count == 0)
                releaseBuffers.Push(new List<ResourceOwner>(releaseBufferCapacity));
            else if (releaseBuffers.Peek().Capacity < releaseBufferCapacity)
                releaseBuffers.Peek().Capacity = releaseBufferCapacity;
        }

        public static ResourceOwner RegisterFor(Component target, IResourceBindingService bindingService)
        {
            if (bindingService == null) throw new ArgumentNullException(nameof(bindingService));
            if (target == null) return null;
            ResourceOwner owner = GetOrAdd(target);
            return bindingService.RegisterOwner(owner) == ResourceBindStatus.Success ? owner : null;
        }

        internal static ResourceOwner GetOrAdd(Component target)
        {
            ResourceOwner owner = target.GetComponent<ResourceOwner>();
            if (owner == null) owner = target.gameObject.AddComponent<ResourceOwner>();
            return owner;
        }

        private void OnDestroy()
        {
            BindingService?.ReleaseOwner(ownerId, generation);
        }
    }
}
