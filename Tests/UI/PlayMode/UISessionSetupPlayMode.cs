using AlicizaX;
using NUnit.Framework;

namespace AlicizaX.UI.Tests
{
    /// <summary>PlayMode 运行同样是长驻域；整批开始前清理上一次遗留的 ServiceWorld。</summary>
    [SetUpFixture]
    public sealed class UISessionSetupPlayMode
    {
        [OneTimeSetUp]
        public void ResetServiceWorld()
        {
            if (AppServices.HasWorld) AppServices.Shutdown();
        }
    }
}
