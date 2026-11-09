using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    public enum UITransitionPreset
    {
        None,
        Fade,
        Scale,
        FadeScale,
        SlideFromBottom,
        SlideFromTop,
        SlideFromLeft,
        SlideFromRight,
        Toast,
    }

    public enum UITransitionEase
    {
        Linear,
        InQuad,
        OutQuad,
        InCubic,
        OutCubic,
        InOutCubic,
        OutBack,
    }

    /// <summary>
    /// 预设转场：每次播放是一次逐帧推进的异步循环，被下一次 Open 或 Close 打断时在下一帧退出。
    /// 推进不依赖节点是否激活；打断从当前外观出发。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIPresetTransition : MonoBehaviour, IUITransition
    {
        private struct VisualState
        {
            public Vector2 AnchoredPosition;
            public Vector3 Scale;
            public float Alpha;
        }

        [System.Flags]
        private enum VisualChannels : byte
        {
            None = 0,
            Position = 1,
            Scale = 2,
            Alpha = 4,
        }

        private enum Phase : byte
        {
            Closed,
            Opened,
            Playing,
        }

        [SerializeField] private UITransitionPreset openPreset = UITransitionPreset.FadeScale;
        [SerializeField] private UITransitionPreset closePreset = UITransitionPreset.FadeScale;
        [SerializeField] private UITransitionEase openEase = UITransitionEase.OutCubic;
        [SerializeField] private UITransitionEase closeEase = UITransitionEase.InCubic;
        [SerializeField] private RectTransform targetRect;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private bool useUnscaledTime = true;
        [SerializeField] [Min(0f)] private float openDuration = 0.22f;
        [SerializeField] [Min(0f)] private float closeDuration = 0.18f;
        [SerializeField] [Min(0f)] private float slideDistance = 120f;
        [SerializeField] [Min(0f)] private float toastDistance = 40f;
        [SerializeField] [Range(0.5f, 1f)] private float closedScale = 0.94f;

        private bool _initialized;
        private Phase _phase;
        private int _version;
        private VisualState _openState;
        private VisualState _closedRest;

#if UNITY_EDITOR
        private bool _editorPreviewActive;
        private VisualState _editorPreviewRestoreState;

        private void OnDisable() => EditorStopPreview();
#endif

        public UniTask Open(bool snap)
        {
            EnsureBaseline();
            if (_phase == Phase.Closed)
            {
                RebaseWhileClosed();
                BuildClosedState(openPreset, in _openState, out VisualState start);
                ApplyVisualState(in start);
            }
            else if (_phase == Phase.Opened)
            {
                CaptureCurrentState(out _openState);
            }
            float duration = snap || openPreset == UITransitionPreset.None ? 0f : openDuration;
            return Play(in _openState, Phase.Opened, openEase, duration);
        }

        public UniTask Close(bool snap)
        {
            EnsureBaseline();
            if (_phase == Phase.Closed) RebaseWhileClosed();
            else if (_phase == Phase.Opened) CaptureCurrentState(out _openState);
            BuildClosedState(closePreset, in _openState, out _closedRest);
            float duration = snap || closePreset == UITransitionPreset.None ? 0f : closeDuration;
            return Play(in _closedRest, Phase.Closed, closeEase, duration);
        }

        private UniTask Play(in VisualState to, Phase end, UITransitionEase ease, float duration)
        {
            int version = ++_version;
            if (duration <= 0f)
            {
                ApplyVisualState(in to);
                _phase = end;
                return UniTask.CompletedTask;
            }
            CaptureCurrentState(out VisualState from);
            _phase = Phase.Playing;
            return Run(version, from, to, end, ease, duration);
        }

        private async UniTask Run(int version, VisualState from, VisualState to, Phase end, UITransitionEase ease, float duration)
        {
            VisualChannels channels = ResolveChannels(in from, in to);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
                if (version != _version || this == null) return;
                elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                Lerp(in from, in to, Evaluate(ease, Mathf.Clamp01(elapsed / duration)), out VisualState current);
                ApplyChannels(channels, in current);
            }
            ApplyChannels(channels, in to);
            _phase = end;
        }

        /// <summary>
        /// 关闭期间位置或缩放被改动（布局重排、业务在 OnOpen 里定位），视为新的打开位置。
        /// </summary>
        private void RebaseWhileClosed()
        {
            CaptureCurrentState(out VisualState current);
            if (current.AnchoredPosition != _closedRest.AnchoredPosition) _openState.AnchoredPosition = current.AnchoredPosition;
            if (current.Scale != _closedRest.Scale) _openState.Scale = current.Scale;
        }

        private void EnsureBaseline()
        {
            if (_initialized) return;
            ResolveTargets();
            CaptureCurrentState(out _openState);
            _closedRest = _openState;
            _initialized = true;
        }

        private void ResolveTargets()
        {
            if (targetRect == null) targetRect = (RectTransform)transform;
            if (RequiresCanvasGroup() && canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        }

        private bool RequiresCanvasGroup() => UsesAlpha(openPreset) || UsesAlpha(closePreset);

        private static bool UsesAlpha(UITransitionPreset preset)
        {
            switch (preset)
            {
                case UITransitionPreset.Fade:
                case UITransitionPreset.FadeScale:
                case UITransitionPreset.SlideFromBottom:
                case UITransitionPreset.SlideFromTop:
                case UITransitionPreset.SlideFromLeft:
                case UITransitionPreset.SlideFromRight:
                case UITransitionPreset.Toast:
                    return true;
                default:
                    return false;
            }
        }

        private void BuildClosedState(UITransitionPreset preset, in VisualState baseline, out VisualState state)
        {
            state = baseline;

            switch (preset)
            {
                case UITransitionPreset.None:
                    return;
                case UITransitionPreset.Fade:
                    state.Alpha = 0f;
                    break;
                case UITransitionPreset.Scale:
                    state.Scale = Vector3.Scale(baseline.Scale, Vector3.one * closedScale);
                    break;
                case UITransitionPreset.FadeScale:
                    state.Alpha = 0f;
                    state.Scale = Vector3.Scale(baseline.Scale, Vector3.one * closedScale);
                    break;
                case UITransitionPreset.SlideFromBottom:
                    state.Alpha = 0f;
                    state.AnchoredPosition = baseline.AnchoredPosition + new Vector2(0f, -slideDistance);
                    break;
                case UITransitionPreset.SlideFromTop:
                    state.Alpha = 0f;
                    state.AnchoredPosition = baseline.AnchoredPosition + new Vector2(0f, slideDistance);
                    break;
                case UITransitionPreset.SlideFromLeft:
                    state.Alpha = 0f;
                    state.AnchoredPosition = baseline.AnchoredPosition + new Vector2(-slideDistance, 0f);
                    break;
                case UITransitionPreset.SlideFromRight:
                    state.Alpha = 0f;
                    state.AnchoredPosition = baseline.AnchoredPosition + new Vector2(slideDistance, 0f);
                    break;
                case UITransitionPreset.Toast:
                    state.Alpha = 0f;
                    state.Scale = Vector3.Scale(baseline.Scale, Vector3.one * 0.98f);
                    state.AnchoredPosition = baseline.AnchoredPosition + new Vector2(0f, -toastDistance);
                    break;
            }
        }

        private void CaptureCurrentState(out VisualState state)
        {
            state.AnchoredPosition = targetRect.anchoredPosition;
            state.Scale = targetRect.localScale;
            state.Alpha = canvasGroup != null ? canvasGroup.alpha : 1f;
        }

        private VisualChannels ResolveChannels(in VisualState from, in VisualState to)
        {
            VisualChannels channels = VisualChannels.None;
            if (from.AnchoredPosition != to.AnchoredPosition) channels |= VisualChannels.Position;
            if (from.Scale != to.Scale) channels |= VisualChannels.Scale;
            if (canvasGroup != null && from.Alpha != to.Alpha) channels |= VisualChannels.Alpha;
            return channels;
        }

        private void ApplyChannels(VisualChannels channels, in VisualState state)
        {
            if ((channels & VisualChannels.Position) != 0) targetRect.anchoredPosition = state.AnchoredPosition;
            if ((channels & VisualChannels.Scale) != 0) targetRect.localScale = state.Scale;
            if ((channels & VisualChannels.Alpha) != 0) canvasGroup.alpha = state.Alpha;
        }

        private void ApplyVisualState(in VisualState state)
        {
            targetRect.anchoredPosition = state.AnchoredPosition;
            targetRect.localScale = state.Scale;
            if (canvasGroup != null) canvasGroup.alpha = state.Alpha;
        }

        private static void Lerp(in VisualState from, in VisualState to, float t, out VisualState state)
        {
            state.AnchoredPosition = Vector2.LerpUnclamped(from.AnchoredPosition, to.AnchoredPosition, t);
            state.Scale = Vector3.LerpUnclamped(from.Scale, to.Scale, t);
            state.Alpha = Mathf.LerpUnclamped(from.Alpha, to.Alpha, t);
        }

        private static float Evaluate(UITransitionEase ease, float t)
        {
            switch (ease)
            {
                case UITransitionEase.InQuad:
                    return t * t;
                case UITransitionEase.OutQuad:
                    return 1f - (1f - t) * (1f - t);
                case UITransitionEase.InCubic:
                    return t * t * t;
                case UITransitionEase.OutCubic:
                {
                    float inv = 1f - t;
                    return 1f - inv * inv * inv;
                }
                case UITransitionEase.InOutCubic:
                    return t < 0.5f
                        ? 4f * t * t * t
                        : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
                case UITransitionEase.OutBack:
                {
                    const float c1 = 1.70158f;
                    const float c3 = c1 + 1f;
                    float inv = t - 1f;
                    return 1f + c3 * inv * inv * inv + c1 * inv * inv;
                }
                default:
                    return t;
            }
        }

#if UNITY_EDITOR
        internal void EditorPreviewOpen(float progress)
        {
            EditorPreview(true, progress);
        }

        internal void EditorPreviewClose(float progress)
        {
            EditorPreview(false, progress);
        }

        internal void EditorStopPreview()
        {
            if (!_editorPreviewActive)
            {
                return;
            }

            ApplyVisualState(in _editorPreviewRestoreState);
            _editorPreviewActive = false;
        }

        internal bool EditorHasActivePreview()
        {
            return _editorPreviewActive;
        }

        private void EditorPreview(bool isOpening, float progress)
        {
            ResolveTargets();
            BeginEditorPreview();

            progress = Mathf.Clamp01(progress);
            UITransitionPreset preset = isOpening ? openPreset : closePreset;
            UITransitionEase ease = isOpening ? openEase : closeEase;
            BuildClosedState(preset, in _editorPreviewRestoreState, out VisualState closedState);
            float easedProgress = Evaluate(ease, progress);

            VisualState previewState;
            if (isOpening) Lerp(in closedState, in _editorPreviewRestoreState, easedProgress, out previewState);
            else Lerp(in _editorPreviewRestoreState, in closedState, easedProgress, out previewState);
            ApplyVisualState(in previewState);
        }

        private void BeginEditorPreview()
        {
            if (_editorPreviewActive)
            {
                return;
            }

            CaptureCurrentState(out _editorPreviewRestoreState);
            _editorPreviewActive = true;
        }

        private void OnValidate()
        {
            if (targetRect == null) targetRect = transform as RectTransform;
            if (canvasGroup == null && RequiresCanvasGroup()) canvasGroup = GetComponent<CanvasGroup>();
        }
#endif
    }
}
