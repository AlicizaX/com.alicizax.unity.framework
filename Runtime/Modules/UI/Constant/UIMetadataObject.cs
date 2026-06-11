using System;
using AlicizaX.ObjectPool;

namespace AlicizaX.UI.Runtime
{
    internal class UIMetadataObject : ObjectBase<UIMetadata>
    {
        public static UIMetadataObject Create(UIMetadata target, string name)
        {
            UIMetadataObject obj = MemoryPool.Acquire<UIMetadataObject>();
            obj.Initialize(name, target);
            return obj;
        }

        protected internal override void Release(bool isShutdown)
        {
            UIMetadata metadata = Target;
            if (metadata != null)
            {
                metadata.ResetRuntimeState();
            }
        }

        protected internal override void OnUnspawn()
        {
            base.OnUnspawn();

            UIMetadata metadata = Target;
            if (metadata != null)
            {
                metadata.ResetRuntimeState();
            }
        }
    }
}
