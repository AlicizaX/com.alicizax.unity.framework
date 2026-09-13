using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioNativeAssetBuildTests
    {
        [Test]
        public void BuildImportedPcmCompressedAndStreamingAudioFixtures()
        {
            const string folder = "Assets/AudioAuditGenerated";
            Directory.CreateDirectory(folder);
            string output = Path.Combine(Application.streamingAssetsPath, "AudioAudit");
            Directory.CreateDirectory(output);
            var names = new string[3];
            for (int mode = 0; mode < names.Length; mode++)
            {
                names[mode] = folder + "/audit-native-" + mode + ".wav";
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
                Assert.That(importer.defaultSampleSettings.loadType, Is.EqualTo((AudioClipLoadType)mode));
            }
            var build = new AssetBundleBuild { assetBundleName = "audio-acceptance", assetNames = names };
            var manifest = BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression, EditorUserBuildSettings.activeBuildTarget);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(output, "audio-acceptance")), Is.True);
            TestContext.WriteLine("NATIVE_FIXTURE," + EditorUserBuildSettings.activeBuildTarget + ",DecompressOnLoad,CompressedInMemory,Streaming");
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
    }
}
