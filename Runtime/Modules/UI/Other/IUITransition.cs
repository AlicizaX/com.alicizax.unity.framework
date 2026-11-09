using Cysharp.Threading.Tasks;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// UI 转场：从当前外观朝打开/关闭播放。播放中再次调用 Open 或 Close 会结束上一次播放并让上一次返回的任务完成；
    /// snap 为 true 时同步落到终态。返回的任务只由视图 await 一次。
    /// </summary>
    public interface IUITransition
    {
        UniTask Open(bool snap);

        UniTask Close(bool snap);
    }
}
