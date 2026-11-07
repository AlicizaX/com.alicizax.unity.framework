using AlicizaX;
using NUnit.Framework;

namespace AlicizaX.UI.Tests
{
    /// <summary>PlayMode 运行同样是长驻域；整批开始前清理上一次遗留的 ServiceRegistry。</summary>
    [SetUpFixture]
    public sealed class UISessionSetupPlayMode
    {
        [OneTimeSetUp]
        public void ResetServiceRegistry()
        {
            if (AppServices.IsAvailable) AppServices.Shutdown();
        }
    }
}
