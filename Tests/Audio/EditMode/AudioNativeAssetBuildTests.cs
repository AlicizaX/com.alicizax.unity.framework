using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.Audio.Tests
{
    [PrebuildSetup(typeof(AudioNativeAssetSetup))]
    [PostBuildCleanup(typeof(AudioNativeAssetSetup))]
    public sealed class AudioNativeAssetBuildTests
    {
        [Test]
        public void BuildImportedPcmCompressedAndStreamingAudioFixtures()
        {
            for (int mode = 0; mode < 3; mode++)
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(AudioNativeAssetSetup.SourceFolder + "/audit-native-" + mode + ".wav");
                Assert.That(importer, Is.Not.Null);
                Assert.That(importer.defaultSampleSettings.loadType, Is.EqualTo((AudioClipLoadType)mode));
            }
            Assert.That(File.Exists(AudioNativeAssetSetup.BundlePath), Is.True);
        }
    }
}
