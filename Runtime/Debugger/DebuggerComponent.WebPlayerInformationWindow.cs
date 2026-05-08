using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class WebPlayerInformationWindow : PollingDebuggerWindowBase
        {
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement section = CreateSection("Web Player Information", out VisualElement card);
#if !UNITY_2017_2_OR_NEWER
                card.Add(CreateRow("Is Web Player", Application.isWebPlayer.ToString()));
#endif
                card.Add(CreateRow("Absolute URL", Application.absoluteURL));
#if !UNITY_2017_2_OR_NEWER
                card.Add(CreateRow("Source Value", Application.srcValue));
#endif
#if !UNITY_2018_2_OR_NEWER
                card.Add(CreateRow("Streamed Bytes", Application.streamedBytes.ToString()));
#endif
#if UNITY_5_3 || UNITY_5_4
                card.Add(CreateRow("Web Security Enabled", Application.webSecurityEnabled.ToString()));
                card.Add(CreateRow("Web Security Host URL", Application.webSecurityHostUrl.ToString()));
#endif
                root.Add(section);
            }
        }
    }
}
