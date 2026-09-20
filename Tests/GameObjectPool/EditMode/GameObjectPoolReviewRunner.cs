using System;
using System.IO;
using System.Linq;
using AlicizaX;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace AlicizaX.GameObjectPool.Tests
{
    [InitializeOnLoad]
    internal static class GameObjectPoolReviewRunner
    {
        [Serializable]
        private sealed class Request
        {
            public string mode, output;
            public string[] assemblies, tests;
        }
        private const string RequestPath = "Temp/GameObjectPoolReview.request.json";
        private const string OutputKey = "GameObjectPoolReview.Output";
        private const string RefreshKey = "GameObjectPoolReview.Refresh";
        private static readonly TestRunnerApi Api;
        static GameObjectPoolReviewRunner()
        {
            Api = ScriptableObject.CreateInstance<TestRunnerApi>();
            Api.RegisterCallbacks(new Callbacks());
            EditorApplication.update += Poll;
        }
        private static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
            var request = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath));
            string stamp = request.output + "|" + File.GetLastWriteTimeUtc(RequestPath).Ticks;
            if (SessionState.GetString(RefreshKey, "") != stamp)
            {
                SessionState.SetString(RefreshKey, stamp);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                return;
            }
            string root = "Packages/com.alicizax.unity.framework/";
            DateTime latestSource = Directory.GetFiles(root + "Runtime/Modules/GameObjectPool", "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(root + "Tests/GameObjectPool", "*.cs", SearchOption.AllDirectories))
                .Append(root + "Runtime/_InternalVisibleTo.cs").Max(File.GetLastWriteTimeUtc);
            DateTime latestAssembly = new[] { typeof(GameObjectPoolService).Assembly, typeof(GameObjectPoolReviewRunner).Assembly }
                .Max(a => File.GetLastWriteTimeUtc(a.Location));
            if (latestAssembly < latestSource)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                return;
            }
            File.Delete(RequestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(request.output));
            File.WriteAllText(request.output + ".build", string.Join("\n", new[] { typeof(GameObjectPoolService).Assembly, typeof(GameObjectPoolReviewRunner).Assembly }
                .Select(a => a.GetName().Name + "," + a.ManifestModule.ModuleVersionId + "," + File.GetLastWriteTimeUtc(a.Location).ToString("O"))));
            SessionState.SetString(OutputKey, request.output);
            Api.Execute(new ExecutionSettings(new Filter { testMode = request.mode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode, assemblyNames = request.assemblies, testNames = request.tests }));
        }
        private sealed class Callbacks : IErrorCallbacks
        {
            public void OnError(string message)
            {
                string output = SessionState.GetString(OutputKey, "");
                if (output != "") File.WriteAllText(output + ".error", message);
                SessionState.EraseString(OutputKey);
                SessionState.EraseString(RefreshKey);
            }

            public void RunStarted(ITestAdaptor test)
            {
                string output = SessionState.GetString(OutputKey, "");
                if (output != "") File.WriteAllText(output + ".cases", "");
            }
            public void TestStarted(ITestAdaptor test)
            {
                string output = SessionState.GetString(OutputKey, "");
                if (output != "") File.WriteAllText(output + ".progress", test.FullName);
            }
            public void TestFinished(ITestResultAdaptor test)
            {
                string output = SessionState.GetString(OutputKey, "");
                if (output == "" || test.Test.IsSuite) return;
                File.AppendAllText(output + ".cases", test.FullName + "\t" + test.ResultState + "\n");
                if (test.FailCount > 0) File.AppendAllText(output + ".failures.xmlfrag", test.ToXml().OuterXml + "\n");
            }
            public void RunFinished(ITestResultAdaptor result)
            {
                string output = SessionState.GetString(OutputKey, "");
                if (output == "") return;
                File.WriteAllText(output, result.ToXml().OuterXml);
                File.WriteAllText(output + ".progress", "Finished: " + result.ResultState);
                SessionState.EraseString(OutputKey);
                SessionState.EraseString(RefreshKey);
            }
        }
    }
}
