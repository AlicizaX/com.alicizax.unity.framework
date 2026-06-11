using System.Collections.Generic;
using UnityEngine;

namespace AlicizaX.Resource.Runtime
{
    [DisallowMultipleComponent]
    public sealed class ResourceOwner : MonoBehaviour
    {
        private const int DefaultReleaseBufferCapacity = 64;
        private static readonly List<ResourceOwner> releaseBuffer = new List<ResourceOwner>(DefaultReleaseBufferCapacity);
        private static int releaseBufferCapacity = DefaultReleaseBufferCapacity;

        [SerializeField]
        private int ownerId;

        [SerializeField]
        private int gameObjectId;

        [SerializeField]
        private uint generation;

        [SerializeField]
        private bool isRegistered;

        public int OwnerId => ownerId;
        public int GameObjectId => gameObjectId;
        public uint Generation => generation;
        public bool IsRegistered => isRegistered;

        internal void SetRegistered(int newOwnerId, int newGameObjectId, uint newGeneration)
        {
            ownerId = newOwnerId;
            gameObjectId = newGameObjectId;
            generation = newGeneration;
            isRegistered = true;
        }

        internal void ClearRegistered()
        {
            ownerId = 0;
            gameObjectId = 0;
            generation = 0;
            isRegistered = false;
        }

        public ResourceBindStatus ReleaseBindings()
        {
            if (!isRegistered)
            {
                return ResourceBindStatus.MissingOwner;
            }

            int currentOwnerId = ownerId;
            uint currentGeneration = generation;
            ResourceBindStatus status = ResourceBindStatus.ServiceShutdown;
            if (AppServices.TryGet<IResourceService>(out var resourceService))
            {
                IResourceBindingService bindingService = resourceService.BindingService;
                if (bindingService != null)
                {
                    status = bindingService.ReleaseOwner(currentOwnerId, currentGeneration);
                }
            }

            if (isRegistered && ownerId == currentOwnerId && generation == currentGeneration)
            {
                ClearRegistered();
            }

            return status;
        }

        public static int ReleaseBindingsInHierarchy(GameObject root)
        {
            if (root == null)
            {
                return 0;
            }

            if (releaseBuffer.Capacity < releaseBufferCapacity)
            {
                releaseBuffer.Capacity = releaseBufferCapacity;
            }

            root.GetComponentsInChildren(true, releaseBuffer);

            int releasedCount = 0;
            for (int i = 0; i < releaseBuffer.Count; i++)
            {
                ResourceOwner owner = releaseBuffer[i];
                if (owner == null || !owner.isRegistered)
                {
                    continue;
                }

                owner.ReleaseBindings();
                releasedCount++;
            }

            releaseBuffer.Clear();
            return releasedCount;
        }

        public static void WarmupReleaseBuffer(int capacity)
        {
            if (capacity <= releaseBufferCapacity)
            {
                return;
            }

            releaseBufferCapacity = capacity;
            releaseBuffer.Capacity = capacity;
        }

        public static ResourceOwner EnsureFor(Component target, IResourceBindingService bindingService)
        {
            if (target == null || target.gameObject == null)
            {
                return null;
            }

            ResourceOwner owner = target.GetComponent<ResourceOwner>();
            if (owner == null)
            {
                owner = target.gameObject.AddComponent<ResourceOwner>();
            }

            bindingService?.RegisterOwner(owner);
            return owner;
        }

        private void OnDestroy()
        {
            if (!isRegistered)
            {
                return;
            }

            ReleaseBindings();
        }
    }
}
