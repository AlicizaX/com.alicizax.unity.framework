using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// Runtime holder for a UI Toolkit window prefab. The prefab must contain a UIDocument
    /// with both PanelSettings and VisualTreeAsset assigned.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public abstract class UIToolkitHolderBase : UIHolderObjectBase
    {
        private const string InputBlockerName = "__UIFrameworkInputBlocker";

        [SerializeField] private UIDocument _document;

        private PanelSettings _runtimePanelSettings;
        private VisualElement _documentRoot;
        private VisualElement _root;
        private VisualElement _inputBlocker;
        private VisualElement _boundRoot;
        private bool _visible;
        private bool _interactable = true;
        private int _sortingOrder;

        public sealed override UIBackend Backend => UIBackend.UIToolkit;
        public UIDocument Document => _document;
        public VisualElement RootVisualElement => _root;
        public sealed override bool FrameworkVisible =>
            _visible && gameObject.activeInHierarchy && _root != null;
        public sealed override int FrameworkSortingOrder => _sortingOrder;

        public override void Awake()
        {
            base.Awake();
            EnsureBackendReady();
        }

        internal sealed override bool EnsureBackendReady()
        {
            _document ??= GetComponent<UIDocument>();
            if (_document == null)
            {
                Log.Error("[UI Toolkit] UIDocument is missing on {0}.", name);
                return false;
            }

            if (_document.visualTreeAsset == null)
            {
                Log.Error("[UI Toolkit] VisualTreeAsset is missing on {0}.", name);
                return false;
            }

            if (_document.panelSettings == null)
            {
                Log.Error("[UI Toolkit] PanelSettings is missing on {0}.", name);
                return false;
            }

            EnsureIndependentPanelSettings();
            VisualElement documentRoot = _document.rootVisualElement;
            if (documentRoot == null)
            {
                Log.Error("[UI Toolkit] RootVisualElement is unavailable on {0}.", name);
                return false;
            }

            if (!ReferenceEquals(_boundRoot, documentRoot))
            {
                _documentRoot = documentRoot;
                _boundRoot = documentRoot;
                _root = new VisualElement { name = "__UIFrameworkRoot" };
                _root.style.flexGrow = 1;
                while (documentRoot.childCount > 0)
                {
                    _root.Add(documentRoot[0]);
                }

                documentRoot.Add(_root);
                BindElements(_root);
                CreateInputBlocker();
            }

            ApplyVisibility();
            ApplyInteractable();
            ApplyDepth();
            return true;
        }

        protected abstract void BindElements(VisualElement root);

        /// <summary>Moves this holder's visual tree into another UI Toolkit container.</summary>
        internal void AttachTo(VisualElement parent)
        {
            if (parent == null || !EnsureBackendReady())
            {
                return;
            }

            if (!ReferenceEquals(_root.parent, parent))
            {
                _root.RemoveFromHierarchy();
                parent.Add(_root);
            }
        }

        protected T QueryRequired<T>(VisualElement root, string elementName) where T : VisualElement
        {
            T element = root?.Q<T>(elementName);
            if (element == null)
            {
                Log.Error("[UI Toolkit] Element '{0}' ({1}) was not found in {2}.", elementName, typeof(T).Name, name);
            }

            return element;
        }

        internal sealed override void SetFrameworkVisible(bool visible)
        {
            _visible = visible;
            ApplyVisibility();
        }

        internal sealed override void SetFrameworkInteractable(bool interactable)
        {
            _interactable = interactable;
            ApplyInteractable();
        }

        internal sealed override void SetFrameworkDepth(int depth)
        {
            _sortingOrder = depth;
            ApplyDepth();
        }

        internal sealed override void EnterFrameworkCache()
        {
            _visible = false;
            ApplyVisibility();
        }

        internal sealed override void ExitFrameworkCache()
        {
            ApplyVisibility();
        }

        private void EnsureIndependentPanelSettings()
        {
            if (!Application.isPlaying || _runtimePanelSettings != null)
            {
                return;
            }

            _runtimePanelSettings = Object.Instantiate(_document.panelSettings);
            _runtimePanelSettings.name = _document.panelSettings.name + " (" + name + ")";
            _document.panelSettings = _runtimePanelSettings;
        }

        private void CreateInputBlocker()
        {
            _inputBlocker = _root.Q<VisualElement>(InputBlockerName);
            if (_inputBlocker == null)
            {
                _inputBlocker = new VisualElement
                {
                    name = InputBlockerName,
                    pickingMode = PickingMode.Position,
                };
                _inputBlocker.style.position = Position.Absolute;
                _inputBlocker.style.left = 0;
                _inputBlocker.style.right = 0;
                _inputBlocker.style.top = 0;
                _inputBlocker.style.bottom = 0;
                _root.Add(_inputBlocker);
            }
        }

        private void ApplyVisibility()
        {
            if (_root != null)
            {
                _root.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void ApplyInteractable()
        {
            if (_inputBlocker == null)
            {
                return;
            }

            _inputBlocker.style.display = _interactable ? DisplayStyle.None : DisplayStyle.Flex;
            if (!_interactable)
            {
                _inputBlocker.BringToFront();
            }
        }

        private void ApplyDepth()
        {
            if (_runtimePanelSettings != null)
            {
                _runtimePanelSettings.sortingOrder = _sortingOrder;
            }
            else if (_document != null && _document.panelSettings != null)
            {
                _document.panelSettings.sortingOrder = _sortingOrder;
            }

            if (_document != null)
            {
                _document.sortingOrder = 0;
            }
        }

        protected override void OnDestroy()
        {
            _root?.RemoveFromHierarchy();
            _inputBlocker = null;
            _documentRoot = null;
            _boundRoot = null;
            _root = null;
            if (_runtimePanelSettings != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(_runtimePanelSettings);
                else
                    Object.DestroyImmediate(_runtimePanelSettings);
                _runtimePanelSettings = null;
            }

            base.OnDestroy();
        }
    }

    public abstract class UIToolkitWindow<T> : UIWindow<T> where T : UIToolkitHolderBase
    {
    }

    public abstract class UIToolkitWidget<T> : UIWidget<T> where T : UIToolkitHolderBase
    {
    }
}
