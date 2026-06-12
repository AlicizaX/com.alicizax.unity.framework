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
    public sealed class UIPresetTransition : MonoBehaviour, IUITransitionPlayer
    {
        private struct VisualState
        {
            public Vector2 AnchoredPosition;
            public Vector3 Scale;
            public float Alpha;
        }

        [SerializeField] private UITransitionPreset openPreset = UITransitionPreset.FadeScale;
        [SerializeField] private UITransitionPreset closePreset = UITransitionPreset.FadeScale;
        [SerializeField] private UITransitionEase openEase = UITransitionEase.OutCubic;
        [SerializeField] private UITransitionEase closeEase = UITransitionEase.InCubic;
        [SerializeField] private RectTransform targetRect;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private bool useUnscaledTime = true;
        [SerializeField] private bool initializeAsClosed = true;
        [SerializeField] private bool followAnimationInteractable = false;
        [SerializeField] [Min(0f)] private float openDuration = 0.22f;
        [SerializeField] [Min(0f)] private float closeDuration = 0.18f;
        [SerializeField] [Min(0f)] private float slideDistance = 120f;
        [SerializeField] [Min(0f)] private float toastDistance = 40f;
        [SerializeField] [Range(0.5f, 1f)] private float closedScale = 0.94f;

        private bool _initialized;
        private bool _initialClosedStatePending;
        private int _playVersion;
        private VisualState _openState;

#if UNITY_EDITOR
        private bool _editorPreviewActive;
        private VisualState _editorPreviewRestoreState;
        private bool _editorPreviewRestoreInteractable;
        private bool _editorPreviewRestoreBlocksRaycasts;
#endif

        private void Awake()
        {
            EnsureInitialized(initializeAsClosed);
        }

        private void OnDisable()
        {
            Stop();
#if UNITY_EDITOR
            EditorStopPreview();
#endif
        }

        public UniTask PlayOpenAsync()
        {
            EnsureInitialized(false);
            PrepareInitialClosedStateForOpen();
            return PlayAsync(_openState, openDuration, openEase, true);
        }

        public UniTask PlayCloseAsync()
        {
            EnsureInitialized(false);
            return PlayAsync(BuildClosedState(closePreset), closeDuration, closeEase, false);
        }

        public void ApplyOpenState()
        {
            EnsureInitialized(false);
            _initialClosedStatePending = false;
            _playVersion++;
            ApplyVisualState(_openState);
            RestoreInteractionState(true);
        }

        public void ApplyClosedState()
        {
            EnsureInitialized(false);
            _initialClosedStatePending = false;
            _playVersion++;
            ApplyVisualState(BuildClosedState(GetClosedStatePreset()));
            RestoreInteractionState(false);
        }

        public void Stop()
        {
            _playVersion++;
            RestoreInteractionState(true);
        }

        private async UniTask PlayAsync(
            VisualState targetState,
            float duration,
            UITransitionEase ease,
            bool isOpening)
        {
            int playVersion = ++_playVersion;
            RestoreInteractionState(false);

            VisualState currentState = CaptureCurrentState();
            if (duration <= 0f)
            {
                ApplyVisualState(targetState);
                RestoreInteractionState(isOpening);
                return;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (playVersion != _playVersion)
                {
                    return;
                }

                elapsed = Mathf.Min(elapsed + GetDeltaTime(), duration);
                float t = Evaluate(ease, elapsed / duration);
                ApplyVisualState(Lerp(currentState, targetState, t));
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            if (playVersion != _playVersion)
            {
                return;
            }

            ApplyVisualState(targetState);
            RestoreInteractionState(isOpening);
        }

        private void EnsureInitialized(bool applyClosedState)
        {
            if (_initialized)
            {
                return;
            }

            if (targetRect == null)
            {
                targetRect = transform as RectTransform;
            }

            if (RequiresCanvasGroup() && canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            _openState = CaptureCurrentState();
            _initialized = true;

            if (applyClosedState)
            {
                UITransitionPreset preset = GetClosedStatePreset();
                if (preset != UITransitionPreset.None)
                {
                    ApplyVisualState(BuildClosedState(preset));
                    _initialClosedStatePending = true;
                    RestoreInteractionState(false);
                }
            }
            else
            {
                RestoreInteractionState(true);
            }
        }

        private void PrepareInitialClosedStateForOpen()
        {
            if (!_initialClosedStatePending)
            {
                return;
            }

            _initialClosedStatePending = false;

            UITransitionPreset preset = GetClosedStatePreset();
            if (preset == UITransitionPreset.None)
            {
                return;
            }

            VisualState expectedClosedState = BuildClosedState(preset);
            VisualState currentState = CaptureCurrentState();
            bool transformStateChanged = !Approximately(currentState.AnchoredPosition, expectedClosedState.AnchoredPosition)
                                         || !Approximately(currentState.Scale, expectedClosedState.Scale);
            if (transformStateChanged)
            {
                _openState.AnchoredPosition = currentState.AnchoredPosition;
                _openState.Scale = currentState.Scale;
            }

            ApplyVisualState(BuildClosedState(preset));
            RestoreInteractionState(false);
        }

        private UITransitionPreset GetClosedStatePreset()
        {
            return closePreset != UITransitionPreset.None ? closePreset : openPreset;
        }

        private bool RequiresCanvasGroup()
        {
            return followAnimationInteractable || UsesAlpha(openPreset) || UsesAlpha(closePreset);
        }

        private bool UsesAlpha(UITransitionPreset preset)
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

        private VisualState BuildClosedState(UITransitionPreset preset)
        {
            VisualState state = _openState;

            switch (preset)
            {
                case UITransitionPreset.None:
                    return state;
                case UITransitionPreset.Fade:
                    state.Alpha = 0f;
                    break;
                case UITransitionPreset.Scale:
                    state.Scale = Vector3.Scale(_openState.Scale, Vector3.one * closedScale);
                    break;
                case UITransitionPreset.FadeScale:
                    state.Alpha = 0f;
                    state.Scale = Vector3.Scale(_openState.Scale, Vector3.one * closedScale);
                    break;
                case UITransitionPreset.SlideFromBottom:
                    state.Alpha = 0f;
                    state.AnchoredPosition = _openState.AnchoredPosition + new Vector2(0f, -slideDistance);
                    break;
                case UITransitionPreset.SlideFromTop:
                    state.Alpha = 0f;
                    state.AnchoredPosition = _openState.AnchoredPosition + new Vector2(0f, slideDistance);
                    break;
                case UITransitionPreset.SlideFromLeft:
                    state.Alpha = 0f;
                    state.AnchoredPosition = _openState.AnchoredPosition + new Vector2(-slideDistance, 0f);
                    break;
                case UITransitionPreset.SlideFromRight:
                    state.Alpha = 0f;
                    state.AnchoredPosition = _openState.AnchoredPosition + new Vector2(slideDistance, 0f);
                    break;
                case UITransitionPreset.Toast:
                    state.Alpha = 0f;
                    state.Scale = Vector3.Scale(_openState.Scale, Vector3.one * 0.98f);
                    state.AnchoredPosition = _openState.AnchoredPosition + new Vector2(0f, -toastDistance);
                    break;
            }

            return state;
        }

        private VisualState CaptureCurrentState()
        {
            return new VisualState
            {
                AnchoredPosition = targetRect != null ? targetRect.anchoredPosition : Vector2.zero,
                Scale = targetRect != null ? targetRect.localScale : transform.localScale,
                Alpha = canvasGroup != null ? canvasGroup.alpha : 1f,
            };
        }

        private void ApplyVisualState(VisualState state)
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

        private void RestoreInteractionState(bool enabled)
        {
            if (!followAnimationInteractable || canvasGroup == null)
            {
                return;
            }

            canvasGroup.interactable = enabled;
            canvasGroup.blocksRaycasts = enabled;
        }

        private float GetDeltaTime()
        {
            return useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        }

        private VisualState Lerp(VisualState from, VisualState to, float t)
        {
            return new VisualState
            {
                AnchoredPosition = Vector2.LerpUnclamped(from.AnchoredPosition, to.AnchoredPosition, t),
                Scale = Vector3.LerpUnclamped(from.Scale, to.Scale, t),
                Alpha = Mathf.LerpUnclamped(from.Alpha, to.Alpha, t),
            };
        }

        private static bool Approximately(Vector2 lhs, Vector2 rhs)
        {
            return (lhs - rhs).sqrMagnitude <= 0.000001f;
        }

        private static bool Approximately(Vector3 lhs, Vector3 rhs)
        {
            return (lhs - rhs).sqrMagnitude <= 0.000001f;
        }

        private float Evaluate(UITransitionEase ease, float t)
        {
            t = Mathf.Clamp01(t);
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

            ApplyVisualState(_editorPreviewRestoreState);
            if (canvasGroup != null)
            {
                canvasGroup.interactable = _editorPreviewRestoreInteractable;
                canvasGroup.blocksRaycasts = _editorPreviewRestoreBlocksRaycasts;
            }

            _editorPreviewActive = false;
        }

        internal bool EditorHasActivePreview()
        {
            return _editorPreviewActive;
        }

        private void EditorPreview(bool isOpening, float progress)
        {
            EnsureInitialized(false);
            BeginEditorPreview();

            progress = Mathf.Clamp01(progress);
            UITransitionPreset preset = isOpening ? openPreset : closePreset;
            UITransitionEase ease = isOpening ? openEase : closeEase;
            VisualState closedState = BuildClosedState(preset);
            float easedProgress = Evaluate(ease, progress);

            ApplyVisualState(isOpening
                ? Lerp(closedState, _openState, easedProgress)
                : Lerp(_openState, closedState, easedProgress));

            if (canvasGroup != null && followAnimationInteractable)
            {
                bool enabled = isOpening ? progress >= 0.999f : progress <= 0.001f;
                canvasGroup.interactable = enabled;
                canvasGroup.blocksRaycasts = enabled;
            }
        }

        private void BeginEditorPreview()
        {
            if (_editorPreviewActive)
            {
                return;
            }

            _editorPreviewRestoreState = CaptureCurrentState();
            _openState = _editorPreviewRestoreState;

            if (canvasGroup != null)
            {
                _editorPreviewRestoreInteractable = canvasGroup.interactable;
                _editorPreviewRestoreBlocksRaycasts = canvasGroup.blocksRaycasts;
            }

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
