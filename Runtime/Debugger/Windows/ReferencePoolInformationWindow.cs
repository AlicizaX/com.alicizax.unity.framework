using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class ReferencePoolInformationWindow : PollingDebuggerWindowBase
        {
            private readonly Dictionary<string, List<int>> m_GroupedIndices = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            private readonly List<string> m_ActiveAssemblyKeys = new List<string>(16);
            private readonly Comparison<int> m_NormalIndexComparer;
            private readonly Comparison<int> m_FullIndexComparer;

            private MemoryPoolInfo[] m_InfoBuffer = Array.Empty<MemoryPoolInfo>();
            private bool m_ShowFullClassName;
            private Toggle m_ShowFullClassNameToggle;

            public ReferencePoolInformationWindow()
            {
                m_NormalIndexComparer = CompareNormalClassName;
                m_FullIndexComparer = CompareFullClassName;
            }

            protected override void BuildWindow(VisualElement root)
            {
                float scale = DebuggerComponent.Instance != null ? DebuggerComponent.Instance.GetUiScale() : 1f;
                int infoCount = FetchInfos();

                VisualElement overview = CreateSection("Memory Pool Overview", out VisualElement overviewCard);
                overviewCard.Add(CreateRow("Pool Type Count", MemoryPool.Count.ToString()));

                int totalUnused = 0;
                int totalUsing = 0;
                int totalArrayLen = 0;
                for (int i = 0; i < infoCount; i++)
                {
                    ref MemoryPoolInfo info = ref m_InfoBuffer[i];
                    totalUnused += info.UnusedCount;
                    totalUsing += info.UsingCount;
                    totalArrayLen += info.PageCapacity;
                }

                overviewCard.Add(CreateRow("Total Cached Objects", totalUnused.ToString()));
                overviewCard.Add(CreateRow("Total In Use", totalUsing.ToString()));
                overviewCard.Add(CreateRow("Total Page Capacity", totalArrayLen.ToString()));

                m_ShowFullClassNameToggle = CreateConsoleFilterToggle("Show Full ClassName", m_ShowFullClassName, DebuggerTheme.PrimaryText, OnShowFullClassNameChanged);
                overviewCard.Add(m_ShowFullClassNameToggle);

                VisualElement buttonRow = new VisualElement();
                buttonRow.style.flexDirection = FlexDirection.Row;
                buttonRow.style.marginTop = 8f * scale;
                buttonRow.Add(CreateActionButton("Clear All Pools", OnClearAllPools, DebuggerTheme.Danger));
                overviewCard.Add(buttonRow);
                root.Add(overview);

                RebuildGroups(infoCount);
                for (int i = 0; i < m_ActiveAssemblyKeys.Count; i++)
                {
                    string assemblyKey = m_ActiveAssemblyKeys[i];
                    List<int> indices = m_GroupedIndices[assemblyKey];
                    indices.Sort(m_ShowFullClassName ? m_FullIndexComparer : m_NormalIndexComparer);

                    VisualElement section = CreateSection(Utility.Text.Format("Assembly: {0}", assemblyKey), out VisualElement card);
                    for (int j = 0; j < indices.Count; j++)
                    {
                        card.Add(CreatePoolInfoItem(in m_InfoBuffer[indices[j]], m_ShowFullClassName, scale));
                    }

                    root.Add(section);
                }
            }

            private int FetchInfos()
            {
                int poolCount = MemoryPool.Count;
                if (m_InfoBuffer.Length < poolCount)
                    m_InfoBuffer = new MemoryPoolInfo[GetBufferCapacity(poolCount)];

                return MemoryPool.GetAllMemoryPoolInfos(m_InfoBuffer);
            }

            private void RebuildGroups(int infoCount)
            {
                foreach (KeyValuePair<string, List<int>> pair in m_GroupedIndices)
                    pair.Value.Clear();

                m_ActiveAssemblyKeys.Clear();
                for (int i = 0; i < infoCount; i++)
                {
                    ref MemoryPoolInfo info = ref m_InfoBuffer[i];
                    string assemblyName = info.Type.Assembly.GetName().Name;
                    if (!m_GroupedIndices.TryGetValue(assemblyName, out List<int> indices))
                    {
                        indices = new List<int>(8);
                        m_GroupedIndices.Add(assemblyName, indices);
                    }

                    if (indices.Count == 0)
                        m_ActiveAssemblyKeys.Add(assemblyName);

                    indices.Add(i);
                }

                m_ActiveAssemblyKeys.Sort(StringComparer.Ordinal);
            }

            private void OnShowFullClassNameChanged(bool value)
            {
                if (m_ShowFullClassName == value)
                    return;

                m_ShowFullClassName = value;
                Rebuild();
            }

            private void OnClearAllPools()
            {
                MemoryPoolRegistry.ClearAll();
                Rebuild();
            }

            private int CompareNormalClassName(int leftIndex, int rightIndex)
            {
                ref MemoryPoolInfo left = ref m_InfoBuffer[leftIndex];
                ref MemoryPoolInfo right = ref m_InfoBuffer[rightIndex];
                return left.Type.Name.CompareTo(right.Type.Name);
            }

            private int CompareFullClassName(int leftIndex, int rightIndex)
            {
                ref MemoryPoolInfo left = ref m_InfoBuffer[leftIndex];
                ref MemoryPoolInfo right = ref m_InfoBuffer[rightIndex];
                return left.Type.FullName.CompareTo(right.Type.FullName);
            }

            private static int GetBufferCapacity(int count)
            {
                int capacity = 8;
                while (capacity < count)
                    capacity <<= 1;

                return capacity;
            }

            private static VisualElement CreatePoolInfoItem(in MemoryPoolInfo info, bool showFullName, float scale)
            {
                VisualElement item = CreateCard();
                item.style.marginBottom = 8f * scale;
                item.style.backgroundColor = DebuggerTheme.PanelSurfaceAlt;

                string title = showFullName ? info.Type.FullName : info.Type.Name;
                Label titleLabel = new Label(title ?? string.Empty);
                titleLabel.style.color = DebuggerTheme.PrimaryText;
                titleLabel.style.fontSize = 16f * scale;
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.whiteSpace = WhiteSpace.Normal;
                titleLabel.style.marginBottom = 6f * scale;
                item.Add(titleLabel);

                string stats = Utility.Text.Format(
                    "Unused {0} | Using {1} | Acquire {2} | Release {3} | Created {4}",
                    info.UnusedCount, info.UsingCount,
                    info.AcquireCount, info.ReleaseCount,
                    info.CreateCount);
                Label statsLabel = new Label(stats);
                statsLabel.style.color = DebuggerTheme.SecondaryText;
                statsLabel.style.fontSize = 14f * scale;
                statsLabel.style.whiteSpace = WhiteSpace.Normal;
                statsLabel.style.marginBottom = 4f * scale;
                item.Add(statsLabel);

                string recycleStatus = Utility.Text.Format(
                    "Target {0} | MaxCap {1} | Idle {2}f | PageCap {3}",
                    info.TargetFreeReserve, info.MaxCapacity,
                    info.IdleFrames, info.PageCapacity);
                Label recycleLabel = new Label(recycleStatus);
                recycleLabel.style.fontSize = 13f * scale;
                recycleLabel.style.whiteSpace = WhiteSpace.Normal;

                if (info.IdleFrames >= 300)
                    recycleLabel.style.color = DebuggerTheme.Warning;
                else if (info.IdleFrames >= 200)
                    recycleLabel.style.color = new Color(0.9f, 0.7f, 0.3f);
                else
                    recycleLabel.style.color = DebuggerTheme.SecondaryText;

                item.Add(recycleLabel);
                return item;
            }
        }
    }
}
