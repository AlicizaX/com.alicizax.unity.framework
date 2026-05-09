using AlicizaX.ObjectPool;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.Debugger.Runtime
{
    public sealed partial class DebuggerComponent
    {
        private sealed class ObjectPoolInformationWindow : PollingDebuggerWindowBase
        {
            private IObjectPoolService m_ObjectPoolService;
            private ObjectPoolService m_ObjectPoolServiceImpl;
            private ObjectPoolBase[] m_ObjectPools;
            private ObjectInfo[] m_ObjectInfos;

            public override void Initialize(params object[] args)
            {
                m_ObjectPoolService = AppServices.App.Require<IObjectPoolService>();
                m_ObjectPoolServiceImpl = m_ObjectPoolService as ObjectPoolService;
            }

            protected override void BuildWindow(VisualElement root)
            {
                if (m_ObjectPoolService == null)
                {
                    return;
                }

                VisualElement overview = CreateSection("Object Pool Overview", out VisualElement overviewCard);
                overviewCard.Add(CreateRow("Object Pool Count", m_ObjectPoolService.Count.ToString()));
                root.Add(overview);

                int objectPoolCount = EnsureObjectPoolBuffer(m_ObjectPoolService.Count);
                objectPoolCount = m_ObjectPoolServiceImpl != null
                    ? m_ObjectPoolServiceImpl.GetAllObjectPools(true, m_ObjectPools)
                    : 0;
                for (int i = 0; i < objectPoolCount; i++)
                {
                    ObjectPoolBase objectPool = m_ObjectPools[i];
                    VisualElement section = CreateSection(Utility.Text.Format("Object Pool: {0}", objectPool.FullName), out VisualElement card);
                    card.Add(CreateRow("Name", objectPool.Name));
                    card.Add(CreateRow("Type", objectPool.ObjectType.FullName));
                    card.Add(CreateRow("Auto Release Interval", objectPool.AutoReleaseInterval.ToString()));
                    card.Add(CreateRow("Capacity", objectPool.Capacity.ToString()));
                    card.Add(CreateRow("Used Count", objectPool.Count.ToString()));
                    card.Add(CreateRow("Expire Time", objectPool.ExpireTime.ToString()));
                    card.Add(CreateRow("Priority", objectPool.Priority.ToString()));

                    int objectInfoCount = EnsureObjectInfoBuffer(objectPool.Count);
                    objectInfoCount = objectPool.GetAllObjectInfos(m_ObjectInfos);
                    if (objectInfoCount <= 0)
                    {
                        card.Add(CreateRow("Entries", "Object Pool is Empty ..."));
                    }
                    else
                    {
                        for (int j = 0; j < objectInfoCount; j++)
                        {
                            ObjectInfo info = m_ObjectInfos[j];
                            string title = string.IsNullOrEmpty(info.Name) ? "<None>" : info.Name;
                            string content = Utility.Text.Format(
                                "Locked {0} | {1} {2} | Flag {3} | Last Use {4}",
                                info.Locked,
                                objectPool.AllowMultiSpawn ? "Count" : "InUse",
                                objectPool.AllowMultiSpawn ? info.SpawnCount.ToString() : info.IsInUse.ToString(),
                                info.CustomCanReleaseFlag,
                                Utility.Text.Format("{0:F1}s ago", Time.realtimeSinceStartup - info.LastUseTime));
                            card.Add(CreateRow(title, content));
                        }
                    }

                    root.Add(section);
                }
            }

            private int EnsureObjectPoolBuffer(int count)
            {
                if (count <= 0)
                {
                    if (m_ObjectPools == null || m_ObjectPools.Length == 0)
                        m_ObjectPools = new ObjectPoolBase[1];
                    return 0;
                }

                if (m_ObjectPools == null || m_ObjectPools.Length < count)
                    m_ObjectPools = new ObjectPoolBase[count];

                return count;
            }

            private int EnsureObjectInfoBuffer(int count)
            {
                if (count <= 0)
                {
                    if (m_ObjectInfos == null || m_ObjectInfos.Length == 0)
                        m_ObjectInfos = new ObjectInfo[1];
                    return 0;
                }

                if (m_ObjectInfos == null || m_ObjectInfos.Length < count)
                    m_ObjectInfos = new ObjectInfo[count];

                return count;
            }
        }
    }
}
