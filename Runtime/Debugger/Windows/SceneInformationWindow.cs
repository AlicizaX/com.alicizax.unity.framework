using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class SceneInformationWindow : PollingDebuggerWindowBase
        {
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement section = CreateSection("Scene Information", out VisualElement card);
                card.Add(CreateRow("Scene Count", SceneManager.sceneCount.ToString()));
                card.Add(CreateRow("Scene Count In Build Settings", SceneManager.sceneCountInBuildSettings.ToString()));

                UnityEngine.SceneManagement.Scene activeScene = SceneManager.GetActiveScene();
                card.Add(CreateRow("Active Scene Handle", activeScene.handle.ToString()));
                card.Add(CreateRow("Active Scene Name", activeScene.name));
                card.Add(CreateRow("Active Scene Path", activeScene.path));
                card.Add(CreateRow("Active Scene Build Index", activeScene.buildIndex.ToString()));
                card.Add(CreateRow("Active Scene Is Dirty", activeScene.isDirty.ToString()));
                card.Add(CreateRow("Active Scene Is Loaded", activeScene.isLoaded.ToString()));
                card.Add(CreateRow("Active Scene Is Valid", activeScene.IsValid().ToString()));
                card.Add(CreateRow("Active Scene Root Count", activeScene.rootCount.ToString()));
                card.Add(CreateRow("Active Scene Is Sub Scene", activeScene.isSubScene.ToString()));
                root.Add(section);
            }
        }
    }
}
