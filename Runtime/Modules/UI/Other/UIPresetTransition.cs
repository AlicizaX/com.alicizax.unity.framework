using System.Threading;
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

    [DisallowMultipleComponent]
    public sealed class UIPresetTransition : MonoBehaviour, ITickTransitionSource
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
        private bool _closed = true;
        private VisualState _openState;

        private VisualState _fromState;
        private VisualState _toState;
        private VisualChannels _channels;
        private Transform _scaleTarget;
        private UITransitionEase _ease;
        private float _duration;
        private float _elapsed;
        private bool _opening;
        private bool _active;

#if UNITY_EDITOR
        private bool _editorPreviewActive;
        private VisualState _editorPreviewRestoreState;

        private void OnDisable() => EditorStopPreview();
#endif

        /// <summary>
        /// 异步播放路径：仅用于外部直接驱动（Holder 在服务存在时走 Tick 驱动）。
        /// </summary>
        public async UniTask Play(bool open, CancellationToken cancellationToken)
        {
            if (!Begin(open)) return;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Advance(Time.deltaTime)) return;
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        public bool Begin(bool open)
        {
            CaptureBaseline();
            _opening = open;
            if (open && _closed)
            {
                BuildClosedState(openPreset, in _openState, out VisualState closed);
                ApplyVisualState(in closed);
            }
            CaptureCurrentState(out _fromState);
            if (open) _toState = _openState;
            else BuildClosedState(closePreset, in _openState, out _toState);
            ResolveChannels();
            _ease = open ? openEase : closeEase;
            _duration = open
                ? (openPreset == UITransitionPreset.None ? 0f : openDuration)
                : (closePreset == UITransitionPreset.None ? 0f : closeDuration);
            _elapsed = 0f;
            if (_duration > 0f)
            {
                _active = true;
                return true;
            }

            _active = false;
            ApplyChannels(in _toState);
            _closed = !open;
            return false;
        }

        public bool Advance(float deltaTime)
        {
            if (!_active) return true;
            _elapsed += GetDeltaTime(deltaTime);
            float progress = Mathf.Clamp01(_elapsed / _duration);
            Lerp(in _fromState, in _toState, Evaluate(_ease, progress), out VisualState current);
            ApplyChannels(in current);
            if (progress < 1f) return false;

            ApplyChannels(in _toState);
            _closed = !_opening;
            _active = false;
            return true;
        }

        public void Snap(bool open)
        {
            CaptureBaseline();
            _closed = !open;
            _active = false;
            if (open)
            {
                ApplyVisualState(in _openState);
                return;
            }

            BuildClosedState(closePreset, in _openState, out VisualState closed);
            ApplyVisualState(in closed);
        }

        private void CaptureBaseline()
        {
            if (_initialized)
            {
                return;
            }

            ResolveTargets();
            _scaleTarget = targetRect != null ? targetRect : transform;
            CaptureCurrentState(out _openState);
            _initialized = true;
        }

        private void ResolveTargets()
        {
            if (targetRect == null)
            {
                targetRect = transform as RectTransform;
            }

            if (RequiresCanvasGroup() && canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();
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
            state.AnchoredPosition = targetRect != null ? targetRect.anchoredPosition : Vector2.zero;
            state.Scale = targetRect != null ? targetRect.localScale : transform.localScale;
            state.Alpha = canvasGroup != null ? canvasGroup.alpha : 1f;
        }

        private void ResolveChannels()
        {
            VisualChannels channels = VisualChannels.None;
            if (targetRect != null && _fromState.AnchoredPosition != _toState.AnchoredPosition)
                channels |= VisualChannels.Position;
            if (_fromState.Scale != _toState.Scale) channels |= VisualChannels.Scale;
            if (canvasGroup != null && _fromState.Alpha != _toState.Alpha) channels |= VisualChannels.Alpha;
            _channels = channels;
        }

        private void ApplyChannels(in VisualState state)
        {
            if ((_channels & VisualChannels.Position) != 0) targetRect.anchoredPosition = state.AnchoredPosition;
            if ((_channels & VisualChannels.Scale) != 0) _scaleTarget.localScale = state.Scale;
            if ((_channels & VisualChannels.Alpha) != 0) canvasGroup.alpha = state.Alpha;
        }

        private void ApplyVisualState(in VisualState state)
        {
            if (targetRect != null)
            {
                targetRect.anchoredPosition = state.AnchoredPosition;
                targetRect.localScale = state.Scale;
            }
            else
            {
                transform.localScale = state.Scale;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = state.Alpha;
            }
        }

        private float GetDeltaTime(float driverDeltaTime) =>
            useUnscaledTime ? Time.unscaledDeltaTime : driverDeltaTime;

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
            if (targetRect == null)
            {
                targetRect = transform as RectTransform;
            }

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }
        }
#endif
    }
}
