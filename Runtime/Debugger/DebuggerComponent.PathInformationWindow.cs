using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class PathInformationWindow : PollingDebuggerWindowBase
        {
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement section = CreateSection("Path Information", out VisualElement card);
                card.Add(CreateRow("Current Directory", Utility.Path.GetRegularPath(Environment.CurrentDirectory)));
                card.Add(CreateRow("Data Path", Utility.Path.GetRegularPath(Application.dataPath)));
                card.Add(CreateRow("Persistent Data Path", Utility.Path.GetRegularPath(Application.persistentDataPath)));
                card.Add(CreateRow("Streaming Assets Path", Utility.Path.GetRegularPath(Application.streamingAssetsPath)));
                card.Add(CreateRow("Temporary Cache Path", Utility.Path.GetRegularPath(Application.temporaryCachePath)));
#if UNITY_2018_3_OR_NEWER
                card.Add(CreateRow("Console Log Path", Utility.Path.GetRegularPath(Application.consoleLogPath)));
#endif
                root.Add(section);
            }
        }
    }
}
