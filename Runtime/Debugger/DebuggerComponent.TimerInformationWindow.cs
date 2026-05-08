using AlicizaX.Timer.Runtime;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class TimerInformationWindow : ScrollableDebuggerWindowBase
        {
            private const int MAX_DISPLAY_COUNT = 64;
            private const float REFRESH_INTERVAL = 0.25f;
            private const string OVERFLOW_NOTE = "Active timer count exceeds visible sample.";
            private const string EMPTY_NOTE = "No active timers.";
            private const string SAMPLE_NOTE = "Runtime view uses fixed buffers and refresh-only style updates.";

            private struct UsageView
            {
                public VisualElement Fill;
            }

            private struct RowView
            {
                public VisualElement Root;
                public VisualElement Fill;
                public VisualElement LoopIndicator;
                public VisualElement ScaleIndicator;
                public VisualElement StateIndicator;
            }

            private ITimerService _timerService;
            private ITimerDebugService _timerDebugService;
            private readonly TimerDebugInfo[] _timerInfos = new TimerDebugInfo[MAX_DISPLAY_COUNT];
            private readonly RowView[] _timerRows = new RowView[MAX_DISPLAY_COUNT];
            private UsageView _activeUsage;
            private UsageView _peakUsage;
            private UsageView _freeUsage;
            private VisualElement _overflowNote;
            private VisualElement _emptyNote;
            private float _refreshCountdown;
            private int _visibleRowCount;

            public override void Initialize(params object[] args)
            {
                if (AppServices.TryGet(out _timerService))
                {
                    _timerDebugService = _timerService as ITimerDebugService;
                }
            }

            public override void OnEnter()
            {
                _refreshCountdown = 0f;
                RefreshContent();
            }

            public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
            {
                _refreshCountdown -= realElapseSeconds;
                if (_refreshCountdown > 0f)
                {
                    return;
                }

                _refreshCountdown = REFRESH_INTERVAL;
                RefreshContent();
            }

            protected override void BuildWindow(VisualElement root)
            {
                root.Add(CreateActionButton("Refresh", RefreshContent, DebuggerTheme.ButtonSurfaceActive, DebuggerTheme.PrimaryText));

                VisualElement overview = CreateSection("Timer Pool", out VisualElement overviewCard);
                overviewCard.Add(CreateUsageRow("Active", DebuggerTheme.Accent, out _activeUsage));
                overviewCard.Add(CreateUsageRow("Peak", DebuggerTheme.Warning, out _peakUsage));
                overviewCard.Add(CreateUsageRow("Free", DebuggerTheme.Positive, out _freeUsage));
                root.Add(overview);

                VisualElement sample = CreateSection("Timer Sample", out VisualElement sampleCard);
                sampleCard.Add(CreateNoteLabel(SAMPLE_NOTE, DebuggerTheme.SecondaryText));
                _overflowNote = CreateNoteLabel(OVERFLOW_NOTE, DebuggerTheme.Warning);
                _overflowNote.style.display = DisplayStyle.None;
                sampleCard.Add(_overflowNote);

                _emptyNote = CreateNoteLabel(EMPTY_NOTE, DebuggerTheme.SecondaryText);
                _emptyNote.style.display = DisplayStyle.None;
                sampleCard.Add(_emptyNote);

                _visibleRowCount = 0;
                for (int i = 0; i < MAX_DISPLAY_COUNT; i++)
                {
                    _timerRows[i] = CreateTimerRow(sampleCard);
                    _timerRows[i].Root.style.display = DisplayStyle.None;
                }

                root.Add(sample);
                RefreshContent();
            }

            private void RefreshContent()
            {
                if (_emptyNote == null || _overflowNote == null)
                {
                    return;
                }

                if (_timerService == null)
                {
                    if (AppServices.TryGet(out _timerService))
                    {
                        _timerDebugService = _timerService as ITimerDebugService;
                    }

                    if (_timerDebugService == null)
                    {
                        SetVisibleRows(0);
                        return;
                    }
                }

                _timerDebugService.GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount);
                float capacity = poolCapacity > 0 ? poolCapacity : 1f;
                SetFillRatio(_activeUsage.Fill, activeCount / capacity);
                SetFillRatio(_peakUsage.Fill, peakActiveCount / capacity);
                SetFillRatio(_freeUsage.Fill, freeCount / capacity);

                if (activeCount <= 0)
                {
                    _emptyNote.style.display = DisplayStyle.Flex;
                    _overflowNote.style.display = DisplayStyle.None;
                    SetVisibleRows(0);
                    return;
                }

                _emptyNote.style.display = DisplayStyle.None;
                _overflowNote.style.display = activeCount > MAX_DISPLAY_COUNT ? DisplayStyle.Flex : DisplayStyle.None;

                int timerCount = _timerDebugService.GetAllTimers(_timerInfos);
                int displayCount = timerCount < MAX_DISPLAY_COUNT ? timerCount : MAX_DISPLAY_COUNT;
                for (int i = 0; i < displayCount; i++)
                {
                    UpdateTimerRow(ref _timerRows[i], ref _timerInfos[i]);
                }

                SetVisibleRows(displayCount);
            }

            private void SetVisibleRows(int visibleCount)
            {
                if (_visibleRowCount == visibleCount)
                {
                    return;
                }

                int minCount = _visibleRowCount < visibleCount ? _visibleRowCount : visibleCount;
                for (int i = minCount; i < visibleCount; i++)
                {
                    _timerRows[i].Root.style.display = DisplayStyle.Flex;
                }

                for (int i = visibleCount; i < _visibleRowCount; i++)
                {
                    _timerRows[i].Root.style.display = DisplayStyle.None;
                }

                _visibleRowCount = visibleCount;
            }

            private static VisualElement CreateUsageRow(string title, Color fillColor, out UsageView usageView)
            {
                float scale = GetScale();
                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Column;
                row.style.marginBottom = 8f * scale;

                Label titleLabel = new Label(title);
                titleLabel.style.color = DebuggerTheme.SecondaryText;
                titleLabel.style.fontSize = 16f * scale;
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.marginBottom = 4f * scale;
                row.Add(titleLabel);

                VisualElement track = CreateTrack(14f * scale);
                VisualElement fill = new VisualElement();
                fill.style.height = Length.Percent(100f);
                fill.style.width = Length.Percent(0f);
                fill.style.backgroundColor = fillColor;
                track.Add(fill);
                row.Add(track);

                usageView.Fill = fill;
                return row;
            }

            private static RowView CreateTimerRow(VisualElement parent)
            {
                float scale = GetScale();
                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.height = 20f * scale;
                row.style.marginBottom = 4f * scale;

                RowView view;
                view.Root = row;
                view.LoopIndicator = CreateIndicator(6f * scale, DebuggerTheme.Accent);
                view.ScaleIndicator = CreateIndicator(6f * scale, DebuggerTheme.Warning);
                view.StateIndicator = CreateIndicator(6f * scale, DebuggerTheme.Positive);
                row.Add(view.LoopIndicator);
                row.Add(view.ScaleIndicator);
                row.Add(view.StateIndicator);

                VisualElement track = CreateTrack(14f * scale);
                view.Fill = new VisualElement();
                view.Fill.style.height = Length.Percent(100f);
                view.Fill.style.width = Length.Percent(0f);
                view.Fill.style.backgroundColor = DebuggerTheme.Positive;
                track.Add(view.Fill);
                row.Add(track);
                parent.Add(row);
                return view;
            }

            private static VisualElement CreateTrack(float height)
            {
                VisualElement track = new VisualElement();
                track.style.flexGrow = 1f;
                track.style.height = height;
                track.style.backgroundColor = DebuggerTheme.PanelSurfaceStrong;
                track.style.borderTopLeftRadius = 4f;
                track.style.borderTopRightRadius = 4f;
                track.style.borderBottomLeftRadius = 4f;
                track.style.borderBottomRightRadius = 4f;
                track.style.overflow = Overflow.Hidden;
                return track;
            }

            private static Label CreateNoteLabel(string text, Color color)
            {
                float scale = GetScale();
                Label label = new Label(text);
                label.style.color = color;
                label.style.fontSize = 15f * scale;
                label.style.marginBottom = 6f * scale;
                label.style.whiteSpace = WhiteSpace.Normal;
                return label;
            }

            private static VisualElement CreateIndicator(float size, Color color)
            {
                VisualElement indicator = new VisualElement();
                indicator.style.width = size;
                indicator.style.height = size;
                indicator.style.marginRight = size;
                indicator.style.backgroundColor = color;
                indicator.style.opacity = 0.2f;
                return indicator;
            }

            private static void UpdateTimerRow(ref RowView row, ref TimerDebugInfo info)
            {
                byte flags = info.Flags;
                bool isRunning = (flags & TimerDebugFlags.Running) != 0;
                bool isLoop = (flags & TimerDebugFlags.Loop) != 0;
                bool isUnscaled = (flags & TimerDebugFlags.Unscaled) != 0;
                float ratio = info.Duration > 0f ? info.LeftTime / info.Duration : 0f;

                row.LoopIndicator.style.opacity = isLoop ? 1f : 0.2f;
                row.ScaleIndicator.style.opacity = isUnscaled ? 1f : 0.35f;
                row.ScaleIndicator.style.backgroundColor = isUnscaled ? DebuggerTheme.Warning : DebuggerTheme.Accent;
                row.StateIndicator.style.opacity = isRunning ? 1f : 0.45f;
                row.StateIndicator.style.backgroundColor = isRunning ? DebuggerTheme.Positive : DebuggerTheme.Warning;
                row.Fill.style.backgroundColor = isRunning ? ratio <= 0.2f ? DebuggerTheme.Warning : DebuggerTheme.Positive : DebuggerTheme.SecondaryText;
                SetFillRatio(row.Fill, ratio);
            }

            private static void SetFillRatio(VisualElement fill, float ratio)
            {
                if (fill == null)
                {
                    return;
                }

                if (ratio < 0f)
                {
                    ratio = 0f;
                }
                else if (ratio > 1f)
                {
                    ratio = 1f;
                }

                fill.style.width = Length.Percent(ratio * 100f);
            }

            private static float GetScale()
            {
                return Instance != null ? Instance.GetUiScale() : 1f;
            }
        }
    }
}
