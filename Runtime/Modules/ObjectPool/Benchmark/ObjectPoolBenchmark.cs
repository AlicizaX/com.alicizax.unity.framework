#if UNITY_EDITOR
using System;
using System.Diagnostics;
using Cysharp.Text;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AlicizaX.ObjectPool
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game Framework/ObjectPool Benchmark")]
    public sealed class ObjectPoolBenchmark : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private int objectCount = 10000;
        [SerializeField] private int loopCount = 100000;
        [SerializeField] private int sameNameCount = 1024;
        [SerializeField] private int occupiedSameNameCount = 1023;
        [SerializeField] private int extremeSameNameCount = 4096;
        [SerializeField] private bool includeReleaseAllUnused = true;
        [SerializeField] private bool logEachCase = true;
        [SerializeField] private bool logMemoryDelta = true;
        [SerializeField] private int maxCapturedLogChars = 128 * 1024;

        private static readonly ProfilerMarker s_TotalMarker = new ProfilerMarker("ObjectPoolBenchmark.Total");
        private static readonly ProfilerMarker s_RegisterMarker = new ProfilerMarker("ObjectPoolBenchmark.Register");
        private static readonly ProfilerMarker s_SpawnUnspawnMarker = new ProfilerMarker("ObjectPoolBenchmark.SpawnUnspawn");
        private static readonly ProfilerMarker s_OccupiedSameNameMarker = new ProfilerMarker("ObjectPoolBenchmark.OccupiedSameName");
        private static readonly ProfilerMarker s_ExtremeSameNameMarker = new ProfilerMarker("ObjectPoolBenchmark.ExtremeSameName");
        private static readonly ProfilerMarker s_MultiSpawnMarker = new ProfilerMarker("ObjectPoolBenchmark.MultiSpawn");
        private static readonly ProfilerMarker s_CapacityMarker = new ProfilerMarker("ObjectPoolBenchmark.Capacity");
        private static readonly ProfilerMarker s_ExpireMarker = new ProfilerMarker("ObjectPoolBenchmark.Expire");
        private static readonly ProfilerMarker s_ReleaseMarker = new ProfilerMarker("ObjectPoolBenchmark.ReleaseAllUnused");
        private static readonly ProfilerMarker s_MixedNameMarker = new ProfilerMarker("ObjectPoolBenchmark.MixedName");
        private static readonly ProfilerMarker s_DestroyMarker = new ProfilerMarker("ObjectPoolBenchmark.DestroyRecreate");
        private static readonly ProfilerMarker s_CursorRecoveryMarker = new ProfilerMarker("ObjectPoolBenchmark.CursorRecovery");

        private readonly Stopwatch m_Stopwatch = new Stopwatch();
        private Utf16ValueStringBuilder m_LogBuilder;
        private IObjectPoolService m_Service;
        private int m_FailCount;
        private int m_CaseCount;
        private bool m_LogBuilderCreated;
        private long m_CaseAllocBefore;
        private long m_CaseAllocAfter;

        private void OnEnable()
        {
            ClearCapturedConsoleOutput();
            Application.logMessageReceived += OnLogMessageReceived;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
            m_LogBuilder.Dispose();
            m_LogBuilderCreated = false;
        }

        private void Start()
        {
            if (runOnStart)
                RunAll();
        }

        [ContextMenu("Run ObjectPool Benchmark")]
        public void RunAll()
        {
            ClearCapturedConsoleOutput();
            EnsureService();
            if (m_Service == null)
                return;

            m_FailCount = 0;
            m_CaseCount = 0;

            using (s_TotalMarker.Auto())
            {
                RunCase("SingleName Spawn/Unspawn", RunSingleNameSpawnUnspawn);
                RunCase("NullName Normalization", RunNullNameNormalization);
                RunCase("SameName Stress", RunSameNameStress);
                RunCase("SameName Occupied Scan", RunSameNameOccupiedScan);
                RunCase("SameName Extreme OneFree", RunSameNameExtremeOneFree);
                RunCase("MultiSpawn", RunMultiSpawn);
                RunCase("MixedName RoundRobin", RunMixedNameRoundRobin);
                RunCase("Capacity Guard", RunCapacityGuard);
                RunCase("Spawned Release Guard", RunSpawnedReleaseGuard);
                RunCase("Destroy Recreate", RunDestroyRecreate);
                RunCase("Cursor Release Recovery", RunCursorReleaseRecovery);
                RunCase("Locked Release Guard", RunLockedReleaseGuard);
                RunCase("Custom Release Guard", RunCustomReleaseGuard);
                RunCase("Expire Release", RunExpireRelease);

                if (includeReleaseAllUnused)
                    RunCase("ReleaseAllUnused", RunReleaseAllUnused);
            }

            Debug.Log(BuildLog("ObjectPool benchmark finished. cases=", m_CaseCount, ", fails=", m_FailCount));
        }

        [ContextMenu("Copy Captured Console Output")]
        public void CopyCapturedConsoleOutput()
        {
            EnsureLogBuilder();
            string text = m_LogBuilder.ToString();
            GUIUtility.systemCopyBuffer = text;
            Debug.Log(BuildLog("ObjectPoolBenchmark copied console output chars=", text.Length, ", max=", maxCapturedLogChars));
        }

        [ContextMenu("Clear Captured Console Output")]
        public void ClearCapturedConsoleOutput()
        {
            m_LogBuilder.Dispose();
            m_LogBuilder = ZString.CreateStringBuilder();
            m_LogBuilderCreated = true;
        }

        private void EnsureLogBuilder()
        {
            if (!m_LogBuilderCreated)
            {
                m_LogBuilder = ZString.CreateStringBuilder();
                m_LogBuilderCreated = true;
            }
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            EnsureLogBuilder();
            if (m_LogBuilder.Length >= maxCapturedLogChars)
                return;

            m_LogBuilder.Append('[');
            m_LogBuilder.Append(type);
            m_LogBuilder.Append("] ");
            m_LogBuilder.Append(condition);
            m_LogBuilder.AppendLine();

            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            {
                m_LogBuilder.Append(stackTrace);
                m_LogBuilder.AppendLine();
            }
        }

        private void EnsureService()
        {
            if (m_Service != null)
                return;

            if (AppServices.HasWorld && AppServices.App.TryGet(out m_Service))
                return;

            if (!AppServices.HasWorld && GetComponent<AppServiceRoot>() == null)
                gameObject.AddComponent<AppServiceRoot>();

            if (GetComponent<ObjectPoolComponent>() == null)
                gameObject.AddComponent<ObjectPoolComponent>();

            if (!AppServices.HasWorld || !AppServices.App.TryGet(out m_Service))
                Debug.LogError("ObjectPoolBenchmark requires ObjectPoolComponent registration.");
        }

        private void RunCase(string caseName, Action action)
        {
            m_CaseCount++;
            m_CaseAllocBefore = GetAllocatedBytesForCurrentThread();
            m_CaseAllocAfter = m_CaseAllocBefore;
            m_Stopwatch.Restart();
            action();
            if (m_Stopwatch.IsRunning)
            {
                m_Stopwatch.Stop();
                m_CaseAllocAfter = GetAllocatedBytesForCurrentThread();
            }

            if (logEachCase)
            {
                if (logMemoryDelta)
                    Debug.Log(BuildLog("[ObjectPoolBenchmark] ", caseName, " ms=", m_Stopwatch.Elapsed.TotalMilliseconds, " gcAlloc=", m_CaseAllocAfter - m_CaseAllocBefore));
                else
                    Debug.Log(BuildLog("[ObjectPoolBenchmark] ", caseName, " ms=", m_Stopwatch.Elapsed.TotalMilliseconds));
            }
        }

        private void RestartCaseMeasure()
        {
            m_CaseAllocBefore = GetAllocatedBytesForCurrentThread();
            m_CaseAllocAfter = m_CaseAllocBefore;
            m_Stopwatch.Restart();
        }

        private void StopCaseMeasure()
        {
            m_Stopwatch.Stop();
            m_CaseAllocAfter = GetAllocatedBytesForCurrentThread();
        }

        private long GetAllocatedBytesForCurrentThread()
        {
            return logMemoryDelta ? GC.GetAllocatedBytesForCurrentThread() : 0L;
        }

        private void RunSingleNameSpawnUnspawn()
        {
            using (s_SpawnUnspawnMarker.Auto())
            {
                string poolName = MakePoolName("single");
                IObjectPool<BenchmarkObject> pool = CreatePool(poolName, false, objectCount, float.MaxValue);
                BenchmarkTarget target = new BenchmarkTarget(1);
                BenchmarkObject obj = BenchmarkObject.Create(string.Empty, target, false, true);
                pool.Register(obj, false);

                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    BenchmarkObject spawned = pool.Spawn();
                    AssertReference(spawned, obj, "single spawn returned wrong object");
                    pool.Unspawn(spawned);
                }
                StopCaseMeasure();

                DestroyPool(poolName);
            }
        }

        private void RunNullNameNormalization()
        {
            string poolName = MakePoolName("null-name");
            IObjectPool<BenchmarkObject> pool = CreatePool(poolName, false, 4, float.MaxValue);
            BenchmarkTarget target = new BenchmarkTarget(2);
            BenchmarkObject obj = BenchmarkObject.Create(null, target, false, true);
            pool.Register(obj, false);

            RestartCaseMeasure();
            BenchmarkObject spawned = pool.Spawn(null);
            AssertReference(spawned, obj, "Spawn(null) failed for empty-name object");
            pool.Unspawn(spawned);
            StopCaseMeasure();

            DestroyPool(poolName);
        }

        private void RunSameNameStress()
        {
            using (s_RegisterMarker.Auto())
            {
                string poolName = MakePoolName("same-name");
                IObjectPool<BenchmarkObject> pool = CreatePool(poolName, false, sameNameCount, float.MaxValue);
                BenchmarkTarget[] targets = new BenchmarkTarget[sameNameCount];
                BenchmarkObject[] objects = new BenchmarkObject[sameNameCount];

                for (int i = 0; i < sameNameCount; i++)
                {
                    targets[i] = new BenchmarkTarget(i);
                    objects[i] = BenchmarkObject.Create("same", targets[i], false, true);
                    pool.Register(objects[i], false);
                }

                int cycles = Math.Max(1, loopCount / Math.Max(1, sameNameCount));
                RestartCaseMeasure();
                for (int c = 0; c < cycles; c++)
                {
                    for (int i = 0; i < sameNameCount; i++)
                    {
                        BenchmarkObject spawned = pool.Spawn("same");
                        AssertNotNull(spawned, "same-name spawn returned null");
                        pool.Unspawn(spawned);
                    }
                }
                StopCaseMeasure();

                DestroyPool(poolName);
            }
        }

        private void RunSameNameOccupiedScan()
        {
            using (s_OccupiedSameNameMarker.Auto())
            {
                int totalCount = Math.Max(2, sameNameCount);
                int blockedCount = Math.Max(1, Math.Min(occupiedSameNameCount, totalCount - 1));
                int cycles = Math.Max(1, loopCount / Math.Max(1, blockedCount));
                string poolName = MakePoolName("same-name-occupied");
                IObjectPool<BenchmarkObject> pool = CreatePool(poolName, false, totalCount, float.MaxValue);
                BenchmarkObject[] blockedObjects = new BenchmarkObject[blockedCount];

                for (int i = 0; i < totalCount; i++)
                    pool.Register(BenchmarkObject.Create("occupied", new BenchmarkTarget(i), false, true), false);

                for (int i = 0; i < blockedCount; i++)
                {
                    BenchmarkObject spawned = pool.Spawn("occupied");
                    AssertNotNull(spawned, "occupied setup spawn returned null");
                    if (spawned != null)
                        blockedObjects[i] = spawned;
                }

                RestartCaseMeasure();
                for (int i = 0; i < cycles; i++)
                {
                    BenchmarkObject spawned = pool.Spawn("occupied");
                    AssertNotNull(spawned, "occupied scan spawn returned null");
                    if (spawned != null)
                        pool.Unspawn(spawned);
                }
                StopCaseMeasure();

                for (int i = 0; i < blockedCount; i++)
                {
                    if (blockedObjects[i] != null)
                        pool.Unspawn(blockedObjects[i]);
                }

                DestroyPool(poolName);
            }
        }

        private void RunSameNameExtremeOneFree()
        {
            using (s_ExtremeSameNameMarker.Auto())
            {
                int totalCount = Math.Max(2, extremeSameNameCount);
                int blockedCount = totalCount - 1;
                string poolName = MakePoolName("same-name-extreme");
                IObjectPool<BenchmarkObject> pool = CreatePool(poolName, false, totalCount, float.MaxValue);
                BenchmarkObject[] blockedObjects = new BenchmarkObject[blockedCount];

                for (int i = 0; i < totalCount; i++)
                    pool.Register(BenchmarkObject.Create("extreme", new BenchmarkTarget(i), false, true), false);

                for (int i = 0; i < blockedCount; i++)
                {
                    BenchmarkObject spawned = pool.Spawn("extreme");
                    AssertNotNull(spawned, "extreme setup spawn returned null");
                    if (spawned != null)
                        blockedObjects[i] = spawned;
                }

                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    BenchmarkObject spawned = pool.Spawn("extreme");
                    AssertNotNull(spawned, "extreme one-free spawn returned null");
                    if (spawned != null)
                        pool.Unspawn(spawned);
                }
                StopCaseMeasure();

                for (int i = 0; i < blockedCount; i++)
                {
                    if (blockedObjects[i] != null)
                        pool.Unspawn(blockedObjects[i]);
                }

                DestroyPool(poolName);
            }
        }

        private void RunMultiSpawn()
        {
            using (s_MultiSpawnMarker.Auto())
            {
                string poolName = MakePoolName("multi");
                IObjectPool<BenchmarkObject> pool = CreatePool(poolName, true, 4, float.MaxValue);
                BenchmarkTarget target = new BenchmarkTarget(3);
                BenchmarkObject obj = BenchmarkObject.Create("multi", target, false, true);
                pool.Register(obj, false);

                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    BenchmarkObject spawned = pool.Spawn("multi");
                    AssertReference(spawned, obj, "multi spawn returned wrong object");
                }

                for (int i = 0; i < loopCount; i++)
                    pool.Unspawn(obj);
                StopCaseMeasure();

                DestroyPool(poolName);
            }
        }

        private void RunMixedNameRoundRobin()
        {
            using (s_MixedNameMarker.Auto())
            {
                const int nameCount = 16;
                int perNameCount = Math.Max(1, sameNameCount / nameCount);
                string poolName = MakePoolName("mixed-name");
                IObjectPool<BenchmarkObject> pool = CreatePool(poolName, false, sameNameCount, float.MaxValue);
                string[] names = new string[nameCount];

                for (int n = 0; n < nameCount; n++)
                {
                    names[n] = MakeIndexedName("mixed", n);
                    for (int i = 0; i < perNameCount; i++)
                        pool.Register(BenchmarkObject.Create(names[n], new BenchmarkTarget(n * perNameCount + i), false, true), false);
                }

                RestartCaseMeasure();
                for (int i = 0; i < loopCount; i++)
                {
                    string objectName = names[i & (nameCount - 1)];
                    BenchmarkObject spawned = pool.Spawn(objectName);
                    AssertNotNull(spawned, "mixed-name spawn returned null");
                    if (spawned != null)
                        pool.Unspawn(spawned);
                }
                StopCaseMeasure();

                DestroyPool(poolName);
            }
        }

        private void RunCapacityGuard()
        {
            using (s_CapacityMarker.Auto())
            {
                string poolName = MakePoolName("capacity");
                IObjectPool<BenchmarkObject> pool = CreatePool(poolName, false, 1, float.MaxValue);
                BenchmarkTarget first = new BenchmarkTarget(4);
                BenchmarkTarget second = new BenchmarkTarget(5);
                BenchmarkObject firstObject = BenchmarkObject.Create("cap", first, false, true);
                BenchmarkObject secondObject = BenchmarkObject.Create("cap", second, false, true);

                RestartCaseMeasure();
                pool.Register(firstObject, false);
                pool.Register(secondObject, false);

                AssertEqual(pool.Count, 1, "capacity guard allowed over-register");
                BenchmarkObject spawned = pool.Spawn("cap");
                AssertNotNull(spawned, "capacity replacement object cannot spawn");
                if (spawned != null)
                    AssertReference(spawned.Target, second, "capacity replacement kept wrong object");

                pool.Unspawn(spawned);
                pool.ReleaseAllUnused();
                AssertEqual(pool.Count, 0, "capacity release did not clear unused object");
                StopCaseMeasure();

                DestroyPool(poolName);
            }
        }

        private void RunSpawnedReleaseGuard()
        {
            string poolName = MakePoolName("spawned-release");
            IObjectPool<BenchmarkObject> pool = CreatePool(poolName, false, 4, float.MaxValue);
            BenchmarkTarget target = new BenchmarkTarget(8);
            BenchmarkObject obj = BenchmarkObject.Create("spawned", target, false, true);
            pool.Register(obj, true);

            RestartCaseMeasure();
            pool.ReleaseAllUnused();
            AssertEqual(pool.Count, 1, "spawned object was released while in use");
            pool.Unspawn(obj);
            pool.ReleaseAllUnused();
            AssertEqual(pool.Count, 0, "spawned object was not released after unspawn");
            StopCaseMeasure();

            DestroyPool(poolName);
        }

        private void RunDestroyRecreate()
        {
            using (s_DestroyMarker.Auto())
            {
                string poolName = MakePoolName("destroy-recreate");

                RestartCaseMeasure();
                for (int i = 0; i < 64; i++)
                {
                    IObjectPool<BenchmarkObject> pool = CreatePool(poolName, false, 8, float.MaxValue);
                    pool.Register(BenchmarkObject.Create("destroy", new BenchmarkTarget(i), false, true), false);
                    AssertEqual(pool.Count, 1, "destroy-recreate register failed");
                    DestroyPool(poolName);
                }
                StopCaseMeasure();
            }
        }

        private void RunCursorReleaseRecovery()
        {
            using (s_CursorRecoveryMarker.Auto())
            {
                string poolName = MakePoolName("cursor-recovery");
                IObjectPool<BenchmarkObject> pool = CreatePool(poolName, false, 4, float.MaxValue);
                BenchmarkObject[] blockedObjects = new BenchmarkObject[3];

                for (int i = 0; i < 4; i++)
                    pool.Register(BenchmarkObject.Create("cursor", new BenchmarkTarget(i), false, true), false);

                for (int i = 0; i < blockedObjects.Length; i++)
                {
                    BenchmarkObject spawned = pool.Spawn("cursor");
                    AssertNotNull(spawned, "cursor setup spawn returned null");
                    if (spawned != null)
                        blockedObjects[i] = spawned;
                }

                BenchmarkObject cursorObject = pool.Spawn("cursor");
                AssertNotNull(cursorObject, "cursor target spawn returned null");
                if (cursorObject != null)
                    pool.Unspawn(cursorObject);

                RestartCaseMeasure();
                pool.ReleaseAllUnused();
                if (blockedObjects[0] != null)
                    pool.Unspawn(blockedObjects[0]);
                BenchmarkObject recovered = pool.Spawn("cursor");
                AssertNotNull(recovered, "cursor did not recover after release");
                if (recovered != null)
                    pool.Unspawn(recovered);
                StopCaseMeasure();

                for (int i = 1; i < blockedObjects.Length; i++)
                    pool.Unspawn(blockedObjects[i]);

                DestroyPool(poolName);
            }
        }

        private void RunLockedReleaseGuard()
        {
            string poolName = MakePoolName("locked");
            IObjectPool<BenchmarkObject> pool = CreatePool(poolName, false, 4, float.MaxValue);
            BenchmarkTarget target = new BenchmarkTarget(6);
            BenchmarkObject obj = BenchmarkObject.Create("locked", target, true, true);
            pool.Register(obj, false);

            RestartCaseMeasure();
            pool.ReleaseAllUnused();

            AssertEqual(pool.Count, 1, "locked object was released");
            StopCaseMeasure();
            DestroyPool(poolName);
        }

        private void RunCustomReleaseGuard()
        {
            string poolName = MakePoolName("custom-release");
            IObjectPool<BenchmarkObject> pool = CreatePool(poolName, false, 4, float.MaxValue);
            BenchmarkTarget target = new BenchmarkTarget(7);
            BenchmarkObject obj = BenchmarkObject.Create("custom", target, false, false);
            pool.Register(obj, false);

            RestartCaseMeasure();
            pool.ReleaseAllUnused();

            AssertEqual(pool.Count, 1, "custom release guard object was released");
            StopCaseMeasure();
            DestroyPool(poolName);
        }

        private void RunExpireRelease()
        {
            using (s_ExpireMarker.Auto())
            {
                string poolName = MakePoolName("expire");
                IObjectPool<BenchmarkObject> pool = CreatePool(poolName, false, objectCount, 0f);
                int count = Math.Max(1, Math.Min(objectCount, 4096));

                for (int i = 0; i < count; i++)
                    pool.Register(BenchmarkObject.Create("expire", new BenchmarkTarget(i), false, true), false);

                RestartCaseMeasure();
                pool.ReleaseAllUnused();
                AssertEqual(pool.Count, 0, "expire release did not clear all unused objects");
                StopCaseMeasure();

                DestroyPool(poolName);
            }
        }

        private void RunReleaseAllUnused()
        {
            using (s_ReleaseMarker.Auto())
            {
                string poolName = MakePoolName("release-all");
                IObjectPool<BenchmarkObject> pool = CreatePool(poolName, false, objectCount, float.MaxValue);

                for (int i = 0; i < objectCount; i++)
                    pool.Register(BenchmarkObject.Create("release", new BenchmarkTarget(i), false, true), false);

                RestartCaseMeasure();
                pool.ReleaseAllUnused();
                AssertEqual(pool.Count, 0, "ReleaseAllUnused did not clear pool");
                StopCaseMeasure();

                DestroyPool(poolName);
            }
        }

        private IObjectPool<BenchmarkObject> CreatePool(string poolName, bool multiSpawn, int capacity, float expireTime)
        {
            if (m_Service.HasObjectPool<BenchmarkObject>(poolName))
                m_Service.DestroyObjectPool<BenchmarkObject>(poolName);

            return m_Service.CreatePool<BenchmarkObject>(
                new ObjectPoolCreateOptions(poolName, multiSpawn, float.MaxValue, capacity, expireTime, 0));
        }

        private void DestroyPool(string poolName)
        {
            if (m_Service.HasObjectPool<BenchmarkObject>(poolName))
                m_Service.DestroyObjectPool<BenchmarkObject>(poolName);
        }

        private static string MakePoolName(string suffix)
        {
            using (var builder = ZString.CreateStringBuilder())
            {
                builder.Append("ObjectPoolBenchmark.");
                builder.Append(suffix);
                return builder.ToString();
            }
        }

        private static string MakeIndexedName(string prefix, int index)
        {
            using (var builder = ZString.CreateStringBuilder())
            {
                builder.Append(prefix);
                builder.Append('.');
                builder.Append(index);
                return builder.ToString();
            }
        }

        private void AssertNotNull(object value, string message)
        {
            if (value != null)
                return;

            m_FailCount++;
            Debug.LogError(message);
        }

        private void AssertReference(object actual, object expected, string message)
        {
            if (ReferenceEquals(actual, expected))
                return;

            m_FailCount++;
            Debug.LogError(message);
        }

        private void AssertEqual(int actual, int expected, string message)
        {
            if (actual == expected)
                return;

            m_FailCount++;
            Debug.LogError(BuildLog(message, " actual=", actual, ", expected=", expected));
        }

        private static string BuildLog(object a, string b, object c, string d,object e)
        {
            using (var builder = ZString.CreateStringBuilder())
            {
                builder.Append(a);
                builder.Append(b);
                builder.Append(c);
                builder.Append(d);
                builder.Append(e);
                return builder.ToString();
            }
        }

        private static string BuildLog(string a, object b, string c, object d)
        {
            using (var builder = ZString.CreateStringBuilder())
            {
                builder.Append(a);
                builder.Append(b);
                builder.Append(c);
                builder.Append(d);
                return builder.ToString();
            }
        }

        private static string BuildLog(string a, object b, string c, object d, string e, object f)
        {
            using (var builder = ZString.CreateStringBuilder())
            {
                builder.Append(a);
                builder.Append(b);
                builder.Append(c);
                builder.Append(d);
                builder.Append(e);
                builder.Append(f);
                return builder.ToString();
            }
        }

        private sealed class BenchmarkTarget
        {
            public readonly int Id;

            public BenchmarkTarget(int id)
            {
                Id = id;
            }
        }

        private sealed class BenchmarkObject : ObjectBase<BenchmarkTarget>
        {
            private bool m_CustomCanReleaseFlag;

            public override bool CustomCanReleaseFlag => m_CustomCanReleaseFlag;

            public BenchmarkObject()
            {
            }

            public static BenchmarkObject Create(string name, BenchmarkTarget target, bool locked, bool customCanReleaseFlag)
            {
                BenchmarkObject obj = MemoryPool.Acquire<BenchmarkObject>();
                obj.Initialize(name, target, locked);
                obj.m_CustomCanReleaseFlag = customCanReleaseFlag;
                return obj;
            }

            protected internal override void Release(bool isShutdown)
            {
            }

            public override void Clear()
            {
                base.Clear();
                m_CustomCanReleaseFlag = true;
            }
        }
    }
}

#endif
