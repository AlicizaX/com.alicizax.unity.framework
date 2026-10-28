using AlicizaX;
using NUnit.Framework;

namespace AlicizaX.UI.Tests
{
    /// <summary>
    /// 编辑器会话内复用同一 AppDomain：上一次运行中断可能留下未释放的 ServiceWorld，
    /// 在整批测试开始前清理一次，避免后续用例被污染。
    /// </summary>
    [SetUpFixture]
    public sealed class UISessionSetup
    {
        [OneTimeSetUp]
        public void ResetServiceWorld()
        {
            if (AppServices.HasWorld) AppServices.Shutdown();
        }
    }
}
