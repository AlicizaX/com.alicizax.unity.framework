using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using System;
using UnityEditor;
#endif

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioNativeAssetSetup : IPrebuildSetup, IPostBuildCleanup
    {
        internal const string SourceFolder = "Assets/__FrameworkAudioTestSources";
        private const string BundleFolder = "Assets/StreamingAssets/__FrameworkAudioTestBundles";
        internal static string BundlePath => Path.Combine(Application.streamingAssetsPath,
            "__FrameworkAudioTestBundles", Application.isEditor ? "editor" : "player", "audio-acceptance");

        public void Setup()
        {
#if UNITY_EDITOR
            Assert.That(Directory.Exists(SourceFolder), Is.False, "Audio fixture source path is already occupied.");
            Assert.That(Directory.Exists(BundleFolder), Is.False, "Audio fixture bundle path is already occupied.");
            SessionState.SetBool(BundleFolder, true);
            try
            {
                Directory.CreateDirectory(SourceFolder);
                var names = new string[3];
                for (int mode = 0; mode < names.Length; mode++)
                {
                    names[mode] = SourceFolder + "/audit-native-" + mode + ".wav";
                    WriteWave(names[mode], 44100 << mode);
                    AssetDatabase.ImportAsset(names[mode], ImportAssetOptions.ForceSynchronousImport);
                    var importer = (AudioImporter)AssetImporter.GetAtPath(names[mode]);
                    var settings = importer.defaultSampleSettings;
                    settings.loadType = (AudioClipLoadType)mode;
                    settings.compressionFormat = mode == 0 ? AudioCompressionFormat.PCM : AudioCompressionFormat.Vorbis;
                    settings.quality = 0.7f;
                    settings.preloadAudioData = true;
                    importer.defaultSampleSettings = settings;
                    importer.loadInBackground = false;
                    importer.SaveAndReimport();
                }
                BuildTarget host = Application.platform == RuntimePlatform.OSXEditor ? BuildTarget.StandaloneOSX
                    : Application.platform == RuntimePlatform.LinuxEditor ? BuildTarget.StandaloneLinux64
                    : BuildTarget.StandaloneWindows64;
                Build(names, "editor", host);
                Build(names, "player", EditorUserBuildSettings.activeBuildTarget);
                AssetDatabase.Refresh();
            }
            catch
            {
                Cleanup();
                throw;
            }
#endif
        }

        public void Cleanup()
        {
#if UNITY_EDITOR
            if (!SessionState.GetBool(BundleFolder, false)) return;
            try { if (Directory.Exists(SourceFolder)) Assert.That(AssetDatabase.DeleteAsset(SourceFolder), Is.True); }
            finally
            {
                if (Directory.Exists(BundleFolder)) Assert.That(AssetDatabase.DeleteAsset(BundleFolder), Is.True);
                SessionState.EraseBool(BundleFolder);
            }
#endif
        }

#if UNITY_EDITOR
        private static void Build(string[] names, string targetFolder, BuildTarget target)
        {
            string output = Path.Combine(BundleFolder, targetFolder);
            Directory.CreateDirectory(output);
            var build = new AssetBundleBuild { assetBundleName = "audio-acceptance", assetNames = names };
            var manifest = BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression, target);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(output, "audio-acceptance")), Is.True);
            Debug.Log("NATIVE_FIXTURE," + target + ",DecompressOnLoad,CompressedInMemory,Streaming");
        }

        private static void WriteWave(string path, int samples)
        {
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(44100); writer.Write(88200);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
            for (int i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * Math.PI * 880 / 44100) * 8192));
        }
#endif
    }
}
