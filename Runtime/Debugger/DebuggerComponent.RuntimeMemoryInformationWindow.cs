using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed partial class RuntimeMemoryInformationWindow<T> : ScrollableDebuggerWindowBase where T : UnityEngine.Object
        {
            private const int ShowSampleCount = 300;

            private readonly List<Sample> m_Samples = new List<Sample>();
            private readonly Comparison<Sample> m_SampleComparer = SampleComparer;
            private DateTime m_SampleTime = DateTime.MinValue;
            private long m_SampleSize;
            private long m_DuplicateSampleSize;
            private int m_DuplicateSimpleCount;

            protected override void BuildWindow(VisualElement root)
            {
                string typeName = typeof(T).Name;
                VisualElement actions = CreateSection("Actions", out VisualElement actionCard);
                actionCard.Add(CreateActionButton(Utility.Text.Format("Take Sample for {0}", typeName), TakeSample, DebuggerTheme.ButtonSurfaceActive));
                root.Add(actions);

                VisualElement section = CreateSection(Utility.Text.Format("{0} Runtime Memory", typeName), out VisualElement card);
                if (m_SampleTime <= DateTime.MinValue)
                {
                    card.Add(CreateRow("State", Utility.Text.Format("Please take sample for {0} first.", typeName)));
                }
                else
                {
                    if (m_DuplicateSimpleCount > 0)
                    {
                        card.Add(CreateRow("Summary", Utility.Text.Format("{0} {1}s ({2}) obtained at {3:yyyy-MM-dd HH:mm:ss}, while {4} {1}s ({5}) might be duplicated.", m_Samples.Count, typeName, GetByteLengthString(m_SampleSize), m_SampleTime.ToLocalTime(), m_DuplicateSimpleCount, GetByteLengthString(m_DuplicateSampleSize))));
                    }
                    else
                    {
                        card.Add(CreateRow("Summary", Utility.Text.Format("{0} {1}s ({2}) obtained at {3:yyyy-MM-dd HH:mm:ss}.", m_Samples.Count, typeName, GetByteLengthString(m_SampleSize), m_SampleTime.ToLocalTime())));
                    }

                    int count = Mathf.Min(ShowSampleCount, m_Samples.Count);
                    for (int i = 0; i < count; i++)
                    {
                        Sample sample = m_Samples[i];
                        string title = sample.Highlight ? Utility.Text.Format("[Possible Duplicate] {0}", sample.Name) : sample.Name;
                        string content = Utility.Text.Format("{0} | {1}", sample.Type, GetByteLengthString(sample.Size));
                        card.Add(CreateRow(title, content));
                    }
                }

                root.Add(section);
            }

            private void TakeSample()
            {
                m_SampleTime = DateTime.UtcNow;
                m_SampleSize = 0L;
                m_DuplicateSampleSize = 0L;
                m_DuplicateSimpleCount = 0;
                m_Samples.Clear();

                T[] samples = Resources.FindObjectsOfTypeAll<T>();
                for (int i = 0; i < samples.Length; i++)
                {
                    long sampleSize = Profiler.GetRuntimeMemorySizeLong(samples[i]);
                    m_SampleSize += sampleSize;
                    m_Samples.Add(new Sample(samples[i].name, samples[i].GetType().Name, sampleSize));
                }

                m_Samples.Sort(m_SampleComparer);

                for (int i = 1; i < m_Samples.Count; i++)
                {
                    if (m_Samples[i].Name == m_Samples[i - 1].Name && m_Samples[i].Type == m_Samples[i - 1].Type && m_Samples[i].Size == m_Samples[i - 1].Size)
                    {
                        m_Samples[i].Highlight = true;
                        m_DuplicateSampleSize += m_Samples[i].Size;
                        m_DuplicateSimpleCount++;
                    }
                }

                Rebuild();
            }

            private static int SampleComparer(Sample a, Sample b)
            {
                int result = b.Size.CompareTo(a.Size);
                if (result != 0)
                {
                    return result;
                }

                result = a.Type.CompareTo(b.Type);
                if (result != 0)
                {
                    return result;
                }

                return a.Name.CompareTo(b.Name);
            }
        }
    }
}
