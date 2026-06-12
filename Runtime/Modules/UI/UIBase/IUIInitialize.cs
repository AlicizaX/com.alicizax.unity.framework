using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    public interface IUISyncInitialize
    {
        void OnInitialize();
    }

    public interface IUIAsyncInitialize
    {
        UniTask OnInitializeAsync();
    }
}
