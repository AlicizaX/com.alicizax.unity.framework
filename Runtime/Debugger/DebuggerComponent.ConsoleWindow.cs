using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        [Serializable]
        private sealed class ConsoleWindow : IDebuggerWindow
        {
            private readonly Queue<LogNode> m_LogNodes = new Queue<LogNode>();
            private readonly List<LogNode> _logNodeCache = new List<LogNode>(256);
            private readonly IList _listItems;

            private int m_InfoCount;
            private int m_WarningCount;
            private int m_ErrorCount;
            private int m_FatalCount;
            private bool m_LastLockScroll = true;
            private bool m_LastInfoFilter = true;
            private bool m_LastWarningFilter = true;
            private bool m_LastErrorFilter = true;
            private bool m_LastFatalFilter = true;

            [SerializeField] private bool m_LockScroll = true;
            [SerializeField] private int m_MaxLine = 100;
            [SerializeField] private bool m_InfoFilter = true;
            [SerializeField] private bool m_WarningFilter = true;
            [SerializeField] private bool m_ErrorFilter = true;
            [SerializeField] private bool m_FatalFilter = true;
            [SerializeField] private Color32 m_InfoColor = Color.white;
            [SerializeField] private Color32 m_WarningColor = Color.yellow;
            [SerializeField] private Color32 m_ErrorColor = Color.red;
            [SerializeField] private Color32 m_FatalColor = new Color(0.7f, 0.2f, 0.2f);

            private VisualElement _root;
            private Label _summaryLabel;
            private Toggle _lockScrollToggle;
            private Toggle _infoToggle;
            private Toggle _warningToggle;
            private Toggle _errorToggle;
            private Toggle _fatalToggle;
            private ListView _logListView;
            private TextField _stackField;
            private VisualElement _stackSection;
            private LogNode _selectedNode;

            public ConsoleWindow()
            {
                _listItems = _logNodeCache;
            }

            public bool LockScroll
            {
                get => m_LockScroll;
                set => m_LockScroll = value;
            }

            public int MaxLine
            {
                get => m_MaxLine;
                set => m_MaxLine = Mathf.Max(1, value);
            }

            public bool InfoFilter
            {
                get => m_InfoFilter;
                set => m_InfoFilter = value;
            }

            public bool WarningFilter
            {
                get => m_WarningFilter;
                set => m_WarningFilter = value;
            }

            public bool ErrorFilter
            {
                get => m_ErrorFilter;
                set => m_ErrorFilter = value;
            }

            public bool FatalFilter
            {
                get => m_FatalFilter;
                set => m_FatalFilter = value;
            }

            public int InfoCount => m_InfoCount;

            public int WarningCount => m_WarningCount;

            public int ErrorCount => m_ErrorCount;

            public int FatalCount => m_FatalCount;

            public Color32 InfoColor
            {
                get => m_InfoColor;
                set => m_InfoColor = value;
            }

            public Color32 WarningColor
            {
                get => m_WarningColor;
                set => m_WarningColor = value;
            }

            public Color32 ErrorColor
            {
                get => m_ErrorColor;
                set => m_ErrorColor = value;
            }

            public Color32 FatalColor
            {
                get => m_FatalColor;
                set => m_FatalColor = value;
            }

            public void Initialize(params object[] args)
            {
                Application.logMessageReceived += OnLogMessageReceived;
                m_LockScroll = m_LastLockScroll = Utility.PlayerPrefsX.GetBool("Debugger.Console.LockScroll", true);
                m_InfoFilter = m_LastInfoFilter = Utility.PlayerPrefsX.GetBool("Debugger.Console.InfoFilter", true);
                m_WarningFilter = m_LastWarningFilter = Utility.PlayerPrefsX.GetBool("Debugger.Console.WarningFilter", true);
                m_ErrorFilter = m_LastErrorFilter = Utility.PlayerPrefsX.GetBool("Debugger.Console.ErrorFilter", true);
                m_FatalFilter = m_LastFatalFilter = Utility.PlayerPrefsX.GetBool("Debugger.Console.FatalFilter", true);
            }

            public void Shutdown()
            {
                Application.logMessageReceived -= OnLogMessageReceived;
                Clear();
            }

            public void OnEnter()
            {
                RefreshView();
            }

            public void OnLeave()
            {
            }

            public void OnUpdate(float elapseSeconds, float realElapseSeconds)
            {
                PersistSettingsIfNeeded();
                RefreshView();
            }

            public VisualElement CreateView()
            {
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                _root = new VisualElement();
                _root.style.flexGrow = 1f;
                _root.style.flexDirection = FlexDirection.Column;
                _root.style.paddingLeft = 12f * scale;
                _root.style.paddingRight = 12f * scale;
                _root.style.paddingTop = 12f * scale;
                _root.style.paddingBottom = 12f * scale;

                VisualElement toolbar = CreateToolbar();
                toolbar.style.marginBottom = 8f * scale;
                _root.Add(toolbar);

                _summaryLabel = new Label();
                _summaryLabel.style.color = DebuggerTheme.SecondaryText;
                _summaryLabel.style.fontSize = 17f * scale;
                _summaryLabel.style.marginBottom = 8f * scale;
                _root.Add(_summaryLabel);

                VisualElement splitLayout = new VisualElement();
                splitLayout.style.flexGrow = 1f;
                splitLayout.style.flexShrink = 1f;
                splitLayout.style.flexDirection = FlexDirection.Column;
                splitLayout.style.minHeight = 0f;
                _root.Add(splitLayout);

                VisualElement listContainer = new VisualElement();
                listContainer.style.flexGrow = 0.62f;
                listContainer.style.flexShrink = 1f;
                listContainer.style.flexBasis = 0f;
                listContainer.style.minHeight = 220f * scale;
                listContainer.style.backgroundColor = DebuggerTheme.PanelSurface;
                listContainer.style.borderTopLeftRadius = 0f;
                listContainer.style.borderTopRightRadius = 0f;
                listContainer.style.borderBottomLeftRadius = 0f;
                listContainer.style.borderBottomRightRadius = 0f;
                listContainer.style.borderTopWidth = 1f;
                listContainer.style.borderRightWidth = 0f;
                listContainer.style.borderBottomWidth = 1f;
                listContainer.style.borderLeftWidth = 0f;
                listContainer.style.borderTopColor = DebuggerTheme.Border;
                listContainer.style.borderBottomColor = DebuggerTheme.Border;
                listContainer.style.marginBottom = 8f * scale;
                listContainer.style.overflow = Overflow.Hidden;

                _logListView = new ListView(_listItems, 36f * scale, MakeLogItem, BindLogItem);
                _logListView.style.flexGrow = 1f;
                _logListView.style.flexShrink = 1f;
                _logListView.style.minHeight = 0f;
                _logListView.style.backgroundColor = Color.clear;
                _logListView.style.borderTopWidth = 0f;
                _logListView.style.borderRightWidth = 0f;
                _logListView.style.borderBottomWidth = 0f;
                _logListView.style.borderLeftWidth = 0f;
                _logListView.selectionType = SelectionType.Single;
                _logListView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
                _logListView.fixedItemHeight = 36f * scale;
                _logListView.itemsSource = _listItems;
                _logListView.selectionChanged += OnSelectionChanged;
                _logListView.schedule.Execute(() =>
                {
                    ScrollableDebuggerWindowBase.StyleScrollers(_logListView, scale);
                }).ExecuteLater(0);
                listContainer.Add(_logListView);
                splitLayout.Add(listContainer);

                _stackSection = ScrollableDebuggerWindowBase.CreateSection("Stack Trace", out VisualElement stackCard);
                _stackSection.style.flexGrow = 0.38f;
                _stackSection.style.flexShrink = 1f;
                _stackSection.style.flexBasis = 0f;
                _stackSection.style.minHeight = 180f * scale;
                _stackField = ScrollableDebuggerWindowBase.CreateReadOnlyMultilineText(string.Empty);
                _stackField.style.backgroundColor = Color.clear;
                _stackField.style.minHeight = 0f;
                _stackField.style.flexGrow = 1f;
                _stackField.style.flexShrink = 1f;
                _stackField.style.backgroundColor = Color.clear;
                _stackField.style.color = DebuggerTheme.PrimaryText;
                _stackField.style.marginBottom = 0f;
                _stackField.schedule.Execute(() => ScrollableDebuggerWindowBase.StyleReadOnlyTextFieldInput(_stackField, scale)).ExecuteLater(0);
                stackCard.Add(_stackField);
                splitLayout.Add(_stackSection);

                Button copyButton = ScrollableDebuggerWindowBase.CreateActionButton("Copy Selected", CopySelectedLog, DebuggerTheme.ButtonSurface);
                _root.Add(copyButton);

                RefreshView();
                return _root;
            }

            private VisualElement CreateToolbar()
            {
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                VisualElement toolbar = ScrollableDebuggerWindowBase.CreateToolbarRow();
                toolbar.style.alignItems = Align.Center;

                Button clearButton = ScrollableDebuggerWindowBase.CreateActionButton("Clear", Clear, DebuggerTheme.Danger);
                clearButton.style.height = 30f * scale;
                clearButton.style.minHeight = 30f * scale;
                clearButton.style.marginRight = 8f * scale;

                _lockScrollToggle = ScrollableDebuggerWindowBase.CreateConsoleFilterToggle("Lock", m_LockScroll, DebuggerTheme.Accent, value => m_LockScroll = value);
                _lockScrollToggle.style.marginRight = 12f * scale;

                _infoToggle = ScrollableDebuggerWindowBase.CreateConsoleFilterToggle(string.Empty, m_InfoFilter, DebuggerTheme.PrimaryText, value => m_InfoFilter = value);
                _warningToggle = ScrollableDebuggerWindowBase.CreateConsoleFilterToggle(string.Empty, m_WarningFilter, DebuggerTheme.Warning, value => m_WarningFilter = value);
                _errorToggle = ScrollableDebuggerWindowBase.CreateConsoleFilterToggle(string.Empty, m_ErrorFilter, DebuggerTheme.Danger, value => m_ErrorFilter = value);
                _fatalToggle = ScrollableDebuggerWindowBase.CreateConsoleFilterToggle(string.Empty, m_FatalFilter, DebuggerTheme.Fatal, value => m_FatalFilter = value);

                toolbar.Add(clearButton);
                toolbar.Add(_lockScrollToggle);
                toolbar.Add(_infoToggle);
                toolbar.Add(_warningToggle);
                toolbar.Add(_errorToggle);
                toolbar.Add(_fatalToggle);
                return toolbar;
            }

            private VisualElement MakeLogItem()
            {
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                Label label = new Label();
                label.style.height = 36f * scale;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.paddingLeft = 10f * scale;
                label.style.paddingRight = 8f * scale;
                label.style.fontSize = 17f * scale;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                label.style.borderLeftWidth = 3f * scale;
                label.style.borderLeftColor = Color.clear;
                label.style.borderBottomWidth = 0f;
                return label;
            }

            private void BindLogItem(VisualElement element, int index)
            {
                Label label = (Label)element;
                if (index < 0 || index >= _logNodeCache.Count)
                {
                    label.text = string.Empty;
                    return;
                }

                LogNode logNode = _logNodeCache[index];
                label.text = GetLogString(logNode);
                label.style.color = GetLogStringColor(logNode.LogType);
                bool isSelected = _logListView != null && index == _logListView.selectedIndex;
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                label.style.backgroundColor = isSelected ? DebuggerTheme.SelectionFill : Color.clear;
                label.style.borderLeftWidth = 3f * scale;
                label.style.borderLeftColor = isSelected ? DebuggerTheme.Accent : Color.clear;
                label.style.color = isSelected ? DebuggerTheme.PrimaryText : GetLogStringColor(logNode.LogType);
            }

            private void OnSelectionChanged(IEnumerable<object> selection)
            {
                _selectedNode = null;
                foreach (object item in selection)
                {
                    _selectedNode = item as LogNode;
                    break;
                }

                UpdateStackField();
            }

            private void UpdateStackField()
            {
                if (_stackField == null)
                {
                    return;
                }

                if (_selectedNode == null)
                {
                    _stackField.value = string.Empty;
                    return;
                }

                _stackField.value = Utility.Text.Format("{0}{2}{2}{1}", _selectedNode.LogMessage, _selectedNode.StackTrack, Environment.NewLine);
            }

            private void CopySelectedLog()
            {
                if (_selectedNode == null)
                {
                    return;
                }

                CopyToClipboard(Utility.Text.Format("{0}{2}{2}{1}", _selectedNode.LogMessage, _selectedNode.StackTrack, Environment.NewLine));
            }

            private void Clear()
            {
                while (m_LogNodes.Count > 0)
                {
                    MemoryPool.Release(m_LogNodes.Dequeue());
                }

                _logNodeCache.Clear();
                _selectedNode = null;
                if (_logListView != null)
                {
                    _logListView.Rebuild();
                }

                UpdateStackField();
                RefreshCount();
            }

            public void RefreshCount()
            {
                m_InfoCount = 0;
                m_WarningCount = 0;
                m_ErrorCount = 0;
                m_FatalCount = 0;
                foreach (LogNode logNode in m_LogNodes)
                {
                    switch (logNode.LogType)
                    {
                        case LogType.Log:
                            m_InfoCount++;
                            break;
                        case LogType.Warning:
                            m_WarningCount++;
                            break;
                        case LogType.Error:
                            m_ErrorCount++;
                            break;
                        case LogType.Exception:
                            m_FatalCount++;
                            break;
                    }
                }
            }

            public void GetRecentLogs(List<LogNode> results)
            {
                if (results == null)
                {
                    Log.Error("Results is invalid.");
                    return;
                }

                results.Clear();
                foreach (LogNode logNode in m_LogNodes)
                {
                    results.Add(logNode);
                }
            }

            public void GetRecentLogs(List<LogNode> results, int count)
            {
                if (results == null)
                {
                    Log.Error("Results is invalid.");
                    return;
                }

                if (count <= 0)
                {
                    Log.Error("Count is invalid.");
                    return;
                }

                int position = m_LogNodes.Count - count;
                if (position < 0)
                {
                    position = 0;
                }

                int index = 0;
                results.Clear();
                foreach (LogNode logNode in m_LogNodes)
                {
                    if (index++ < position)
                    {
                        continue;
                    }

                    results.Add(logNode);
                }
            }

            private void PersistSettingsIfNeeded()
            {
                if (m_LastLockScroll != m_LockScroll)
                {
                    m_LastLockScroll = m_LockScroll;
                    Utility.PlayerPrefsX.SetBool("Debugger.Console.LockScroll", m_LockScroll);
                }

                if (m_LastInfoFilter != m_InfoFilter)
                {
                    m_LastInfoFilter = m_InfoFilter;
                    Utility.PlayerPrefsX.SetBool("Debugger.Console.InfoFilter", m_InfoFilter);
                }

                if (m_LastWarningFilter != m_WarningFilter)
                {
                    m_LastWarningFilter = m_WarningFilter;
                    Utility.PlayerPrefsX.SetBool("Debugger.Console.WarningFilter", m_WarningFilter);
                }

                if (m_LastErrorFilter != m_ErrorFilter)
                {
                    m_LastErrorFilter = m_ErrorFilter;
                    Utility.PlayerPrefsX.SetBool("Debugger.Console.ErrorFilter", m_ErrorFilter);
                }

                if (m_LastFatalFilter != m_FatalFilter)
                {
                    m_LastFatalFilter = m_FatalFilter;
                    Utility.PlayerPrefsX.SetBool("Debugger.Console.FatalFilter", m_FatalFilter);
                }
            }

            private void RefreshView()
            {
                RefreshCount();
                _logNodeCache.Clear();
                foreach (LogNode logNode in m_LogNodes)
                {
                    if (!PassFilter(logNode))
                    {
                        continue;
                    }

                    _logNodeCache.Add(logNode);
                }

                if (_summaryLabel != null)
                {
                    _summaryLabel.text = Utility.Text.Format(
                        "Info {0}  Warning {1}  Error {2}  Fatal {3}  Showing {4}",
                        m_InfoCount,
                        m_WarningCount,
                        m_ErrorCount,
                        m_FatalCount,
                        _logNodeCache.Count);
                }

                if (_infoToggle != null)
                {
                    _infoToggle.SetValueWithoutNotify(m_InfoFilter);
                    Label infoLabel = _infoToggle.Q<Label>();
                    if (infoLabel != null) infoLabel.text = Utility.Text.Format("Info ({0})", m_InfoCount);
                }

                if (_warningToggle != null)
                {
                    _warningToggle.SetValueWithoutNotify(m_WarningFilter);
                    Label warningLabel = _warningToggle.Q<Label>();
                    if (warningLabel != null) warningLabel.text = Utility.Text.Format("Warn ({0})", m_WarningCount);
                }

                if (_errorToggle != null)
                {
                    _errorToggle.SetValueWithoutNotify(m_ErrorFilter);
                    Label errorLabel = _errorToggle.Q<Label>();
                    if (errorLabel != null) errorLabel.text = Utility.Text.Format("Error ({0})", m_ErrorCount);
                }

                if (_fatalToggle != null)
                {
                    _fatalToggle.SetValueWithoutNotify(m_FatalFilter);
                    Label fatalLabel = _fatalToggle.Q<Label>();
                    if (fatalLabel != null) fatalLabel.text = Utility.Text.Format("Fatal ({0})", m_FatalCount);
                }

                if (_lockScrollToggle != null)
                {
                    _lockScrollToggle.SetValueWithoutNotify(m_LockScroll);
                }

                if (_logListView != null)
                {
                    int selectedIndex = _selectedNode == null ? -1 : _logNodeCache.IndexOf(_selectedNode);
                    _logListView.Rebuild();
                    if (selectedIndex >= 0)
                    {
                        _logListView.selectedIndex = selectedIndex;
                        if (m_LockScroll)
                        {
                            _logListView.ScrollToItem(selectedIndex);
                        }
                    }
                    else if (_logNodeCache.Count > 0 && m_LockScroll)
                    {
                        _logListView.ScrollToItem(_logNodeCache.Count - 1);
                    }
                }

                UpdateStackField();
            }

            private bool PassFilter(LogNode logNode)
            {
                switch (logNode.LogType)
                {
                    case LogType.Log:
                        return m_InfoFilter;
                    case LogType.Warning:
                        return m_WarningFilter;
                    case LogType.Error:
                        return m_ErrorFilter;
                    case LogType.Exception:
                        return m_FatalFilter;
                    default:
                        return true;
                }
            }

            private void OnLogMessageReceived(string logMessage, string stackTrace, LogType logType)
            {
                if (logType == LogType.Assert)
                {
                    logType = LogType.Error;
                }

                m_LogNodes.Enqueue(LogNode.Create(logType, logMessage, stackTrace));
                while (m_LogNodes.Count > m_MaxLine)
                {
                    MemoryPool.Release(m_LogNodes.Dequeue());
                }
            }

            private string GetLogString(LogNode logNode)
            {
                return Utility.Text.Format("[{0:HH:mm:ss.fff}][{1}] {2}", logNode.LogTime.ToLocalTime(), logNode.LogFrameCount, logNode.LogMessage);
            }

            internal Color GetLogStringColor(LogType logType)
            {
                switch (logType)
                {
                    case LogType.Log:
                        return m_InfoColor;
                    case LogType.Warning:
                        return m_WarningColor;
                    case LogType.Error:
                        return m_ErrorColor;
                    case LogType.Exception:
                        return m_FatalColor;
                    default:
                        return DebuggerTheme.PrimaryText;
                }
            }
        }
    }
}
