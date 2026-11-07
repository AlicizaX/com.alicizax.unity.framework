using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlicizaX.Scene.Runtime
{
    using Scene = UnityEngine.SceneManagement.Scene;

    public interface ISceneService : IService
    {
        /// <summary>
        /// 当前主场景。主场景尚未由本服务加载，或其句柄已失效时，返回 <c>SceneManager.GetActiveScene()</c>。
        /// </summary>
        Scene MainScene { get; }

        /// <summary>
        /// 加载主场景（<c>LoadSceneMode.Single</c>，会关闭所有现有场景）。
        /// <para>该 location 已是当前主场景时直接返回现有场景，不做重复加载；需要重载请用 <see cref="ReloadMainAsync"/>。</para>
        /// </summary>
        /// <param name="location">YooAsset 资源定位地址。</param>
        /// <param name="progress">
        /// 加载进度：0 起始，资源包阶段推至 0.5，场景阶段推至 0.9 后停住，激活后跳至 1。
        /// YooAsset 不上报 0.5 以下的场景阶段进度，因此进度条呈台阶状而非连续曲线。
        /// </param>
        /// <param name="suspend">为 true 时加载停在激活前，等待 <see cref="AllowActivation"/> 放行，此时 await 不会完成。</param>
        /// <param name="cancellationToken">取消加载；已加载的场景会被卸载，不残留。</param>
        /// <exception cref="GameFrameworkException">另一主场景切换在途，或该 location 已在加载/挂起中，或加载失败。</exception>
        UniTask<Scene> LoadMainAsync(string location, IProgress<float> progress = null, bool suspend = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 强制重新加载主场景，即使它已经是当前主场景。
        /// </summary>
        /// <param name="location">YooAsset 资源定位地址。</param>
        /// <param name="progress">加载进度，语义同 <see cref="LoadMainAsync"/>。</param>
        /// <param name="suspend">为 true 时加载停在激活前，等待 <see cref="AllowActivation"/> 放行。</param>
        /// <param name="cancellationToken">取消加载；已加载的场景会被卸载，不残留。</param>
        /// <exception cref="GameFrameworkException">另一主场景切换在途，或该 location 已在加载/挂起中，或加载失败。</exception>
        UniTask<Scene> ReloadMainAsync(string location, IProgress<float> progress = null, bool suspend = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 以 Additive 方式加载附加场景。
        /// <para>该 location 已加载完成时直接返回现有场景，不做重复加载。</para>
        /// </summary>
        /// <param name="location">YooAsset 资源定位地址。</param>
        /// <param name="progress">加载进度，语义同 <see cref="LoadMainAsync"/>。</param>
        /// <param name="suspend">为 true 时加载停在激活前，等待 <see cref="AllowActivation"/> 放行。</param>
        /// <param name="cancellationToken">取消加载；已加载的场景会被卸载，不残留。</param>
        /// <exception cref="GameFrameworkException">主场景切换在途，或该 location 已在加载/挂起中，或加载失败。</exception>
        UniTask<Scene> LoadAdditiveAsync(string location, IProgress<float> progress = null, bool suspend = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 卸载附加场景。
        /// <para>取消只表示不再等待：卸载操作由 Unity 执行且无法撤销，场景仍会被移除。</para>
        /// </summary>
        /// <param name="location">YooAsset 资源定位地址。</param>
        /// <param name="cancellationToken">取消等待；不撤销卸载本身。</param>
        /// <exception cref="GameFrameworkException">该 location 不是已加载完成的附加场景，或它是主场景，或卸载失败。</exception>
        UniTask UnloadAdditiveAsync(string location, CancellationToken cancellationToken = default);

        /// <summary>
        /// 放行一个以 <c>suspend</c> 方式加载、正停在激活前的场景。
        /// </summary>
        /// <param name="location">YooAsset 资源定位地址。</param>
        /// <returns>该场景存在、句柄有效且确实处于挂起状态时为 true。</returns>
        bool AllowActivation(string location);

        /// <summary>
        /// 把 location 对应的场景设为激活场景。
        /// </summary>
        /// <param name="location">YooAsset 资源定位地址。</param>
        /// <returns>场景不存在、尚未加载完成或激活失败时为 false。</returns>
        bool SetActive(string location);

        /// <summary>
        /// location 是否由本服务持有。
        /// </summary>
        /// <param name="location">YooAsset 资源定位地址。</param>
        /// <returns>加载中与挂起中的场景计入；已被外部卸载（句柄失效）的不计入。</returns>
        bool Contains(string location);
    }
}
