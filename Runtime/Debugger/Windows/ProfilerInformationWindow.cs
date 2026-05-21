using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class ProfilerInformationWindow : PollingDebuggerWindowBase
        {
            protected override void BuildWindow(VisualElement root)
            {
                VisualElement section = CreateSection("Profiler Information", out VisualElement card);
                card.Add(CreateRow("Supported", Profiler.supported.ToString()));
                card.Add(CreateRow("Enabled", Profiler.enabled.ToString()));
                card.Add(CreateRow("Enable Binary Log", Profiler.enableBinaryLog ? Utility.Text.Format("True, {0}", Profiler.logFile) : "False"));
                card.Add(CreateRow("Enable Allocation Callstacks", Profiler.enableAllocationCallstacks.ToString()));
                card.Add(CreateRow("Area Count", Profiler.areaCount.ToString()));
                card.Add(CreateRow("Max Used Memory", GetByteLengthString(Profiler.maxUsedMemory)));
                card.Add(CreateRow("Mono Used Size", GetByteLengthString(Profiler.GetMonoUsedSizeLong())));
                card.Add(CreateRow("Mono Heap Size", GetByteLengthString(Profiler.GetMonoHeapSizeLong())));
                card.Add(CreateRow("Used Heap Size", GetByteLengthString(Profiler.usedHeapSizeLong)));
                card.Add(CreateRow("Total Allocated Memory", GetByteLengthString(Profiler.GetTotalAllocatedMemoryLong())));
                card.Add(CreateRow("Total Reserved Memory", GetByteLengthString(Profiler.GetTotalReservedMemoryLong())));
                card.Add(CreateRow("Total Unused Reserved Memory", GetByteLengthString(Profiler.GetTotalUnusedReservedMemoryLong())));
                card.Add(CreateRow("Allocated Memory For Graphics Driver", GetByteLengthString(Profiler.GetAllocatedMemoryForGraphicsDriver())));
                card.Add(CreateRow("Temp Allocator Size", GetByteLengthString(Profiler.GetTempAllocatorSize())));
                card.Add(CreateRow("Marshal Cached HGlobal Size", GetByteLengthString(Utility.Marshal.CachedHGlobalSize)));
                root.Add(section);
            }
        }
    }
}
