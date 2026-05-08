using UnityEngine;
#if UNITY_5_5_OR_NEWER
using UnityEngine.Rendering;
#endif
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class EnvironmentInformationWindow : PollingDebuggerWindowBase
        {
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement section = CreateSection("Environment Information", out VisualElement card);
                card.Add(CreateRow("Product Name", Application.productName));
                card.Add(CreateRow("Company Name", Application.companyName));
#if UNITY_5_6_OR_NEWER
                card.Add(CreateRow("Game Identifier", Application.identifier));
#else
                card.Add(CreateRow("Game Identifier", Application.bundleIdentifier));
#endif
                card.Add(CreateRow("Game Framework Version", AppVersion.GameFrameworkVersion));
                card.Add(CreateRow("Game Version", Utility.Text.Format("{0} ({1})", AppVersion.GameVersion, AppVersion.GameFrameworkVersion)));
                card.Add(CreateRow("Application Version", Application.version));
                card.Add(CreateRow("Unity Version", Application.unityVersion));
                card.Add(CreateRow("Platform", Application.platform.ToString()));
                card.Add(CreateRow("System Language", Application.systemLanguage.ToString()));
                card.Add(CreateRow("Cloud Project Id", Application.cloudProjectId));
#if UNITY_5_6_OR_NEWER
                card.Add(CreateRow("Build Guid", Application.buildGUID));
#endif
                card.Add(CreateRow("Target Frame Rate", Application.targetFrameRate.ToString()));
                card.Add(CreateRow("Internet Reachability", Application.internetReachability.ToString()));
                card.Add(CreateRow("Background Loading Priority", Application.backgroundLoadingPriority.ToString()));
                card.Add(CreateRow("Is Playing", Application.isPlaying.ToString()));
#if UNITY_5_5_OR_NEWER
                card.Add(CreateRow("Splash Screen Is Finished", SplashScreen.isFinished.ToString()));
#else
                card.Add(CreateRow("Is Showing Splash Screen", Application.isShowingSplashScreen.ToString()));
#endif
                card.Add(CreateRow("Run In Background", Application.runInBackground.ToString()));
#if UNITY_5_5_OR_NEWER
                card.Add(CreateRow("Install Name", Application.installerName));
#endif
                card.Add(CreateRow("Install Mode", Application.installMode.ToString()));
                card.Add(CreateRow("Sandbox Type", Application.sandboxType.ToString()));
                card.Add(CreateRow("Is Mobile Platform", Application.isMobilePlatform.ToString()));
                card.Add(CreateRow("Is Console Platform", Application.isConsolePlatform.ToString()));
                card.Add(CreateRow("Is Editor", Application.isEditor.ToString()));
                card.Add(CreateRow("Is Debug Build", Debug.isDebugBuild.ToString()));
#if UNITY_5_6_OR_NEWER
                card.Add(CreateRow("Is Focused", Application.isFocused.ToString()));
#endif
#if UNITY_2018_2_OR_NEWER
                card.Add(CreateRow("Is Batch Mode", Application.isBatchMode.ToString()));
#endif
#if UNITY_5_3
                card.Add(CreateRow("Stack Trace Log Type", Application.stackTraceLogType.ToString()));
#endif
                root.Add(section);
            }
        }
    }
}
