#if UNITY_EDITOR
namespace AlicizaX.UI.Tests
{
    public sealed class UIWindowStateMatrixPlayModeTests : UIWindowStateMatrixCases { }
    public sealed class UIWidgetStateMatrixPlayModeTests : UIWidgetStateMatrixCases { }
    public sealed class UIReentryMatrixPlayModeTests : UIReentryMatrixCases { }
    public sealed class UILayerStackPlayModeTests : UILayerStackCases { }
    public sealed class UIRegistryPlayModeTests : UIRegistryCases { }
    public sealed class UINavigationTabAdditionPlayModeTests : UINavigationTabAdditionCases { }
    public sealed class UIAllocationPlayModeTests : UIAllocationCases { }
    public sealed class UIWindowModelPlayModeTests : UIWindowModelCases { }
    public sealed class UILeakPlayModeTests : UILeakCases { }
}
#endif
