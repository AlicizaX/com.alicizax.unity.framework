using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AlicizaX.Debugger.Runtime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game Framework/Debugger")]
    public sealed partial class DebuggerComponent : MonoBehaviour
    {
        private const string DefaultWindowPath = "Console";
        private const string RootWindowTitle = "ALICIZAX DEBUGGER";
        private const string DebuggerRootName = "alicizax-debugger-root";
        private const string SettingsPanelName = "Debugger Runtime Panel Settings";
        private const string DefaultPanelSettingsResourcePath = "DebuggerPanelSettings";
        private const string SettingsEventSystemName = "Debugger Runtime EventSystem";
        private const int DefaultPanelSortingOrder = short.MaxValue - 64;
        private const float MinWindowWidth = 420f;
        private const float MinWindowHeight = 320f;
        private const float MaxWindowScale = 4f;
        private const float MinWindowScale = 0.5f;
        private const float ToggleClickMoveThreshold = 10f;
        private const float ToggleClickSuppressAfterDrag = 0.35f;
        private const float ToggleDoubleClickInterval = 0.3f;
        private const float ToggleDoubleClickMaxDistance = 24f;
        private const float ToggleSnapSmoothTime = 0.08f;
        private const float ToggleSnapStopDistance = 0.5f;
        private const float SidebarIndentWidth = 14f;

        private static DebuggerComponent _instance;

        internal static readonly Rect DefaultIconRect = new Rect(24f, 24f, 180f, 56f);
        internal static readonly Rect DefaultWindowRect = new Rect(24f, 96f, 1320f, 760f);
        internal static readonly float DefaultWindowScale = 1.2f;

        private static readonly Dictionary<LogType, Color> LogColors = new Dictionary<LogType, Color>
        {
            { LogType.Log, DebuggerTheme.PrimaryText },
            { LogType.Warning, DebuggerTheme.Warning },
            { LogType.Error, DebuggerTheme.Danger },
            { LogType.Exception, DebuggerTheme.Fatal },
            { LogType.Assert, DebuggerTheme.Danger },
        };

        [SerializeField] private DebuggerActiveWindowType m_ActiveWindow = DebuggerActiveWindowType.AlwaysOpen;
        [SerializeField] private bool m_ShowFullWindow;
        [SerializeField, Range(0.2f, 1f)] private float m_WindowOpacity = 1f;
        [SerializeField] private bool m_EnableFloatingToggleSnap = true;
        [SerializeField] private PanelSettings m_PanelSettings;
        [SerializeField] private ConsoleWindow m_ConsoleWindow = new ConsoleWindow();

        private readonly List<DebuggerMenuNode> _menuRoots = new List<DebuggerMenuNode>(16);
        private readonly List<IDebuggerWindow> _registeredWindows = new List<IDebuggerWindow>(32);
        private readonly Dictionary<IDebuggerWindow, DebuggerMenuNode> _nodeByWindow = new Dictionary<IDebuggerWindow, DebuggerMenuNode>(32);
        private readonly Dictionary<IDebuggerWindow, VisualElement> _viewByWindow = new Dictionary<IDebuggerWindow, VisualElement>(32);
        private readonly Dictionary<DebuggerMenuNode, SidebarRowState> _sidebarRowStates = new Dictionary<DebuggerMenuNode, SidebarRowState>(64);
        private readonly Dictionary<DebuggerMenuNode, VisualElement> _sidebarChildContainers = new Dictionary<DebuggerMenuNode, VisualElement>(32);

        private IDebuggerService _mDebuggerService;
        private Rect m_IconRect = DefaultIconRect;
        private Rect m_WindowRect = DefaultWindowRect;
        private float m_WindowScale = DefaultWindowScale;

        private SystemInformationWindow m_SystemInformationWindow = new SystemInformationWindow();
        private EnvironmentInformationWindow m_EnvironmentInformationWindow = new EnvironmentInformationWindow();
        private ScreenInformationWindow m_ScreenInformationWindow = new ScreenInformationWindow();
        private GraphicsInformationWindow m_GraphicsInformationWindow = new GraphicsInformationWindow();
        private InputSummaryInformationWindow m_InputSummaryInformationWindow = new InputSummaryInformationWindow();
        private InputTouchInformationWindow m_InputTouchInformationWindow = new InputTouchInformationWindow();
        private InputLocationInformationWindow m_InputLocationInformationWindow = new InputLocationInformationWindow();
        private InputAccelerationInformationWindow m_InputAccelerationInformationWindow = new InputAccelerationInformationWindow();
        private InputGyroscopeInformationWindow m_InputGyroscopeInformationWindow = new InputGyroscopeInformationWindow();
        private InputCompassInformationWindow m_InputCompassInformationWindow = new InputCompassInformationWindow();
        private PathInformationWindow m_PathInformationWindow = new PathInformationWindow();
        private SceneInformationWindow m_SceneInformationWindow = new SceneInformationWindow();
        private TimeInformationWindow m_TimeInformationWindow = new TimeInformationWindow();
        private QualityInformationWindow m_QualityInformationWindow = new QualityInformationWindow();
        private ProfilerInformationWindow m_ProfilerInformationWindow = new ProfilerInformationWindow();
        private WebPlayerInformationWindow m_WebPlayerInformationWindow = new WebPlayerInformationWindow();
        private RuntimeMemorySummaryWindow m_RuntimeMemorySummaryWindow = new RuntimeMemorySummaryWindow();
        private RuntimeMemoryInformationWindow<Object> m_RuntimeMemoryAllInformationWindow = new RuntimeMemoryInformationWindow<Object>();
        private RuntimeMemoryInformationWindow<Texture> m_RuntimeMemoryTextureInformationWindow = new RuntimeMemoryInformationWindow<Texture>();
        private RuntimeMemoryInformationWindow<Mesh> m_RuntimeMemoryMeshInformationWindow = new RuntimeMemoryInformationWindow<Mesh>();
        private RuntimeMemoryInformationWindow<Material> m_RuntimeMemoryMaterialInformationWindow = new RuntimeMemoryInformationWindow<Material>();
        private RuntimeMemoryInformationWindow<Shader> m_RuntimeMemoryShaderInformationWindow = new RuntimeMemoryInformationWindow<Shader>();
        private RuntimeMemoryInformationWindow<AnimationClip> m_RuntimeMemoryAnimationClipInformationWindow = new RuntimeMemoryInformationWindow<AnimationClip>();
        private RuntimeMemoryInformationWindow<AudioClip> m_RuntimeMemoryAudioClipInformationWindow = new RuntimeMemoryInformationWindow<AudioClip>();
        private RuntimeMemoryInformationWindow<Font> m_RuntimeMemoryFontInformationWindow = new RuntimeMemoryInformationWindow<Font>();
        private RuntimeMemoryInformationWindow<TextAsset> m_RuntimeMemoryTextAssetInformationWindow = new RuntimeMemoryInformationWindow<TextAsset>();
        private RuntimeMemoryInformationWindow<ScriptableObject> m_RuntimeMemoryScriptableObjectInformationWindow = new RuntimeMemoryInformationWindow<ScriptableObject>();
        private ObjectPoolInformationWindow m_ObjectPoolInformationWindow = new ObjectPoolInformationWindow();
        private ReferencePoolInformationWindow m_ReferencePoolInformationWindow = new ReferencePoolInformationWindow();
        private AudioInformationWindow m_AudioInformationWindow = new AudioInformationWindow();
        private TimerInformationWindow m_TimerInformationWindow = new TimerInformationWindow();
        private SettingsWindow m_SettingsWindow = new SettingsWindow();
        private FpsCounter m_FpsCounter;

        private PanelSettings _panelSettings;
        private UIDocument _uiDocument;
        private EventSystem _createdEventSystem;
        private VisualElement _root;
        private VisualElement _overlay;
        private VisualElement _window;
        private VisualElement _sidebar;
        private ScrollView _sidebarScrollView;
        private VisualElement _contentHost;
        private Label _headerTitle;
        private VisualElement _toggleButton;
        private Label _toggleFpsLabel;
        private Button _closeButton;
        private VisualElement _resizeHandle;
        private IDebuggerWindow _activeWindow;
        private DebuggerMenuNode _activeNode;
        private VisualElement _activeSidebarElement;

        private Vector2 _dragPointerStart;
        private Vector2 _dragWindowStart;
        private bool _isResizeActive;
        private Vector2 _resizePointerStart;
        private Vector2 _resizeWindowSizeStart;
        private float _toggleOpenSuppressUntil;
        private bool _isToggleDragging;
        private bool _isToggleSnapAnimating;
        private Vector2 _toggleSnapTargetPosition;
        private Vector2 _toggleSnapVelocity;
        private float _lastToggleTapTime = -1f;
        private Vector2 _lastToggleTapPosition;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        public static DebuggerComponent Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<DebuggerComponent>();
                }

                return _instance;
            }
        }

        public bool ActiveWindow
        {
            get => _mDebuggerService != null && _mDebuggerService.ActiveWindow;
            set
            {
                if (_mDebuggerService == null)
                {
                    return;
                }

                _mDebuggerService.ActiveWindow = value;
                enabled = value;
                SyncWindowVisibility();
            }
        }

        public bool ShowFullWindow
        {
            get => m_ShowFullWindow;
            set
            {
                if (m_ShowFullWindow == value)
                {
                    return;
                }

                m_ShowFullWindow = value;
                ApplyWindowScale();
                RebuildRuntimeVisualTree();
                SyncWindowVisibility();
            }
        }

        public float WindowOpacity
        {
            get => m_WindowOpacity;
            set
            {
                float clampedValue = Mathf.Clamp(value, 0.2f, 1f);
                if (Mathf.Approximately(m_WindowOpacity, clampedValue))
                {
                    return;
                }

                m_WindowOpacity = clampedValue;
                ApplyWindowOpacity();
            }
        }

        public bool EnableFloatingToggleSnap
        {
            get => m_EnableFloatingToggleSnap;
            set
            {
                if (m_EnableFloatingToggleSnap == value)
                {
                    return;
                }

                m_EnableFloatingToggleSnap = value;
                if (_isToggleDragging)
                {
                    return;
                }

                if (m_EnableFloatingToggleSnap)
                {
                    SnapIconToEdge();
                    return;
                }

                _isToggleSnapAnimating = false;
                _toggleSnapVelocity = Vector2.zero;
                ApplyToggleRect();
                ApplyToggleVisualOffset(Vector2.zero);
            }
        }

        public Rect IconRect
        {
            get => m_IconRect;
            set
            {
                m_IconRect = ClampIconRect(value);
                ApplyToggleRect();
            }
        }

        public Rect WindowRect
        {
            get => m_WindowRect;
            set
            {
                m_WindowRect = ClampWindowRect(value);
                ApplyWindowRect();
            }
        }

        public float WindowScale
        {
            get => m_WindowScale;
            set
            {
                float clampedValue = Mathf.Clamp(value, MinWindowScale, MaxWindowScale);
                if (Mathf.Approximately(m_WindowScale, clampedValue))
                {
                    return;
                }

                m_WindowScale = clampedValue;
                ApplyWindowScale();
                RebuildRuntimeVisualTree();
            }
        }

        private void Awake()
        {
            _instance = this;

            if (!AppServices.TryGetApp<IDebuggerService>(out _mDebuggerService))
            {
            _mDebuggerService = AppServices.RegisterApp<IDebuggerService>(new DebuggerService());
            }

            if (_mDebuggerService == null)
            {
                Log.Error("Debugger service is invalid.");
                enabled = false;
                return;
            }

            m_FpsCounter = new FpsCounter(0.5f);
            EnsureRuntimePanel();
        }

        private void OnValidate()
        {
            m_WindowOpacity = Mathf.Clamp(m_WindowOpacity, 0.2f, 1f);
            m_WindowScale = Mathf.Clamp(m_WindowScale, MinWindowScale, MaxWindowScale);

            if (!Application.isPlaying)
            {
                return;
            }

            m_IconRect = ClampIconRect(m_IconRect);
            m_WindowRect = ClampWindowRect(m_WindowRect);
            ApplyWindowRect();
            ApplyWindowOpacity();

            if (m_EnableFloatingToggleSnap && !_isToggleDragging)
            {
                SnapIconToEdge();
            }
            else
            {
                ApplyToggleRect();
                ApplyToggleVisualOffset(Vector2.zero);
            }
        }

        internal void SetActiveMode(DebuggerActiveWindowType activeWindowType)
        {
            m_ActiveWindow = activeWindowType;
        }

        private void Start()
        {
            RegisterBuiltInWindows();
            ApplyActiveMode();
            m_ShowFullWindow = false;
            SyncWindowVisibility();
        }

        private void Update()
        {
            if (m_FpsCounter != null)
            {
                m_FpsCounter.Update(Time.deltaTime, Time.unscaledDeltaTime);
            }

            UpdateToggleState();
            UpdateToggleSnapAnimation(Time.unscaledDeltaTime);
            HandleScreenSizeChanged();

            if (_mDebuggerService == null || !_mDebuggerService.ActiveWindow)
            {
                return;
            }

            if (_activeWindow != null)
            {
                _activeWindow.OnUpdate(Time.deltaTime, Time.unscaledDeltaTime);
            }
        }

        private void OnEnable()
        {
            SyncWindowVisibility();
        }

        private void OnDisable()
        {
            SyncWindowVisibility();
        }

        private void OnDestroy()
        {
            if (_activeWindow != null)
            {
                _activeWindow.OnLeave();
                _activeWindow = null;
            }

            for (int i = _registeredWindows.Count - 1; i >= 0; i--)
            {
                _registeredWindows[i].Shutdown();
            }

            _registeredWindows.Clear();
            _viewByWindow.Clear();
            _nodeByWindow.Clear();
            _sidebarRowStates.Clear();
            _sidebarChildContainers.Clear();
            _menuRoots.Clear();

            if (_uiDocument != null)
            {
                Destroy(_uiDocument);
                _uiDocument = null;
            }

            if (_panelSettings != null)
            {
                Destroy(_panelSettings);
                _panelSettings = null;
            }

            if (_createdEventSystem != null)
            {
                Destroy(_createdEventSystem.gameObject);
                _createdEventSystem = null;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        public void RegisterDebuggerWindow(string path, IDebuggerWindow debuggerWindow, params object[] args)
        {
            if (_mDebuggerService == null)
            {
                throw new GameFrameworkException("Debugger service is invalid.");
            }

            _mDebuggerService.RegisterDebuggerWindow(path, debuggerWindow, args);
            _registeredWindows.Add(debuggerWindow);
            RegisterMenuPath(path, debuggerWindow);
            RebuildSidebar();
        }

        public bool UnregisterDebuggerWindow(string path)
        {
            IDebuggerWindow window = GetDebuggerWindow(path);
            bool result = _mDebuggerService != null && _mDebuggerService.UnregisterDebuggerWindow(path);
            if (!result || window == null)
            {
                return false;
            }

            _registeredWindows.Remove(window);
            _viewByWindow.Remove(window);
            RemoveMenuWindow(window);
            if (ReferenceEquals(_activeWindow, window))
            {
                _activeWindow = null;
                _activeNode = null;
                _contentHost?.Clear();
            }

            RebuildSidebar();
            return true;
        }

        public IDebuggerWindow GetDebuggerWindow(string path)
        {
            return _mDebuggerService?.GetDebuggerWindow(path);
        }

        public bool SelectDebuggerWindow(string path)
        {
            IDebuggerWindow window = GetDebuggerWindow(path);
            if (window == null)
            {
                return false;
            }

            if (!_mDebuggerService.SelectDebuggerWindow(path))
            {
                return false;
            }

            return SelectWindow(window);
        }

        public void ResetLayout()
        {
            IconRect = DefaultIconRect;
            if (m_EnableFloatingToggleSnap)
            {
                SnapIconToEdge();
            }
            WindowRect = DefaultWindowRect;
            WindowScale = DefaultWindowScale;
        }

        public void GetRecentLogs(List<LogNode> results)
        {
            m_ConsoleWindow.GetRecentLogs(results);
        }

        public void GetRecentLogs(List<LogNode> results, int count)
        {
            m_ConsoleWindow.GetRecentLogs(results, count);
        }

        private void RegisterBuiltInWindows()
        {
            RegisterDebuggerWindow("Console", m_ConsoleWindow);
            RegisterDebuggerWindow("Information/System", m_SystemInformationWindow);
            RegisterDebuggerWindow("Information/Environment", m_EnvironmentInformationWindow);
            RegisterDebuggerWindow("Information/Screen", m_ScreenInformationWindow);
            RegisterDebuggerWindow("Information/Graphics", m_GraphicsInformationWindow);
            RegisterDebuggerWindow("Information/Input/Summary", m_InputSummaryInformationWindow);
            RegisterDebuggerWindow("Information/Input/Touch", m_InputTouchInformationWindow);
            RegisterDebuggerWindow("Information/Input/Location", m_InputLocationInformationWindow);
            RegisterDebuggerWindow("Information/Input/Acceleration", m_InputAccelerationInformationWindow);
            RegisterDebuggerWindow("Information/Input/Gyroscope", m_InputGyroscopeInformationWindow);
            RegisterDebuggerWindow("Information/Input/Compass", m_InputCompassInformationWindow);
            RegisterDebuggerWindow("Information/Other/Scene", m_SceneInformationWindow);
            RegisterDebuggerWindow("Information/Other/Path", m_PathInformationWindow);
            RegisterDebuggerWindow("Information/Other/Time", m_TimeInformationWindow);
            RegisterDebuggerWindow("Information/Other/Quality", m_QualityInformationWindow);
            RegisterDebuggerWindow("Information/Other/Web Player", m_WebPlayerInformationWindow);
            RegisterDebuggerWindow("Profiler/Summary", m_ProfilerInformationWindow);
            RegisterDebuggerWindow("Profiler/Memory/Summary", m_RuntimeMemorySummaryWindow);
            RegisterDebuggerWindow("Profiler/Memory/All", m_RuntimeMemoryAllInformationWindow);
            RegisterDebuggerWindow("Profiler/Memory/Texture", m_RuntimeMemoryTextureInformationWindow);
            RegisterDebuggerWindow("Profiler/Memory/Mesh", m_RuntimeMemoryMeshInformationWindow);
            RegisterDebuggerWindow("Profiler/Memory/Material", m_RuntimeMemoryMaterialInformationWindow);
            RegisterDebuggerWindow("Profiler/Memory/Shader", m_RuntimeMemoryShaderInformationWindow);
            RegisterDebuggerWindow("Profiler/Memory/AnimationClip", m_RuntimeMemoryAnimationClipInformationWindow);
            RegisterDebuggerWindow("Profiler/Memory/AudioClip", m_RuntimeMemoryAudioClipInformationWindow);
            RegisterDebuggerWindow("Profiler/Memory/Font", m_RuntimeMemoryFontInformationWindow);
            RegisterDebuggerWindow("Profiler/Memory/TextAsset", m_RuntimeMemoryTextAssetInformationWindow);
            RegisterDebuggerWindow("Profiler/Memory/ScriptableObject", m_RuntimeMemoryScriptableObjectInformationWindow);
            RegisterDebuggerWindow("Profiler/Object Pool", m_ObjectPoolInformationWindow);
            RegisterDebuggerWindow("Profiler/Reference Pool", m_ReferencePoolInformationWindow);
            RegisterDebuggerWindow("Profiler/Audio", m_AudioInformationWindow);
            RegisterDebuggerWindow("Profiler/Timer", m_TimerInformationWindow);
            RegisterDebuggerWindow("Other/Settings", m_SettingsWindow);
        }

        private void ApplyActiveMode()
        {
            switch (m_ActiveWindow)
            {
                case DebuggerActiveWindowType.AlwaysOpen:
                    ActiveWindow = true;
                    break;
                case DebuggerActiveWindowType.OnlyOpenWhenDevelopment:
                    ActiveWindow = Debug.isDebugBuild;
                    break;
                case DebuggerActiveWindowType.OnlyOpenInEditor:
                    ActiveWindow = Application.isEditor;
                    break;
                default:
                    ActiveWindow = false;
                    break;
            }
        }

        private void EnsureRuntimePanel()
        {
            EnsureEventSystem();

            _panelSettings = CreateRuntimePanelSettings();
            ApplyRuntimePanelSettings(_panelSettings);

            _uiDocument = gameObject.GetOrAddComponent<UIDocument>();

            _uiDocument.panelSettings = _panelSettings;
            _uiDocument.sortingOrder = DefaultPanelSortingOrder;
            BuildRootVisualTree();
        }

        private PanelSettings CreateRuntimePanelSettings()
        {
            PanelSettings source = m_PanelSettings != null
                ? m_PanelSettings
                : Resources.Load<PanelSettings>(DefaultPanelSettingsResourcePath);

            if (source != null)
            {
                PanelSettings instance = Instantiate(source);
                instance.name = SettingsPanelName;
                return instance;
            }

            PanelSettings fallback = ScriptableObject.CreateInstance<PanelSettings>();
            fallback.name = SettingsPanelName;
            InitializePanelSettingsDefaults(fallback);
            return fallback;
        }

        private static void ApplyRuntimePanelSettings(PanelSettings panelSettings)
        {
            if (panelSettings == null)
            {
                return;
            }

            panelSettings.sortingOrder = DefaultPanelSortingOrder;
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.scale = 1f;
            panelSettings.referenceDpi = 96f;
            panelSettings.fallbackDpi = 96f;
            panelSettings.clearColor = false;
            panelSettings.targetTexture = null;
        }

        private void EnsureEventSystem()
        {
            EventSystem existing = FindObjectOfType<EventSystem>();
            if (existing != null)
            {
                return;
            }

            GameObject eventSystemObject = new GameObject(SettingsEventSystemName);
            _createdEventSystem = eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        private void BuildRootVisualTree()
        {
            _root = _uiDocument.rootVisualElement;
            _root.Clear();
            _root.name = DebuggerRootName;
            _root.style.position = Position.Absolute;
            _root.style.left = 0f;
            _root.style.top = 0f;
            _root.style.right = 0f;
            _root.style.bottom = 0f;
            _root.pickingMode = PickingMode.Ignore;

            _toggleButton = BuildToggleButton();
            _overlay = BuildWindowOverlay();

            _root.Add(_toggleButton);
            _root.Add(_overlay);

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            ApplyToggleRect();
            ApplyWindowRect();
            ApplyWindowScale();
            ApplyWindowOpacity();
            UpdateToggleState();
        }

        private static void InitializePanelSettingsDefaults(PanelSettings panelSettings)
        {
            if (panelSettings == null)
            {
                return;
            }

            MethodInfo resetMethod = typeof(PanelSettings).GetMethod("Reset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            resetMethod?.Invoke(panelSettings, null);
        }

        private void RebuildRuntimeVisualTree()
        {
            if (_uiDocument == null)
            {
                return;
            }

            _viewByWindow.Clear();
            BuildRootVisualTree();
            UpdateHeaderTitle();
            RebuildSidebar();
            RefreshActiveWindowView();
            SyncWindowVisibility();
        }

        private void RefreshActiveWindowView()
        {
            if (_contentHost == null)
            {
                return;
            }

            _contentHost.Clear();
            if (_activeWindow == null)
            {
                return;
            }

            VisualElement view = _activeWindow.CreateView();
            if (view != null)
            {
                _viewByWindow[_activeWindow] = view;
                _contentHost.Add(view);
            }
        }

        private VisualElement BuildToggleButton()
        {
            float scale = GetUiScale();
            VisualElement button = new VisualElement
            {
                name = "debugger-toggle-button"
            };
            button.style.position = Position.Absolute;
            button.style.width = m_IconRect.width;
            button.style.height = m_IconRect.height;
            button.style.paddingLeft = 12f * scale;
            button.style.paddingRight = 12f * scale;
            button.style.paddingTop = 8f * scale;
            button.style.paddingBottom = 8f * scale;
            button.style.flexDirection = FlexDirection.Column;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
            button.style.translate = new Translate(0f, 0f);
            button.style.backgroundColor = DebuggerTheme.Background;
            button.style.borderTopLeftRadius = 0f;
            button.style.borderTopRightRadius = 0f;
            button.style.borderBottomLeftRadius = 0f;
            button.style.borderBottomRightRadius = 0f;
            button.style.borderTopWidth = 1f;
            button.style.borderRightWidth = 1f;
            button.style.borderBottomWidth = 1f;
            button.style.borderLeftWidth = 1f;
            button.style.borderTopColor = DebuggerTheme.Border;
            button.style.borderRightColor = DebuggerTheme.Border;
            button.style.borderBottomColor = DebuggerTheme.Border;
            button.style.borderLeftColor = DebuggerTheme.Border;
            button.pickingMode = PickingMode.Position;
            button.usageHints = UsageHints.DynamicTransform;

            _toggleFpsLabel = new Label();
            _toggleFpsLabel.style.fontSize = 22f * scale;
            _toggleFpsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _toggleFpsLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _toggleFpsLabel.style.color = DebuggerTheme.PrimaryText;
            _toggleFpsLabel.style.letterSpacing = 0.2f;
            _toggleFpsLabel.pickingMode = PickingMode.Ignore;

            button.Add(_toggleFpsLabel);
            ScrollableDebuggerWindowBase.ApplyButtonStateStyles(
                button,
                DebuggerTheme.Background,
                DebuggerTheme.PanelSurfaceAlt,
                DebuggerTheme.PanelSurfaceStrong,
                DebuggerTheme.PrimaryText,
                DebuggerTheme.PrimaryText,
                DebuggerTheme.PrimaryText);
            RegisterFloatingToggleManipulator(button);
            return button;
        }

        private void OpenWindow()
        {
            if (_activeWindow == null)
            {
                SelectDebuggerWindow(DefaultWindowPath);
                return;
            }

            ShowFullWindow = true;
        }

        private VisualElement BuildWindowOverlay()
        {
            float scale = GetUiScale();
            VisualElement overlay = new VisualElement
            {
                name = "debugger-overlay"
            };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0f;
            overlay.style.top = 0f;
            overlay.style.right = 0f;
            overlay.style.bottom = 0f;
            overlay.style.backgroundColor = Color.clear;
            overlay.pickingMode = PickingMode.Ignore;

            _window = new VisualElement
            {
                name = "debugger-window"
            };
            _window.style.position = Position.Absolute;
            _window.style.width = m_WindowRect.width;
            _window.style.height = m_WindowRect.height;
            _window.style.backgroundColor = DebuggerTheme.Background;
            _window.style.borderTopLeftRadius = 0f;
            _window.style.borderTopRightRadius = 0f;
            _window.style.borderBottomLeftRadius = 0f;
            _window.style.borderBottomRightRadius = 0f;
            _window.style.borderTopWidth = 1f;
            _window.style.borderRightWidth = 1f;
            _window.style.borderBottomWidth = 1f;
            _window.style.borderLeftWidth = 1f;
            _window.style.borderTopColor = DebuggerTheme.Border;
            _window.style.borderRightColor = DebuggerTheme.Border;
            _window.style.borderBottomColor = DebuggerTheme.Border;
            _window.style.borderLeftColor = DebuggerTheme.Border;
            _window.style.flexDirection = FlexDirection.Column;
            _window.pickingMode = PickingMode.Position;
            _window.usageHints = UsageHints.DynamicTransform;
            _window.style.minWidth = MinWindowWidth;
            _window.style.minHeight = MinWindowHeight;

            VisualElement header = new VisualElement();
            header.style.height = 54f * scale;
            header.style.minHeight = 54f * scale;
            header.style.paddingLeft = 16f * scale;
            header.style.paddingRight = 10f * scale;
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.backgroundColor = DebuggerTheme.PanelSurfaceAlt;
            header.style.borderBottomWidth = 1f;
            header.style.borderBottomColor = DebuggerTheme.Border;

            VisualElement headerLeft = new VisualElement();
            headerLeft.style.flexGrow = 1f;
            headerLeft.style.minWidth = 0f;

            _headerTitle = new Label(RootWindowTitle);
            _headerTitle.style.color = DebuggerTheme.PrimaryText;
            _headerTitle.style.fontSize = 22f * scale;
            _headerTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerLeft.Add(_headerTitle);

            VisualElement headerActions = new VisualElement();
            headerActions.style.flexDirection = FlexDirection.Row;
            _closeButton = CreateIconChromeButton("-", "Close", CloseToFloatingEntry, DebuggerTheme.Danger);
            headerActions.Add(_closeButton);

            header.Add(headerLeft);
            header.Add(headerActions);

            VisualElement body = new VisualElement();
            body.style.flexGrow = 1f;
            body.style.flexDirection = FlexDirection.Row;
            body.style.minWidth = 0f;
            body.style.minHeight = 0f;

            _sidebar = new VisualElement();
            _sidebar.style.width = 266f * scale;
            _sidebar.style.minWidth = 246f * scale;
            _sidebar.style.backgroundColor = DebuggerTheme.SidebarBackground;
            _sidebar.style.borderRightWidth = 1f;
            _sidebar.style.borderRightColor = DebuggerTheme.Border;
            _sidebar.style.flexShrink = 0f;
            _sidebar.style.flexDirection = FlexDirection.Column;
            _sidebar.style.minHeight = 0f;

            _sidebarScrollView = new ScrollView(ScrollViewMode.Vertical);
            _sidebarScrollView.style.flexGrow = 1f;
            _sidebarScrollView.style.minHeight = 0f;
            _sidebarScrollView.style.paddingLeft = 10f * scale;
            _sidebarScrollView.style.paddingRight = 8f * scale;
            _sidebarScrollView.style.paddingTop = 12f * scale;
            _sidebarScrollView.style.paddingBottom = 12f * scale;
            _sidebarScrollView.contentContainer.style.flexDirection = FlexDirection.Column;
            _sidebarScrollView.mouseWheelScrollSize = 240f * scale;
            ScrollableDebuggerWindowBase.StyleScrollView(_sidebarScrollView, scale);
            _sidebar.Add(_sidebarScrollView);

            _contentHost = new VisualElement();
            _contentHost.style.flexGrow = 1f;
            _contentHost.style.flexShrink = 1f;
            _contentHost.style.flexBasis = 0f;
            _contentHost.style.minWidth = 0f;
            _contentHost.style.minHeight = 0f;
            _contentHost.style.backgroundColor = DebuggerTheme.OverlayBackground;
            _contentHost.pickingMode = PickingMode.Position;

            body.Add(_sidebar);
            body.Add(_contentHost);

            _resizeHandle = new VisualElement();
            _resizeHandle.style.position = Position.Absolute;
            _resizeHandle.style.right = 0f;
            _resizeHandle.style.bottom = 0f;
            _resizeHandle.style.width = 22f * scale;
            _resizeHandle.style.height = 22f * scale;
            _resizeHandle.style.backgroundColor = DebuggerTheme.PanelSurfaceAlt;
            _resizeHandle.style.borderTopLeftRadius = 0f;
            _resizeHandle.style.borderBottomRightRadius = 0f;
            _resizeHandle.style.borderTopWidth = 1f;
            _resizeHandle.style.borderLeftWidth = 1f;
            _resizeHandle.style.borderTopColor = DebuggerTheme.Border;
            _resizeHandle.style.borderLeftColor = DebuggerTheme.Border;
            _resizeHandle.pickingMode = PickingMode.Position;
            _resizeHandle.style.justifyContent = Justify.Center;
            _resizeHandle.style.alignItems = Align.Center;

            Label resizeGlyph = new Label("//");
            resizeGlyph.style.color = DebuggerTheme.SecondaryText;
            resizeGlyph.style.fontSize = 10f * scale;
            resizeGlyph.style.unityFontStyleAndWeight = FontStyle.Bold;
            resizeGlyph.style.rotate = new Rotate(45f);
            resizeGlyph.pickingMode = PickingMode.Ignore;
            _resizeHandle.Add(resizeGlyph);
            ScrollableDebuggerWindowBase.ApplyButtonStateStyles(
                _resizeHandle,
                DebuggerTheme.PanelSurfaceAlt,
                DebuggerTheme.ButtonSurfaceHover,
                DebuggerTheme.ButtonSurfacePressed,
                DebuggerTheme.SecondaryText,
                DebuggerTheme.PrimaryText,
                DebuggerTheme.PrimaryText);
            RegisterResizeManipulator(_resizeHandle);

            _window.Add(header);
            _window.Add(body);
            _window.Add(_resizeHandle);
            overlay.Add(_window);

            RegisterMoveManipulator(header, true);
            return overlay;
        }

        private Button CreateChromeButton(string text, Action onClick, Color? background = null)
        {
            float scale = GetUiScale();
            Button button = new Button(onClick)
            {
                text = text
            };
            button.style.height = 34f * scale;
            button.style.minWidth = 94f * scale;
            button.style.paddingLeft = 12f * scale;
            button.style.paddingRight = 12f * scale;
            button.style.backgroundColor = background ?? DebuggerTheme.ButtonSurface;
            button.style.color = DebuggerTheme.PrimaryText;
            button.style.borderTopLeftRadius = 0f;
            button.style.borderTopRightRadius = 0f;
            button.style.borderBottomLeftRadius = 0f;
            button.style.borderBottomRightRadius = 0f;
            button.style.borderTopWidth = 0f;
            button.style.borderRightWidth = 0f;
            button.style.borderBottomWidth = 0f;
            button.style.borderLeftWidth = 0f;
            button.style.fontSize = 16f * scale;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            Color defaultBackground = background ?? DebuggerTheme.ButtonSurface;
            Color hoverBackground = ColorsEqual(defaultBackground, DebuggerTheme.ButtonSurface)
                ? DebuggerTheme.ButtonSurfaceHover
                : TintColor(defaultBackground, 0.12f);
            Color pressedBackground = ColorsEqual(defaultBackground, DebuggerTheme.ButtonSurface)
                ? DebuggerTheme.ButtonSurfacePressed
                : TintColor(defaultBackground, -0.12f);
            ScrollableDebuggerWindowBase.ApplyButtonStateStyles(
                button,
                defaultBackground,
                hoverBackground,
                pressedBackground,
                DebuggerTheme.PrimaryText,
                DebuggerTheme.PrimaryText,
                DebuggerTheme.PrimaryText);
            return button;
        }

        private Button CreateIconChromeButton(string icon, string tooltip, Action onClick, Color? background = null)
        {
            float scale = GetUiScale();
            Button button = CreateChromeButton(icon, onClick, background);
            button.tooltip = tooltip;
            button.style.minWidth = 38f * scale;
            button.style.width = 38f * scale;
            button.style.height = 38f * scale;
            button.style.paddingLeft = 0f;
            button.style.paddingRight = 0f;
            button.style.fontSize = 18f * scale;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.borderTopWidth = 1f;
            button.style.borderRightWidth = 1f;
            button.style.borderBottomWidth = 1f;
            button.style.borderLeftWidth = 1f;
            button.style.borderTopColor = DebuggerTheme.Border;
            button.style.borderRightColor = DebuggerTheme.Border;
            button.style.borderBottomColor = DebuggerTheme.Border;
            button.style.borderLeftColor = DebuggerTheme.Border;
            return button;
        }

        internal float GetUiScale()
        {
            return m_ShowFullWindow ? m_WindowScale : 1f;
        }

        private void RebuildSidebar()
        {
            if (_sidebarScrollView == null)
            {
                return;
            }

            _sidebarScrollView.contentContainer.Clear();
            _sidebarRowStates.Clear();
            _sidebarChildContainers.Clear();
            _activeSidebarElement = null;
            for (int i = 0; i < _menuRoots.Count; i++)
            {
                AddMenuNode(_menuRoots[i], 0);
            }

            ScrollSidebarToActive();
        }

        private void AddMenuNode(DebuggerMenuNode node, int depth)
        {
            AddMenuNode(node, depth, _sidebarScrollView.contentContainer);
        }

        private void AddMenuNode(DebuggerMenuNode node, int depth, VisualElement parent)
        {
            if (node == null)
            {
                return;
            }

            float scale = GetUiScale();
            bool hasChildren = node.Children.Count > 0;
            bool isActive = IsNodeActive(node);
            bool isExpanded = hasChildren && (node.Expanded || isActive);

            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 36f * scale;
            row.style.minHeight = 36f * scale;
            row.style.paddingLeft = 10f * scale + depth * SidebarIndentWidth * scale;
            row.style.paddingRight = 10f * scale;
            row.style.marginBottom = 0f;
            row.style.borderTopLeftRadius = 0f;
            row.style.borderTopRightRadius = 0f;
            row.style.borderBottomLeftRadius = 0f;
            row.style.borderBottomRightRadius = 0f;
            row.style.backgroundColor = isActive ? DebuggerTheme.SidebarRowSelected : DebuggerTheme.SidebarRow;
            row.style.borderLeftWidth = 3f * scale;
            row.style.borderLeftColor = isActive ? DebuggerTheme.Accent : Color.clear;
            row.style.borderTopWidth = 0f;
            row.style.borderRightWidth = 0f;
            row.style.borderBottomWidth = 0f;
            row.style.flexShrink = 0f;
            row.pickingMode = PickingMode.Position;

            Label titleLabel = new Label(node.DisplayName);
            titleLabel.style.flexGrow = 1f;
            titleLabel.style.minWidth = 0f;
            titleLabel.style.fontSize = 17f * scale;
            titleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            titleLabel.style.whiteSpace = WhiteSpace.NoWrap;
            titleLabel.style.color = isActive ? DebuggerTheme.PrimaryText : DebuggerTheme.SecondaryText;
            titleLabel.pickingMode = PickingMode.Ignore;
            row.Add(titleLabel);

            Button expander = null;

            if (hasChildren)
            {
                expander = ScrollableDebuggerWindowBase.CreateGhostButton(isExpanded ? "v" : ">");
                expander.style.width = 18f * scale;
                expander.style.minWidth = 18f * scale;
                expander.style.height = 28f * scale;
                expander.style.minHeight = 28f * scale;
                expander.style.paddingLeft = 0f;
                expander.style.paddingRight = 0f;
                expander.style.marginLeft = 6f * scale;
                expander.style.fontSize = 14f * scale;
                expander.style.color = isActive ? DebuggerTheme.PrimaryText : DebuggerTheme.SecondaryText;
                expander.style.unityTextAlign = TextAnchor.MiddleCenter;
                expander.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                expander.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
                row.Add(expander);
            }

            row.AddManipulator(new Clickable(() => SelectWindowNode(node)));
            SidebarRowState state = new SidebarRowState(row, titleLabel, expander, scale, isActive);
            _sidebarRowStates[node] = state;

            parent.Add(row);
            if (isActive)
            {
                _activeSidebarElement = row;
            }

            if (!hasChildren)
            {
                return;
            }

            VisualElement childContainer = new VisualElement();
            childContainer.style.flexDirection = FlexDirection.Column;
            childContainer.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            _sidebarChildContainers[node] = childContainer;
            parent.Add(childContainer);

            expander.clicked += () =>
            {
                node.Expanded = !node.Expanded;
                bool nowExpanded = node.Expanded;
                childContainer.style.display = nowExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                expander.text = nowExpanded ? "v" : ">";
            };

            for (int i = 0; i < node.Children.Count; i++)
            {
                AddMenuNode(node.Children[i], depth + 1, childContainer);
            }
        }

        private void RefreshSidebarSelectionState(bool scrollToActive)
        {
            if (_sidebarRowStates.Count <= 0)
            {
                return;
            }

            _activeSidebarElement = null;
            foreach (KeyValuePair<DebuggerMenuNode, SidebarRowState> pair in _sidebarRowStates)
            {
                bool isActive = IsNodeActive(pair.Key);
                pair.Value.SetActive(isActive);
                if (isActive)
                {
                    _activeSidebarElement = pair.Value.Row;
                }
            }

            if (scrollToActive)
            {
                ScrollSidebarToActive();
            }
        }

        private void ScrollSidebarToActive()
        {
            if (_activeSidebarElement == null || _sidebarScrollView == null)
            {
                return;
            }

            _activeSidebarElement.schedule.Execute(() => _sidebarScrollView.ScrollTo(_activeSidebarElement)).ExecuteLater(0);
        }

        private bool IsNodeActive(DebuggerMenuNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (_activeNode == node)
            {
                return true;
            }

            return _activeNode != null && _activeNode.Path.StartsWith(node.Path + "/", StringComparison.Ordinal);
        }

        private bool SelectWindowNode(DebuggerMenuNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (node.Window == null)
            {
                node.Expanded = !node.Expanded;
                if (_sidebarChildContainers.TryGetValue(node, out VisualElement container))
                {
                    container.style.display = node.Expanded ? DisplayStyle.Flex : DisplayStyle.None;
                    if (_sidebarRowStates.TryGetValue(node, out SidebarRowState state) && state.Expander != null)
                    {
                        state.Expander.text = node.Expanded ? "v" : ">";
                    }
                }
                return true;
            }

            return SelectWindow(node.Window);
        }

        private bool SelectWindow(IDebuggerWindow window)
        {
            if (window == null)
            {
                return false;
            }

            if (ReferenceEquals(_activeWindow, window))
            {
                ShowFullWindow = true;
                return true;
            }

            if (_activeWindow != null)
            {
                _activeWindow.OnLeave();
            }

            _activeWindow = window;
            _activeNode = _nodeByWindow.TryGetValue(window, out DebuggerMenuNode node) ? node : null;
            ExpandParents(_activeNode);
            _activeWindow.OnEnter();
            UpdateHeaderTitle();

            if (!m_ShowFullWindow)
            {
                ShowFullWindow = true;
                return true;
            }

            _contentHost.Clear();
            if (!_viewByWindow.TryGetValue(window, out VisualElement view) || view == null)
            {
                view = window.CreateView();
                _viewByWindow[window] = view;
            }

            if (view != null)
            {
                _contentHost.Add(view);
            }

            RefreshSidebarSelectionState(true);

            return true;
        }

        private void UpdateHeaderTitle()
        {
            if (_headerTitle == null)
            {
                return;
            }

            _headerTitle.text = _activeNode == null ? RootWindowTitle : _activeNode.Path;
        }

        private bool ExpandParents(DebuggerMenuNode node)
        {
            bool changed = false;
            while (node != null)
            {
                if (!node.Expanded)
                {
                    node.Expanded = true;
                    changed = true;
                    if (_sidebarChildContainers.TryGetValue(node, out VisualElement container))
                    {
                        container.style.display = DisplayStyle.Flex;
                    }
                    if (_sidebarRowStates.TryGetValue(node, out SidebarRowState state) && state.Expander != null)
                    {
                        state.Expander.text = "v";
                    }
                }

                node = node.Parent;
            }

            return changed;
        }

        private void UpdateToggleState()
        {
            if (_toggleFpsLabel == null || m_FpsCounter == null)
            {
                return;
            }

            m_ConsoleWindow.RefreshCount();
            Color color = m_ConsoleWindow.FatalCount > 0
                ? m_ConsoleWindow.GetLogStringColor(LogType.Exception)
                : m_ConsoleWindow.ErrorCount > 0
                    ? m_ConsoleWindow.GetLogStringColor(LogType.Error)
                    : m_ConsoleWindow.WarningCount > 0
                        ? m_ConsoleWindow.GetLogStringColor(LogType.Warning)
                        : m_ConsoleWindow.GetLogStringColor(LogType.Log);

            _toggleFpsLabel.text = Utility.Text.Format("FPS:{0:F0}", m_FpsCounter.CurrentFps);
            _toggleFpsLabel.style.color = color;
        }

        private void SyncWindowVisibility()
        {
            bool active = isActiveAndEnabled && _mDebuggerService != null && _mDebuggerService.ActiveWindow;
            if (_toggleButton != null)
            {
                _toggleButton.style.display = active && !m_ShowFullWindow ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_overlay != null)
            {
                _overlay.style.display = active && m_ShowFullWindow ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void ApplyToggleRect()
        {
            if (_toggleButton == null)
            {
                return;
            }

            _toggleButton.style.left = m_IconRect.x;
            _toggleButton.style.top = m_IconRect.y;
            _toggleButton.style.width = m_IconRect.width;
            _toggleButton.style.height = m_IconRect.height;
        }

        private void ApplyToggleVisualOffset(Vector2 offset)
        {
            if (_toggleButton == null)
            {
                return;
            }

            _toggleButton.style.translate = new Translate(offset.x, offset.y);
        }

        private void ApplyWindowRect()
        {
            if (_window == null)
            {
                return;
            }

            _window.style.left = m_WindowRect.x;
            _window.style.top = m_WindowRect.y;
            _window.style.width = m_WindowRect.width;
            _window.style.height = m_WindowRect.height;
        }

        private void ApplyWindowScale()
        {
            if (_window == null || _panelSettings == null)
            {
                return;
            }

            _window.transform.scale = Vector3.one;
            _panelSettings.scale = 1f;
        }

        private void ApplyWindowOpacity()
        {
            if (_window != null)
            {
                _window.style.opacity = m_WindowOpacity;
            }

            if (_toggleButton != null)
            {
                _toggleButton.style.opacity = Mathf.Clamp(m_WindowOpacity + 0.1f, 0.35f, 1f);
            }
        }

        private void CloseToFloatingEntry()
        {
            ShowFullWindow = false;
        }

        private void RegisterMoveManipulator(VisualElement target, bool moveWindow)
        {
            target.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                target.CapturePointer(evt.pointerId);
                _dragPointerStart = new Vector2(evt.position.x, evt.position.y);
                _dragWindowStart = moveWindow ? new Vector2(m_WindowRect.x, m_WindowRect.y) : new Vector2(m_IconRect.x, m_IconRect.y);
                evt.StopPropagation();
            });

            target.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!target.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                Vector2 delta = new Vector2(evt.position.x, evt.position.y) - _dragPointerStart;
                if (moveWindow)
                {
                    WindowRect = new Rect(_dragWindowStart.x + delta.x, _dragWindowStart.y + delta.y, m_WindowRect.width, m_WindowRect.height);
                }
                else
                {
                    IconRect = new Rect(_dragWindowStart.x + delta.x, _dragWindowStart.y + delta.y, m_IconRect.width, m_IconRect.height);
                }

                evt.StopPropagation();
            });

            target.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!target.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                target.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            });
        }

        private void RegisterFloatingToggleManipulator(VisualElement target)
        {
            target.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                target.CapturePointer(evt.pointerId);
                _isToggleDragging = false;
                _isToggleSnapAnimating = false;
                _toggleSnapVelocity = Vector2.zero;
                _dragPointerStart = new Vector2(evt.position.x, evt.position.y);
                _dragWindowStart = new Vector2(m_IconRect.x, m_IconRect.y);
                ApplyToggleVisualOffset(Vector2.zero);

                evt.StopPropagation();
            });

            target.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!target.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                Vector2 currentPosition = new Vector2(evt.position.x, evt.position.y);
                Vector2 delta = currentPosition - _dragPointerStart;
                if (!_isToggleDragging && delta.sqrMagnitude > ToggleClickMoveThreshold * ToggleClickMoveThreshold)
                {
                    _isToggleDragging = true;
                }

                if (_isToggleDragging)
                {
                    Vector2 targetPosition = ClampIconPosition(new Vector2(_dragWindowStart.x + delta.x, _dragWindowStart.y + delta.y));
                    m_IconRect = new Rect(targetPosition.x, targetPosition.y, m_IconRect.width, m_IconRect.height);
                    ApplyToggleRect();
                    ApplyToggleVisualOffset(Vector2.zero);
                }

                evt.StopPropagation();
            });

            target.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!target.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                target.ReleasePointer(evt.pointerId);
                if (_isToggleDragging)
                {
                    if (m_EnableFloatingToggleSnap)
                    {
                        StartToggleSnapAnimation(GetSnappedIconPosition(new Vector2(m_IconRect.x, m_IconRect.y)));
                    }
                    else
                    {
                        _isToggleSnapAnimating = false;
                        _toggleSnapVelocity = Vector2.zero;
                        ApplyToggleRect();
                    }

                    _toggleOpenSuppressUntil = Time.unscaledTime + ToggleClickSuppressAfterDrag;
                }
                else if (Time.unscaledTime >= _toggleOpenSuppressUntil && IsToggleDoubleTap(evt.position))
                {
                    OpenWindow();
                }

                _isToggleDragging = false;
                evt.StopPropagation();
            });
        }

        private void SnapIconToEdge()
        {
            if (!m_EnableFloatingToggleSnap)
            {
                ApplyToggleRect();
                ApplyToggleVisualOffset(Vector2.zero);
                return;
            }

            Vector2 targetPosition = GetSnappedIconPosition(new Vector2(m_IconRect.x, m_IconRect.y));
            IconRect = new Rect(targetPosition.x, targetPosition.y, m_IconRect.width, m_IconRect.height);
            ApplyToggleVisualOffset(Vector2.zero);
        }

        private void RegisterResizeManipulator(VisualElement target)
        {
            target.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                target.CapturePointer(evt.pointerId);
                _isResizeActive = true;
                _resizePointerStart = new Vector2(evt.position.x, evt.position.y);
                _resizeWindowSizeStart = new Vector2(m_WindowRect.width, m_WindowRect.height);
                evt.StopPropagation();
            });

            target.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_isResizeActive || !target.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                Vector2 delta = (new Vector2(evt.position.x, evt.position.y) - _resizePointerStart) / Mathf.Max(0.001f, GetUiScale());
                WindowRect = new Rect(m_WindowRect.x, m_WindowRect.y, _resizeWindowSizeStart.x + delta.x, _resizeWindowSizeStart.y + delta.y);
                evt.StopPropagation();
            });

            target.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!target.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                target.ReleasePointer(evt.pointerId);
                _isResizeActive = false;
                evt.StopPropagation();
            });
        }

        private bool IsToggleDoubleTap(Vector2 pointerPosition)
        {
            float now = Time.unscaledTime;
            Vector2 currentPosition = new Vector2(pointerPosition.x, pointerPosition.y);
            bool isDoubleTap = now - _lastToggleTapTime <= ToggleDoubleClickInterval
                && (currentPosition - _lastToggleTapPosition).sqrMagnitude <= ToggleDoubleClickMaxDistance * ToggleDoubleClickMaxDistance;

            _lastToggleTapTime = now;
            _lastToggleTapPosition = currentPosition;
            return isDoubleTap;
        }

        private void StartToggleSnapAnimation(Vector2 targetPosition)
        {
            _toggleSnapTargetPosition = ClampIconPosition(targetPosition);
            _isToggleSnapAnimating = true;
            _toggleSnapVelocity = Vector2.zero;
        }

        private void UpdateToggleSnapAnimation(float deltaTime)
        {
            if (!m_EnableFloatingToggleSnap)
            {
                _isToggleSnapAnimating = false;
                _toggleSnapVelocity = Vector2.zero;
                return;
            }

            if (!_isToggleSnapAnimating || _isToggleDragging)
            {
                return;
            }

            Vector2 currentPosition = new Vector2(m_IconRect.x, m_IconRect.y);
            Vector2 nextPosition = Vector2.SmoothDamp(currentPosition, _toggleSnapTargetPosition, ref _toggleSnapVelocity, ToggleSnapSmoothTime, Mathf.Infinity, Mathf.Max(0.0001f, deltaTime));
            m_IconRect = new Rect(nextPosition.x, nextPosition.y, m_IconRect.width, m_IconRect.height);
            ApplyToggleRect();

            if ((nextPosition - _toggleSnapTargetPosition).sqrMagnitude <= ToggleSnapStopDistance * ToggleSnapStopDistance)
            {
                _isToggleSnapAnimating = false;
                m_IconRect = new Rect(_toggleSnapTargetPosition.x, _toggleSnapTargetPosition.y, m_IconRect.width, m_IconRect.height);
                ApplyToggleRect();
                ApplyToggleVisualOffset(Vector2.zero);
            }
        }

        private void HandleScreenSizeChanged()
        {
            if (_lastScreenWidth == Screen.width && _lastScreenHeight == Screen.height)
            {
                return;
            }

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            IconRect = new Rect(m_IconRect.x, m_IconRect.y, m_IconRect.width, m_IconRect.height);
            if (m_EnableFloatingToggleSnap && !_isToggleDragging)
            {
                SnapIconToEdge();
            }
            else
            {
                ApplyToggleRect();
                ApplyToggleVisualOffset(Vector2.zero);
            }

            WindowRect = new Rect(m_WindowRect.x, m_WindowRect.y, m_WindowRect.width, m_WindowRect.height);
        }

        private Vector2 ClampIconPosition(Vector2 position)
        {
            float maxX = Mathf.Max(0f, Screen.width - m_IconRect.width);
            float maxY = Mathf.Max(0f, Screen.height - m_IconRect.height);
            position.x = Mathf.Clamp(position.x, 0f, maxX);
            position.y = Mathf.Clamp(position.y, 0f, maxY);
            return position;
        }

        private Vector2 GetSnappedIconPosition(Vector2 position)
        {
            position = ClampIconPosition(position);

            float leftDistance = position.x;
            float rightDistance = Mathf.Max(0f, Screen.width - (position.x + m_IconRect.width));
            float topDistance = position.y;
            float bottomDistance = Mathf.Max(0f, Screen.height - (position.y + m_IconRect.height));

            float minDistance = leftDistance;
            Vector2 snappedPosition = new Vector2(0f, position.y);

            if (rightDistance < minDistance)
            {
                minDistance = rightDistance;
                snappedPosition = new Vector2(Mathf.Max(0f, Screen.width - m_IconRect.width), position.y);
            }

            if (topDistance < minDistance)
            {
                minDistance = topDistance;
                snappedPosition = new Vector2(position.x, 0f);
            }

            if (bottomDistance < minDistance)
            {
                snappedPosition = new Vector2(position.x, Mathf.Max(0f, Screen.height - m_IconRect.height));
            }

            return ClampIconPosition(snappedPosition);
        }

        private Rect ClampIconRect(Rect rect)
        {
            rect.width = DefaultIconRect.width;
            rect.height = DefaultIconRect.height;
            Vector2 clampedPosition = ClampIconPosition(new Vector2(rect.x, rect.y));
            rect.x = clampedPosition.x;
            rect.y = clampedPosition.y;
            return rect;
        }

        private Rect ClampWindowRect(Rect rect)
        {
            float uiScale = GetUiScale();
            float screenW = Screen.width;
            float screenH = Screen.height;
            float maxW = Mathf.Max(MinWindowWidth, screenW / Mathf.Max(0.001f, uiScale));
            float maxH = Mathf.Max(MinWindowHeight, screenH / Mathf.Max(0.001f, uiScale));
            rect.width = Mathf.Clamp(rect.width, MinWindowWidth, maxW);
            rect.height = Mathf.Clamp(rect.height, MinWindowHeight, maxH);

            float scaledWidth = rect.width * uiScale;
            float scaledHeight = rect.height * uiScale;
            const float edgeMargin = 60f;
            float minX = -(scaledWidth - edgeMargin);
            float minY = 0f;
            float maxX = screenW - edgeMargin;
            float maxY = screenH - edgeMargin;
            rect.x = Mathf.Clamp(rect.x, minX, maxX);
            rect.y = Mathf.Clamp(rect.y, minY, maxY);
            return rect;
        }

        private void RegisterMenuPath(string path, IDebuggerWindow window)
        {
            string[] segments = path.Split('/');
            List<DebuggerMenuNode> current = _menuRoots;
            DebuggerMenuNode parent = null;
            string currentPath = string.Empty;

            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                currentPath = string.IsNullOrEmpty(currentPath) ? segment : currentPath + "/" + segment;
                DebuggerMenuNode node = current.Find(item => item.DisplayName == segment);
                if (node == null)
                {
                    node = new DebuggerMenuNode(segment, currentPath, parent);
                    current.Add(node);
                    if (parent == null)
                    {
                        node.Expanded = true;
                    }
                }

                parent = node;
                current = node.Children;

                if (i == segments.Length - 1)
                {
                    node.Window = window;
                    _nodeByWindow[window] = node;
                }
            }

            _menuRoots.Sort(DebuggerMenuNodeComparer.Instance);
        }

        private void RemoveMenuWindow(IDebuggerWindow window)
        {
            if (!_nodeByWindow.TryGetValue(window, out DebuggerMenuNode node) || node == null)
            {
                return;
            }

            node.Window = null;
            _nodeByWindow.Remove(window);

            while (node != null)
            {
                if (node.Window != null || node.Children.Count > 0)
                {
                    break;
                }

                if (node.Parent == null)
                {
                    _menuRoots.Remove(node);
                    break;
                }

                node.Parent.Children.Remove(node);
                node = node.Parent;
            }
        }

        private static void CopyToClipboard(string content)
        {
            GUIUtility.systemCopyBuffer = content ?? string.Empty;
        }

        private static Color TintColor(Color color, float amount)
        {
            return new Color(
                Mathf.Clamp01(color.r + amount),
                Mathf.Clamp01(color.g + amount),
                Mathf.Clamp01(color.b + amount),
                color.a);
        }

        private static bool ColorsEqual(Color lhs, Color rhs)
        {
            return Mathf.Approximately(lhs.r, rhs.r)
                && Mathf.Approximately(lhs.g, rhs.g)
                && Mathf.Approximately(lhs.b, rhs.b)
                && Mathf.Approximately(lhs.a, rhs.a);
        }

        private sealed class SidebarRowState
        {
            private readonly Label _titleLabel;
            private readonly float _scale;
            private bool _isActive;
            private bool _isHovered;
            private bool _isPressed;

            public SidebarRowState(VisualElement row, Label titleLabel, Button expander, float scale, bool isActive)
            {
                Row = row;
                _titleLabel = titleLabel;
                Expander = expander;
                _scale = scale;
                _isActive = isActive;
                RegisterCallbacks();
                Apply();
            }

            public VisualElement Row { get; }

            public Button Expander { get; }

            public void SetActive(bool isActive)
            {
                if (_isActive == isActive)
                {
                    Apply();
                    return;
                }

                _isActive = isActive;
                Apply();
            }

            private void RegisterCallbacks()
            {
                Row.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    _isHovered = true;
                    Apply();
                });
                Row.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    _isHovered = false;
                    _isPressed = false;
                    Apply();
                });
                Row.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0)
                    {
                        return;
                    }

                    _isPressed = true;
                    Apply();
                });
                Row.RegisterCallback<PointerUpEvent>(_ =>
                {
                    _isPressed = false;
                    Apply();
                });
                Row.RegisterCallback<PointerCancelEvent>(_ =>
                {
                    _isPressed = false;
                    _isHovered = false;
                    Apply();
                });
            }

            private void Apply()
            {
                Row.style.backgroundColor = _isPressed
                    ? _isActive ? DebuggerTheme.SidebarRowSelectedPressed : DebuggerTheme.SidebarRowPressed
                    : _isHovered
                        ? _isActive ? DebuggerTheme.SidebarRowSelectedHover : DebuggerTheme.SidebarRowHover
                        : _isActive ? DebuggerTheme.SidebarRowSelected : DebuggerTheme.SidebarRow;
                Row.style.borderLeftWidth = 3f * _scale;
                Row.style.borderLeftColor = _isActive ? DebuggerTheme.Accent : Color.clear;

                Color textColor = _isActive || _isHovered ? DebuggerTheme.PrimaryText : DebuggerTheme.SecondaryText;
                _titleLabel.style.color = textColor;
                _titleLabel.style.unityFontStyleAndWeight = _isActive ? FontStyle.Bold : FontStyle.Normal;

                if (Expander != null)
                {
                    Expander.style.color = textColor;
                }
            }
        }

        private sealed class DebuggerMenuNode
        {
            public DebuggerMenuNode(string displayName, string path, DebuggerMenuNode parent)
            {
                DisplayName = displayName;
                Path = path;
                Parent = parent;
            }

            public string DisplayName { get; }

            public string Path { get; }

            public DebuggerMenuNode Parent { get; }

            public List<DebuggerMenuNode> Children { get; } = new List<DebuggerMenuNode>(8);

            public IDebuggerWindow Window { get; set; }

            public bool Expanded { get; set; }
        }

        private sealed class DebuggerMenuNodeComparer : IComparer<DebuggerMenuNode>
        {
            public static readonly DebuggerMenuNodeComparer Instance = new DebuggerMenuNodeComparer();

            public int Compare(DebuggerMenuNode x, DebuggerMenuNode y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x == null)
                {
                    return -1;
                }

                if (y == null)
                {
                    return 1;
                }

                return string.CompareOrdinal(x.DisplayName, y.DisplayName);
            }
        }
    }
}
