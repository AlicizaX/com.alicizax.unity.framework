#if UNITY_EDITOR
using AlicizaX.UI.Runtime;
using NUnit.Framework;

namespace AlicizaX.UI.Tests
{
    internal static class UIInvariantChecker
    {
        private const int CacheCapacity = 64;
        private static readonly UIWindowDebugInfo Window = new();
        private static readonly UIWindowDebugInfo[] Cached = CreateCacheInfos();

        internal static void Verify(UIService service)
        {
            IUIDebugService debug = service;
            for (int layer = 0; layer < debug.LayerCount; layer++)
            {
                for (int index = 0; debug.FillWindowDebugInfo(layer, index, Window); index++)
                {
                    string name = Window.LogicTypeName;
                    Assert.That(Window.State, Is.Not.EqualTo(UIState.Destroyed), "I6 " + name);
                    Assert.That(Window.HolderTransform == null, Is.EqualTo(Window.State == UIState.Loading), "I1 " + name);
                    Assert.That(Window.Depth, Is.EqualTo(layer * UIService.LAYER_DEEP + index * UIService.WINDOW_DEEP), "I4 " + name);
                }
            }
            int cached = debug.FillCacheDebugInfo(Cached);
            Assert.That(cached, Is.EqualTo(debug.CacheWindowCount), "I5 count");
            for (int i = 0; i < cached; i++)
            {
                Assert.That(Cached[i].State, Is.EqualTo(UIState.Closed), "I5 " + Cached[i].LogicTypeName);
                Assert.That(Cached[i].Updating, Is.False, "I2 " + Cached[i].LogicTypeName);
                for (int layer = 0; layer < debug.LayerCount; layer++)
                    for (int index = 0; debug.FillWindowDebugInfo(layer, index, Window); index++)
                        Assert.That(Window.LogicTypeName, Is.Not.EqualTo(Cached[i].LogicTypeName), "I5 " + Window.LogicTypeName);
            }
        }

        private static UIWindowDebugInfo[] CreateCacheInfos()
        {
            var infos = new UIWindowDebugInfo[CacheCapacity];
            for (int i = 0; i < infos.Length; i++) infos[i] = new UIWindowDebugInfo();
            return infos;
        }
    }
}
#endif
