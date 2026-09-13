using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    internal interface IUIResourceLoader
    {
        GameObject Load(UIResRegistry.UIResInfo resource, Transform parent);
        UniTask<GameObject> LoadAsync(UIResRegistry.UIResInfo resource, Transform parent, CancellationToken token);
    }

    internal sealed class UIResourceLoader : IUIResourceLoader
    {
        public GameObject Load(UIResRegistry.UIResInfo resource, Transform parent) =>
            UIHolderFactory.LoadUIResourcesSync(resource, parent);
        public UniTask<GameObject> LoadAsync(UIResRegistry.UIResInfo resource, Transform parent, CancellationToken token) =>
            UIHolderFactory.LoadUIResourcesAsync(resource, parent, token);
    }
}
