using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace AlicizaX.ObjectPool.Tests
{
    [InitializeOnLoad]
    internal static class ObjectPoolReviewRunner
    {
        [Serializable]
        private sealed class Request
        {
            public string mode, output;
            public string[] assemblies, tests;
        }
        private const string RequestPath = "Temp/ObjectPoolReview.request.json";
        private const string OutputKey = "ObjectPoolReview.Output";
        private const string RefreshKey = "ObjectPoolReview.Refresh";
        private static readonly TestRunnerApi Api;
        static ObjectPoolReviewRunner()
        {
            Api = ScriptableObject.CreateInstance<TestRunnerApi>();
            Api.RegisterCallbacks(new Callbacks());
            EditorApplication.update += Poll;
        }
        private static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
            var request = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath));
            if (SessionState.GetString(RefreshKey, "") != request.output)
            {
                SessionState.SetString(RefreshKey, request.output);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                return;
            }
            string root = "Packages/com.alicizax.unity.framework/";
            DateTime latestSource = Directory.GetFiles(root + "Runtime/Modules/ObjectPool", "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(root + "Tests/ObjectPool", "*.cs", SearchOption.AllDirectories))
                .Append(root + "Runtime/_InternalVisibleTo.cs").Max(File.GetLastWriteTimeUtc);
            DateTime latestAssembly = new[] { typeof(ObjectPoolService).Assembly, typeof(PoolObject).Assembly, typeof(ObjectPoolReviewRunner).Assembly }
                .Max(a => File.GetLastWriteTimeUtc(a.Location));
            if (latestAssembly < latestSource) return;
            File.Delete(RequestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(request.output));
            File.WriteAllText(request.output + ".build", string.Join("\n", new[] { typeof(ObjectPoolService).Assembly, typeof(PoolObject).Assembly, typeof(ObjectPoolReviewRunner).Assembly }
                .Select(a => a.GetName().Name + "," + a.ManifestModule.ModuleVersionId + "," + File.GetLastWriteTimeUtc(a.Location).ToString("O"))));
            SessionState.SetString(OutputKey, request.output);
            Api.Execute(new ExecutionSettings(new Filter { testMode = request.mode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode, assemblyNames = request.assemblies, testNames = request.tests }));
        }
        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor test) { }
            public void TestStarted(ITestAdaptor test)
            {
                string output = SessionState.GetString(OutputKey, "");
                if (output != "") File.WriteAllText(output + ".progress", test.FullName);
            }
            public void TestFinished(ITestResultAdaptor test) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                string output = SessionState.GetString(OutputKey, "");
                if (output == "") return;
                File.WriteAllText(output, result.ToXml().OuterXml);
                File.WriteAllText(output + ".progress", "Finished: " + result.ResultState);
                SessionState.EraseString(OutputKey);
            }
        }
    }
}
