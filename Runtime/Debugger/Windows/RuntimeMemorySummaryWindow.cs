using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed partial class RuntimeMemorySummaryWindow : ScrollableDebuggerWindowBase
        {
            private readonly List<Record> m_Records = new List<Record>();
            private readonly Comparison<Record> m_RecordComparer = RecordComparer;
            private DateTime m_SampleTime = DateTime.MinValue;
            private int m_SampleCount;
            private long m_SampleSize;

            protected override void BuildWindow(VisualElement root)
            {
                VisualElement actions = CreateSection("Actions", out VisualElement actionCard);
                actionCard.Add(CreateActionButton("Take Sample", TakeSample, DebuggerTheme.ButtonSurfaceActive));
                root.Add(actions);

                VisualElement summary = CreateSection("Runtime Memory Summary", out VisualElement card);
                if (m_SampleTime <= DateTime.MinValue)
                {
                    card.Add(CreateRow("State", "Please take sample first."));
                }
                else
                {
                    card.Add(CreateRow("Summary", Utility.Text.Format("{0} Objects ({1}) obtained at {2:yyyy-MM-dd HH:mm:ss}.", m_SampleCount, GetByteLengthString(m_SampleSize), m_SampleTime.ToLocalTime())));
                    for (int i = 0; i < m_Records.Count; i++)
                    {
                        card.Add(CreateRow(m_Records[i].Name, Utility.Text.Format("Count {0} | Size {1}", m_Records[i].Count, GetByteLengthString(m_Records[i].Size))));
                    }
                }

                root.Add(summary);
            }

            private void TakeSample()
            {
                m_Records.Clear();
                m_SampleTime = DateTime.UtcNow;
                m_SampleCount = 0;
                m_SampleSize = 0L;

                UnityEngine.Object[] samples = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
                for (int i = 0; i < samples.Length; i++)
                {
                    long sampleSize = Profiler.GetRuntimeMemorySizeLong(samples[i]);
                    string name = samples[i].GetType().Name;
                    m_SampleCount++;
                    m_SampleSize += sampleSize;

                    Record record = null;
                    for (int j = 0; j < m_Records.Count; j++)
                    {
                        if (m_Records[j].Name == name)
                        {
                            record = m_Records[j];
                            break;
                        }
                    }

                    if (record == null)
                    {
                        record = new Record(name);
                        m_Records.Add(record);
                    }

                    record.Count++;
                    record.Size += sampleSize;
                }

                m_Records.Sort(m_RecordComparer);
                Rebuild();
            }

            private static int RecordComparer(Record a, Record b)
            {
                int result = b.Size.CompareTo(a.Size);
                if (result != 0)
                {
                    return result;
                }

                result = a.Count.CompareTo(b.Count);
                if (result != 0)
                {
                    return result;
                }

                return a.Name.CompareTo(b.Name);
            }
        }
    }
}
