using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Editor
{
    internal sealed class EventMonitorWindow : EditorWindow
    {
        private const string MenuPath = "AlicizaX/Event Monitor";
        private const double RepaintIntervalSeconds = 0.25d;
        private const float DefaultLeftPanelWidth = 360f;
        private const float MinLeftPanelWidth = 260f;
        private const float MinRightPanelWidth = 360f;
        private const float SplitterWidth = 5f;

        private static readonly List<Type> s_KnownEventTypes = new();
        private static readonly Dictionary<Type, int> s_InitialCapacityCache = new();

        private Vector2 _eventListScroll;
        private Vector2 _subscriberScroll;
        private Vector2 _historyScroll;
        private string _searchText = string.Empty;
        private bool _onlyInitialized;
        private bool _autoRefresh = true;
        private Type _selectedEventType;
        private double _lastRepaintTime;
        private float _leftPanelWidth = DefaultLeftPanelWidth;
        private bool _isDraggingSplitter;
        private DateTime _snapshotTimeUtc;
        private readonly Dictionary<Type, EventSnapshotEntry> _snapshotEntries = new();

        private readonly struct EventRow
        {
            internal readonly Type EventType;
            internal readonly bool Initialized;
            internal readonly EventDebugSummary Summary;
            internal readonly int InitialCapacity;

            internal EventRow(Type eventType, bool initialized, EventDebugSummary summary, int initialCapacity)
            {
                EventType = eventType;
                Initialized = initialized;
                Summary = summary;
                InitialCapacity = initialCapacity;
            }
        }

        private readonly struct EventSnapshotEntry
        {
            internal readonly EventDebugSummary Summary;
            internal readonly EventDebugSubscriberInfo[] Subscribers;

            internal EventSnapshotEntry(EventDebugSummary summary, EventDebugSubscriberInfo[] subscribers)
            {
                Summary = summary;
                Subscribers = subscribers;
            }
        }

        private readonly struct EventAlert
        {
            internal readonly MessageType Type;
            internal readonly string Message;

            internal EventAlert(MessageType type, string message)
            {
                Type = type;
                Message = message;
            }
        }

        [MenuItem(MenuPath, priority = 50)]
        private static void Open()
        {
            EventMonitorWindow window = GetWindow<EventMonitorWindow>();
            window.titleContent = new GUIContent("事件监视器");
            window.minSize = new Vector2(1080f, 640f);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshKnownEventTypes();
            EventDebugRegistry.BeginDetailedHistory();
            EditorApplication.update += HandleEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= HandleEditorUpdate;
            EventDebugRegistry.EndDetailedHistory();
        }

        private void OnFocus()
        {
            RefreshKnownEventTypes();
            Repaint();
        }

        private void HandleEditorUpdate()
        {
            if (!_autoRefresh)
            {
                return;
            }

            double time = EditorApplication.timeSinceStartup;
            if (time - _lastRepaintTime < RepaintIntervalSeconds)
            {
                return;
            }

            _lastRepaintTime = time;
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();

            Dictionary<Type, EventDebugSummary> summaries = BuildSummaryMap();
            List<EventRow> rows = BuildRows(summaries);
            EnsureSelection(rows);

            DrawSplitLayout(rows, summaries);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("搜索", GUILayout.Width(44f));
            _searchText = GUILayout.TextField(
                _searchText,
                GUI.skin.FindStyle("ToolbarSearchTextField") ?? GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarTextField,
                GUILayout.MinWidth(220f),
                GUILayout.ExpandWidth(true));
            _onlyInitialized = GUILayout.Toggle(_onlyInitialized, "仅已初始化", EditorStyles.toolbarButton, GUILayout.Width(110f));
            _autoRefresh = GUILayout.Toggle(_autoRefresh, "自动刷新", EditorStyles.toolbarButton, GUILayout.Width(92f));

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                RefreshKnownEventTypes();
                Repaint();
            }

            if (GUILayout.Button("重置统计", EditorStyles.toolbarButton, GUILayout.Width(84f)))
            {
                EventDebugRegistry.ResetStats();
                Repaint();
            }

            if (GUILayout.Button("拍摄快照", EditorStyles.toolbarButton, GUILayout.Width(108f)))
            {
                CaptureSnapshot();
                Repaint();
            }

            using (new EditorGUI.DisabledScope(_snapshotEntries.Count == 0))
            {
                if (GUILayout.Button("清空快照", EditorStyles.toolbarButton, GUILayout.Width(96f)))
                {
                    _snapshotEntries.Clear();
                    _snapshotTimeUtc = default;
                    Repaint();
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "这是仅编辑器可用的事件监视器。普通 Publish 保持极限热路径；SafePublisher 会隔离回调异常，并把派发期间的订阅、取消订阅、清空或扩容延迟到最外层派发结束后立即处理。",
                MessageType.Info);

            if (_snapshotEntries.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"快照拍摄时间：{_snapshotTimeUtc.ToLocalTime():HH:mm:ss}，发生变化的事件数量：{CountChangedEvents()}。",
                    MessageType.None);
            }
        }

        private void DrawSplitLayout(List<EventRow> rows, Dictionary<Type, EventDebugSummary> summaries)
        {
            float viewWidth = EditorGUIUtility.currentViewWidth;
            float maxLeftWidth = Mathf.Max(MinLeftPanelWidth, viewWidth - MinRightPanelWidth - SplitterWidth - 24f);
            _leftPanelWidth = Mathf.Clamp(_leftPanelWidth, MinLeftPanelWidth, maxLeftWidth);

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginVertical(GUILayout.Width(_leftPanelWidth), GUILayout.ExpandHeight(true));
            DrawEventList(rows);
            EditorGUILayout.EndVertical();

            Rect splitterRect = GUILayoutUtility.GetRect(
                SplitterWidth,
                SplitterWidth,
                GUILayout.Width(SplitterWidth),
                GUILayout.ExpandHeight(true));
            GUI.Box(splitterRect, GUIContent.none, EditorStyles.helpBox);
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);
            UpdateSplitter(splitterRect, maxLeftWidth);

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawDetailPanel(summaries);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void UpdateSplitter(Rect splitterRect, float maxLeftWidth)
        {
            Event currentEvent = Event.current;

            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                    if (currentEvent.button == 0 && splitterRect.Contains(currentEvent.mousePosition))
                    {
                        _isDraggingSplitter = true;
                        currentEvent.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (_isDraggingSplitter)
                    {
                        _leftPanelWidth = Mathf.Clamp(currentEvent.mousePosition.x, MinLeftPanelWidth, maxLeftWidth);
                        Repaint();
                        currentEvent.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (_isDraggingSplitter)
                    {
                        _isDraggingSplitter = false;
                        currentEvent.Use();
                    }
                    break;
            }
        }

        private void DrawEventList(List<EventRow> rows)
        {
            EditorGUILayout.BeginVertical();
            GUILayout.Label($"事件列表（{rows.Count}）", EditorStyles.boldLabel);

            _eventListScroll = EditorGUILayout.BeginScrollView(_eventListScroll);
            for (int i = 0; i < rows.Count; i++)
            {
                DrawEventRow(rows[i]);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawEventRow(EventRow row)
        {
            bool selected = _selectedEventType == row.EventType;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUIStyle buttonStyle = selected ? EditorStyles.miniButtonMid : EditorStyles.miniButton;
            if (GUILayout.Button(row.EventType.FullName ?? row.EventType.Name, buttonStyle))
            {
                _selectedEventType = row.EventType;
            }

            string capacityText = row.Initialized ? row.Summary.Capacity.ToString() : row.InitialCapacity.ToString();
            string status = row.Initialized ? "已初始化" : "未初始化";
            EditorGUILayout.LabelField(
                $"订阅 {row.Summary.SubscriberCount} | 无参 {row.Summary.EmptySubscriberCount} / in {row.Summary.InSubscriberCount} | 容量 {capacityText} | 发布 {row.Summary.PublishCount} | Safe {row.Summary.SafePublishCount}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"{status} | 初始容量 {row.InitialCapacity}", EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
        }

        private void DrawDetailPanel(Dictionary<Type, EventDebugSummary> summaries)
        {
            EditorGUILayout.BeginVertical();

            if (_selectedEventType == null)
            {
                EditorGUILayout.HelpBox("请先从左侧选择一个事件类型。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            GUILayout.Label(_selectedEventType.FullName ?? _selectedEventType.Name, EditorStyles.boldLabel);

            if (!summaries.TryGetValue(_selectedEventType, out EventDebugSummary summary))
            {
                int initialCapacity = GetInitialCapacity(_selectedEventType);
                EditorGUILayout.HelpBox("这个事件类型在当前域中还没有被初始化。", MessageType.Info);
                EditorGUILayout.LabelField("初始容量", initialCapacity.ToString());
                EditorGUILayout.LabelField("当前订阅数", "0");
                EditorGUILayout.LabelField("发布次数", "0");
                EditorGUILayout.EndVertical();
                return;
            }

            EventDebugRegistry.TryGetDetails(_selectedEventType, out _, out EventDebugSubscriberInfo[] subscribers);
            DrawSummary(summary, GetInitialCapacity(_selectedEventType));
            DrawAlerts(summary, subscribers);
            DrawSnapshotDiff(_selectedEventType, summary, subscribers);
            DrawSubscribers(subscribers);
            DrawHistory();

            EditorGUILayout.EndVertical();
        }

        private static void DrawSummary(EventDebugSummary summary, int initialCapacity)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("摘要", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("初始容量", initialCapacity.ToString());
            EditorGUILayout.LabelField("当前订阅数", summary.SubscriberCount.ToString());
            EditorGUILayout.LabelField("当前派发模式", $"无参 {summary.EmptySubscriberCount} | in {summary.InSubscriberCount}");
            EditorGUILayout.LabelField("峰值订阅数", summary.PeakSubscriberCount.ToString());
            EditorGUILayout.LabelField("当前容量", summary.Capacity.ToString());
            EditorGUILayout.LabelField("容量利用率", FormatRatio(summary.SubscriberCount, summary.Capacity));
            EditorGUILayout.LabelField("发布次数", summary.PublishCount.ToString());
            EditorGUILayout.LabelField("Safe 发布次数", summary.SafePublishCount.ToString());
            EditorGUILayout.LabelField("订阅次数", summary.SubscribeCount.ToString());
            EditorGUILayout.LabelField("取消订阅次数", summary.UnsubscribeCount.ToString());
            EditorGUILayout.LabelField("扩容次数", summary.ResizeCount.ToString());
            EditorGUILayout.LabelField("清空次数", summary.ClearCount.ToString());
            EditorGUILayout.LabelField("发布期非法变更", summary.MutationRejectedCount.ToString());
            EditorGUILayout.LabelField("Safe 回调异常", summary.HandlerExceptionCount.ToString());
            EditorGUILayout.LabelField("Safe 延迟变更", summary.DeferredMutationCount.ToString());
            EditorGUILayout.LabelField("Safe Flush 次数", summary.FlushCount.ToString());
            EditorGUILayout.LabelField("Safe 峰值 Pending", summary.PeakPendingCount.ToString());
            EditorGUILayout.LabelField("最后操作帧", summary.LastOperationFrame.ToString());
            EditorGUILayout.LabelField("最后操作时间", FormatTicks(summary.LastOperationTicksUtc));
            EditorGUILayout.EndVertical();
        }

        private static void DrawAlerts(EventDebugSummary summary, EventDebugSubscriberInfo[] subscribers)
        {
            List<EventAlert> alerts = BuildAlerts(summary, subscribers);
            if (alerts.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            GUILayout.Label("告警", EditorStyles.boldLabel);
            for (int i = 0; i < alerts.Count; i++)
            {
                EditorGUILayout.HelpBox(alerts[i].Message, alerts[i].Type);
            }
        }

        private void DrawSnapshotDiff(Type eventType, EventDebugSummary currentSummary, EventDebugSubscriberInfo[] currentSubscribers)
        {
            if (_snapshotEntries.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            GUILayout.Label("快照对比", EditorStyles.boldLabel);

            if (!_snapshotEntries.TryGetValue(eventType, out EventSnapshotEntry snapshot))
            {
                EditorGUILayout.HelpBox("这个事件在拍摄快照时还不存在。", MessageType.Info);
                return;
            }

            int subscriberDelta = currentSummary.SubscriberCount - snapshot.Summary.SubscriberCount;
            long publishDelta = currentSummary.PublishCount - snapshot.Summary.PublishCount;
            long safePublishDelta = currentSummary.SafePublishCount - snapshot.Summary.SafePublishCount;
            long subscribeDelta = currentSummary.SubscribeCount - snapshot.Summary.SubscribeCount;
            long unsubscribeDelta = currentSummary.UnsubscribeCount - snapshot.Summary.UnsubscribeCount;
            int resizeDelta = currentSummary.ResizeCount - snapshot.Summary.ResizeCount;
            int capacityDelta = currentSummary.Capacity - snapshot.Summary.Capacity;
            int emptySubscriberDelta = currentSummary.EmptySubscriberCount - snapshot.Summary.EmptySubscriberCount;
            int inSubscriberDelta = currentSummary.InSubscriberCount - snapshot.Summary.InSubscriberCount;
            long mutationRejectedDelta = currentSummary.MutationRejectedCount - snapshot.Summary.MutationRejectedCount;
            long handlerExceptionDelta = currentSummary.HandlerExceptionCount - snapshot.Summary.HandlerExceptionCount;
            long deferredMutationDelta = currentSummary.DeferredMutationCount - snapshot.Summary.DeferredMutationCount;
            int flushDelta = currentSummary.FlushCount - snapshot.Summary.FlushCount;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("订阅数变化", FormatSigned(subscriberDelta));
            EditorGUILayout.LabelField("无参订阅变化", FormatSigned(emptySubscriberDelta));
            EditorGUILayout.LabelField("in 订阅变化", FormatSigned(inSubscriberDelta));
            EditorGUILayout.LabelField("发布次数变化", FormatSigned(publishDelta));
            EditorGUILayout.LabelField("Safe 发布变化", FormatSigned(safePublishDelta));
            EditorGUILayout.LabelField("订阅次数变化", FormatSigned(subscribeDelta));
            EditorGUILayout.LabelField("取消订阅次数变化", FormatSigned(unsubscribeDelta));
            EditorGUILayout.LabelField("扩容次数变化", FormatSigned(resizeDelta));
            EditorGUILayout.LabelField("容量变化", FormatSigned(capacityDelta));
            EditorGUILayout.LabelField("非法变更变化", FormatSigned(mutationRejectedDelta));
            EditorGUILayout.LabelField("Safe 异常变化", FormatSigned(handlerExceptionDelta));
            EditorGUILayout.LabelField("Safe 延迟变更变化", FormatSigned(deferredMutationDelta));
            EditorGUILayout.LabelField("Safe Flush 变化", FormatSigned(flushDelta));

            List<string> addedSubscribers = GetSubscriberDiff(currentSubscribers, snapshot.Subscribers);
            List<string> removedSubscribers = GetSubscriberDiff(snapshot.Subscribers, currentSubscribers);

            if (addedSubscribers.Count == 0 && removedSubscribers.Count == 0)
            {
                EditorGUILayout.LabelField("订阅者集合", "无变化");
            }
            else
            {
                if (addedSubscribers.Count > 0)
                {
                    EditorGUILayout.LabelField("新增", string.Join(" | ", addedSubscribers));
                }

                if (removedSubscribers.Count > 0)
                {
                    EditorGUILayout.LabelField("移除", string.Join(" | ", removedSubscribers));
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSubscribers(EventDebugSubscriberInfo[] subscribers)
        {
            EditorGUILayout.Space(4f);
            GUILayout.Label("订阅者", EditorStyles.boldLabel);

            if (subscribers.Length == 0)
            {
                EditorGUILayout.HelpBox("当前没有活跃订阅者。", MessageType.None);
                return;
            }

            _subscriberScroll = EditorGUILayout.BeginScrollView(_subscriberScroll, GUILayout.Height(position.height * 0.45f));
            for (int i = 0; i < subscribers.Length; i++)
            {
                DrawSubscriberRow(subscribers[i]);
            }
            EditorGUILayout.EndScrollView();
        }

        private void CaptureSnapshot()
        {
            _snapshotEntries.Clear();
            _snapshotTimeUtc = DateTime.UtcNow;

            EventDebugSummary[] summaries = EventDebugRegistry.GetSummaries();
            for (int i = 0; i < summaries.Length; i++)
            {
                EventDebugSummary summary = summaries[i];
                EventDebugRegistry.TryGetDetails(summary.EventType, out _, out EventDebugSubscriberInfo[] subscribers);
                _snapshotEntries[summary.EventType] = new EventSnapshotEntry(summary, subscribers);
            }
        }

        private int CountChangedEvents()
        {
            int changedCount = 0;
            EventDebugSummary[] summaries = EventDebugRegistry.GetSummaries();
            for (int i = 0; i < summaries.Length; i++)
            {
                EventDebugSummary summary = summaries[i];
                if (!_snapshotEntries.TryGetValue(summary.EventType, out EventSnapshotEntry snapshot))
                {
                    changedCount++;
                    continue;
                }

                if (summary.SubscriberCount != snapshot.Summary.SubscriberCount ||
                    summary.EmptySubscriberCount != snapshot.Summary.EmptySubscriberCount ||
                    summary.InSubscriberCount != snapshot.Summary.InSubscriberCount ||
                    summary.PublishCount != snapshot.Summary.PublishCount ||
                    summary.SafePublishCount != snapshot.Summary.SafePublishCount ||
                    summary.SubscribeCount != snapshot.Summary.SubscribeCount ||
                    summary.UnsubscribeCount != snapshot.Summary.UnsubscribeCount ||
                    summary.ResizeCount != snapshot.Summary.ResizeCount ||
                    summary.MutationRejectedCount != snapshot.Summary.MutationRejectedCount ||
                    summary.HandlerExceptionCount != snapshot.Summary.HandlerExceptionCount ||
                    summary.DeferredMutationCount != snapshot.Summary.DeferredMutationCount ||
                    summary.FlushCount != snapshot.Summary.FlushCount ||
                    summary.Capacity != snapshot.Summary.Capacity)
                {
                    changedCount++;
                }
            }

            return changedCount;
        }

        private static void DrawSubscriberRow(EventDebugSubscriberInfo subscriber)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"{subscriber.DeclaringTypeName}.{subscriber.MethodName}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("槽位", subscriber.HandlerIndex.ToString());
            EditorGUILayout.LabelField("版本", subscriber.Version.ToString());
            EditorGUILayout.LabelField("目标", subscriber.TargetTypeName);
            EditorGUILayout.LabelField("类型", subscriber.IsStatic ? "静态方法" : "实例方法");
            EditorGUILayout.LabelField("派发方式", GetSubscriberDispatchModeText(subscriber));

            if (subscriber.IsUnityObjectDestroyed)
            {
                EditorGUILayout.HelpBox("Unity 目标对象已经被销毁，但委托仍然存在。", MessageType.Warning);
            }

            if (subscriber.IsCompilerGeneratedTarget || subscriber.IsCompilerGeneratedMethod)
            {
                EditorGUILayout.HelpBox("该订阅者看起来来自 lambda 或闭包，可能带来额外分配或生命周期问题。", MessageType.Info);
            }

            if (subscriber.UnityTarget != null && !subscriber.IsUnityObjectDestroyed)
            {
                if (GUILayout.Button("定位目标", GUILayout.Width(90f)))
                {
                    EditorGUIUtility.PingObject(subscriber.UnityTarget);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawHistory()
        {
            EditorGUILayout.Space(4f);
            GUILayout.Label("最近操作", EditorStyles.boldLabel);

            EventDebugOperationRecord[] history = EventDebugRegistry.GetRecentOperations();
            if (history.Length == 0)
            {
                EditorGUILayout.HelpBox("当前域里还没有记录到任何操作。", MessageType.None);
                return;
            }

            _historyScroll = EditorGUILayout.BeginScrollView(_historyScroll);
            for (int i = 0; i < history.Length; i++)
            {
                EventDebugOperationRecord record = history[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label(GetOperationKindText(record.OperationKind), GUILayout.Width(84f));
                GUILayout.Label(record.EventType.FullName ?? record.EventType.Name);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"订阅 {record.SubscriberCount}", GUILayout.Width(72f));
                GUILayout.Label($"容量 {record.Capacity}", GUILayout.Width(68f));
                GUILayout.Label(FormatTicks(record.TicksUtc), GUILayout.Width(92f));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private static Dictionary<Type, EventDebugSummary> BuildSummaryMap()
        {
            EventDebugSummary[] summaries = EventDebugRegistry.GetSummaries();
            Dictionary<Type, EventDebugSummary> map = new(summaries.Length);
            for (int i = 0; i < summaries.Length; i++)
            {
                map[summaries[i].EventType] = summaries[i];
            }

            return map;
        }

        private List<EventRow> BuildRows(Dictionary<Type, EventDebugSummary> summaries)
        {
            List<EventRow> rows = new(s_KnownEventTypes.Count);
            for (int i = 0; i < s_KnownEventTypes.Count; i++)
            {
                Type eventType = s_KnownEventTypes[i];
                bool initialized = summaries.TryGetValue(eventType, out EventDebugSummary summary);
                EventDebugSummary rowSummary = initialized
                    ? summary
                    : new EventDebugSummary(eventType, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

                EventRow row = new EventRow(eventType, initialized, rowSummary, GetInitialCapacity(eventType));
                if (MatchesFilter(row))
                {
                    rows.Add(row);
                }
            }

            rows.Sort(CompareRows);
            return rows;
        }

        private bool MatchesFilter(EventRow row)
        {
            if (_onlyInitialized && !row.Initialized)
            {
                return false;
            }

            if (string.IsNullOrEmpty(_searchText))
            {
                return true;
            }

            string fullName = row.EventType.FullName ?? row.EventType.Name;
            return fullName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void EnsureSelection(List<EventRow> rows)
        {
            if (_selectedEventType != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].EventType == _selectedEventType)
                    {
                        return;
                    }
                }
            }

            if (rows.Count > 0)
            {
                _selectedEventType = rows[0].EventType;
            }
            else
            {
                _selectedEventType = null;
            }
        }

        private static int CompareRows(EventRow x, EventRow y)
        {
            int initializedCompare = y.Initialized.CompareTo(x.Initialized);
            if (initializedCompare != 0)
            {
                return initializedCompare;
            }

            int activeCompare = y.Summary.SubscriberCount.CompareTo(x.Summary.SubscriberCount);
            if (activeCompare != 0)
            {
                return activeCompare;
            }

            int publishCompare = GetTotalPublishCount(y.Summary).CompareTo(GetTotalPublishCount(x.Summary));
            if (publishCompare != 0)
            {
                return publishCompare;
            }

            return string.CompareOrdinal(x.EventType.FullName, y.EventType.FullName);
        }

        private static void RefreshKnownEventTypes()
        {
            s_KnownEventTypes.Clear();
            s_InitialCapacityCache.Clear();
            foreach (Type eventType in TypeCache.GetTypesDerivedFrom<IEventArgs>())
            {
                if (!eventType.IsValueType || eventType.IsAbstract)
                {
                    continue;
                }

                s_KnownEventTypes.Add(eventType);
            }

            s_KnownEventTypes.Sort((x, y) => string.CompareOrdinal(x.FullName, y.FullName));
        }

        private static int GetInitialCapacity(Type eventType)
        {
            if (s_InitialCapacityCache.TryGetValue(eventType, out int cachedCapacity))
            {
                return cachedCapacity;
            }

            try
            {
                Type initialSizeType = typeof(EventInitialSize<>).MakeGenericType(eventType);
                FieldInfo sizeField = initialSizeType.GetField("Size", BindingFlags.Public | BindingFlags.Static);
                if (sizeField != null && sizeField.GetValue(null) is int size)
                {
                    s_InitialCapacityCache[eventType] = size;
                    return size;
                }
            }
            catch
            {
            }

            s_InitialCapacityCache[eventType] = 0;
            return 0;
        }

        private static string FormatTicks(long ticksUtc)
        {
            if (ticksUtc <= 0)
            {
                return "-";
            }

            return new DateTime(ticksUtc, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss");
        }

        private static string FormatRatio(int value, int capacity)
        {
            if (capacity <= 0)
            {
                return "-";
            }

            float percent = value / (float)capacity * 100f;
            return $"{percent:F1}% ({value}/{capacity})";
        }

        private static string FormatSigned(int value)
        {
            if (value > 0)
            {
                return $"+{value}";
            }

            return value.ToString();
        }

        private static string FormatSigned(long value)
        {
            if (value > 0)
            {
                return $"+{value}";
            }

            return value.ToString();
        }

        private static List<EventAlert> BuildAlerts(EventDebugSummary summary, EventDebugSubscriberInfo[] subscribers)
        {
            List<EventAlert> alerts = new();
            int destroyedTargetCount = 0;
            int compilerGeneratedCount = 0;
            for (int i = 0; i < subscribers.Length; i++)
            {
                if (subscribers[i].IsUnityObjectDestroyed)
                {
                    destroyedTargetCount++;
                }

                if (subscribers[i].IsCompilerGeneratedMethod || subscribers[i].IsCompilerGeneratedTarget)
                {
                    compilerGeneratedCount++;
                }
            }

            if (destroyedTargetCount > 0)
            {
                alerts.Add(new EventAlert(MessageType.Warning, $"发现 {destroyedTargetCount} 个订阅者的 Unity 目标对象已经被销毁。"));
            }

            if (summary.MutationRejectedCount > 0)
            {
                alerts.Add(new EventAlert(MessageType.Warning, $"普通 Publish 派发期间发生了 {summary.MutationRejectedCount} 次非法变更尝试；需要派发期变更时请使用 SafePublisher。"));
            }

            if (summary.HandlerExceptionCount > 0)
            {
                alerts.Add(new EventAlert(MessageType.Warning, $"SafePublisher 捕获了 {summary.HandlerExceptionCount} 次回调异常，请检查对应订阅者。"));
            }

            if (summary.DeferredMutationCount > 0)
            {
                alerts.Add(new EventAlert(MessageType.Info, $"SafePublisher 延迟处理了 {summary.DeferredMutationCount} 次派发期变更，峰值 Pending 为 {summary.PeakPendingCount}。"));
            }

            if (summary.ResizeCount > 0)
            {
                alerts.Add(new EventAlert(MessageType.Warning, $"容器已经扩容 {summary.ResizeCount} 次，当前事件的 Prewarm 可能偏小。"));
            }

            if (summary.PeakSubscriberCount > 0 && summary.Capacity >= summary.PeakSubscriberCount * 4)
            {
                alerts.Add(new EventAlert(MessageType.Info, $"当前容量 {summary.Capacity} 远大于峰值订阅数 {summary.PeakSubscriberCount}，Prewarm 可能偏大。"));
            }

            if (compilerGeneratedCount > 0)
            {
                alerts.Add(new EventAlert(MessageType.Info, $"检测到 {compilerGeneratedCount} 个编译器生成的订阅者，通常意味着 lambda 或闭包。"));
            }

            long churn = summary.SubscribeCount + summary.UnsubscribeCount;
            long totalPublishCount = GetTotalPublishCount(summary);
            if (churn > 0 && totalPublishCount == 0)
            {
                alerts.Add(new EventAlert(MessageType.Info, $"这个事件在当前域中发生了 {churn} 次订阅/取消订阅操作，但从未被发布。"));
            }
            else if (totalPublishCount > 0 && churn > totalPublishCount * 4)
            {
                alerts.Add(new EventAlert(MessageType.Info, $"检测到较高的订阅抖动：订阅变更 {churn} 次，发布次数 {totalPublishCount} 次。"));
            }

            if (summary.SubscriberCount > 0 && totalPublishCount == 0 && summary.LastOperationFrame > 0)
            {
                alerts.Add(new EventAlert(MessageType.Info, "这个事件当前已有订阅者，但在当前域中还没有被发布过。"));
            }

            return alerts;
        }

        private static long GetTotalPublishCount(EventDebugSummary summary)
        {
            return summary.PublishCount + summary.SafePublishCount;
        }

        private static List<string> GetSubscriberDiff(EventDebugSubscriberInfo[] source, EventDebugSubscriberInfo[] baseline)
        {
            Dictionary<string, int> counts = BuildSubscriberCounts(source);
            Dictionary<string, int> baselineCounts = BuildSubscriberCounts(baseline);
            List<string> result = new();

            foreach (KeyValuePair<string, int> pair in counts)
            {
                baselineCounts.TryGetValue(pair.Key, out int baselineCount);
                int delta = pair.Value - baselineCount;
                if (delta <= 0)
                {
                    continue;
                }

                result.Add(delta == 1 ? pair.Key : $"{pair.Key} x{delta}");
            }

            return result;
        }

        private static Dictionary<string, int> BuildSubscriberCounts(EventDebugSubscriberInfo[] subscribers)
        {
            Dictionary<string, int> counts = new();
            for (int i = 0; i < subscribers.Length; i++)
            {
                string key = BuildSubscriberKey(subscribers[i]);
                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;
            }

            return counts;
        }

        private static string BuildSubscriberKey(EventDebugSubscriberInfo subscriber)
        {
            StringBuilder builder = new();
            builder.Append(subscriber.DeclaringTypeName);
            builder.Append('.');
            builder.Append(subscriber.MethodName);
            builder.Append(" -> ");
            builder.Append(subscriber.TargetTypeName);
            if (subscriber.IsStatic)
            {
                builder.Append(" [static]");
            }
            if (subscriber.IsParameterless)
            {
                builder.Append(" [empty]");
            }
            else
            {
                builder.Append(" [in]");
            }

            return builder.ToString();
        }

        private static string GetSubscriberDispatchModeText(EventDebugSubscriberInfo subscriber)
        {
            if (subscriber.IsParameterless)
            {
                return "无参";
            }

            return "in";
        }

        private static string GetOperationKindText(EventDebugOperationKind kind)
        {
            return kind switch
            {
                EventDebugOperationKind.Subscribe => "订阅",
                EventDebugOperationKind.Unsubscribe => "取消订阅",
                EventDebugOperationKind.Publish => "发布",
                EventDebugOperationKind.SafePublish => "Safe发布",
                EventDebugOperationKind.Resize => "扩容",
                EventDebugOperationKind.Clear => "清空",
                EventDebugOperationKind.MutationRejected => "非法变更",
                EventDebugOperationKind.HandlerException => "回调异常",
                EventDebugOperationKind.DeferredMutation => "延迟变更",
                EventDebugOperationKind.Flush => "Flush",
                _ => kind.ToString()
            };
        }
    }
}
