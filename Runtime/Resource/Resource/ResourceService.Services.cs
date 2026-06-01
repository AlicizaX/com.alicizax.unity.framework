using System.IO;
using System.Text;
using Cysharp.Text;
using UnityEngine;
using YooAsset;

namespace AlicizaX.Resource.Runtime
{
    /// <summary>
    /// 远端资源地址查询服务类
    /// </summary>
    class RemoteServices : IRemoteServices
    {
        private readonly string _defaultHostServer;
        private readonly string _fallbackHostServer;

        public RemoteServices(string defaultHostServer, string fallbackHostServer)
        {
            _defaultHostServer = defaultHostServer;
            _fallbackHostServer = fallbackHostServer;
        }

        string IRemoteServices.GetRemoteMainURL(string fileName)
        {
            return ZString.Concat(_defaultHostServer, "/", fileName);
        }

        string IRemoteServices.GetRemoteFallbackURL(string fileName)
        {
            return ZString.Concat(_fallbackHostServer, "/", fileName);
        }
    }

    /// <summary>
    /// 资源文件流加载解密类
    /// </summary>
    class FileStreamDecryption : IDecryptionServices
    {
        /// <summary>
        /// 同步方式获取解密的资源包对象
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundle(DecryptFileInfo fileInfo)
        {
            BundleStream bundleStream = new BundleStream(fileInfo.FileLoadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.ManagedStream = bundleStream;
            decryptResult.Result = AssetBundle.LoadFromStream(bundleStream, fileInfo.FileLoadCRC, GetManagedReadBufferSize());
            return decryptResult;
        }

        /// <summary>
        /// 异步方式获取解密的资源包对象
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundleAsync(DecryptFileInfo fileInfo)
        {
            BundleStream bundleStream = new BundleStream(fileInfo.FileLoadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.ManagedStream = bundleStream;
            decryptResult.CreateRequest = AssetBundle.LoadFromStreamAsync(bundleStream, fileInfo.FileLoadCRC, GetManagedReadBufferSize());
            return decryptResult;
        }

        /// <summary>
        /// 后备方式获取解密的资源包
        /// 注意：当正常解密方法失败后，会触发后备加载！
        /// 说明：建议通过LoadFromMemory()方法加载资源包作为保底机制。
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundleFallback(DecryptFileInfo fileInfo)
        {
            byte[] fileData = ((IDecryptionServices)this).ReadFileData(fileInfo);
            var assetBundle = AssetBundle.LoadFromMemory(fileData, fileInfo.FileLoadCRC);
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.Result = assetBundle;
            return decryptResult;
        }

        /// <summary>
        /// 获取解密的字节数据
        /// </summary>
        byte[] IDecryptionServices.ReadFileData(DecryptFileInfo fileInfo)
        {
            byte[] fileData = File.ReadAllBytes(fileInfo.FileLoadPath);
            for (int i = 0; i < fileData.Length; i++)
            {
                fileData[i] ^= BundleStream.KEY;
            }

            return fileData;
        }

        /// <summary>
        /// 获取解密的文本数据
        /// </summary>
        string IDecryptionServices.ReadFileText(DecryptFileInfo fileInfo)
        {
            byte[] fileData = ((IDecryptionServices)this).ReadFileData(fileInfo);
            return Encoding.UTF8.GetString(fileData);
        }

        private static uint GetManagedReadBufferSize()
        {
            return 1024;
        }
    }

    /// <summary>
    /// 资源文件偏移加载解密类
    /// </summary>
    class FileOffsetDecryption : IDecryptionServices
    {
        /// <summary>
        /// 同步方式获取解密的资源包对象
        /// 注意：加载流对象在资源包对象释放的时候会自动释放
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundle(DecryptFileInfo fileInfo)
        {
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.ManagedStream = null;
            decryptResult.Result = AssetBundle.LoadFromFile(fileInfo.FileLoadPath, fileInfo.FileLoadCRC, GetFileOffset());
            return decryptResult;
        }

        /// <summary>
        /// 异步方式获取解密的资源包对象
        /// 注意：加载流对象在资源包对象释放的时候会自动释放
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundleAsync(DecryptFileInfo fileInfo)
        {
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.ManagedStream = null;
            decryptResult.CreateRequest = AssetBundle.LoadFromFileAsync(fileInfo.FileLoadPath, fileInfo.FileLoadCRC, GetFileOffset());
            return decryptResult;
        }

        /// <summary>
        /// 后备方式获取解密的资源包对象
        /// </summary>
        DecryptResult IDecryptionServices.LoadAssetBundleFallback(DecryptFileInfo fileInfo)
        {
            byte[] fileData = ((IDecryptionServices)this).ReadFileData(fileInfo);
            DecryptResult decryptResult = new DecryptResult();
            decryptResult.Result = AssetBundle.LoadFromMemory(fileData, fileInfo.FileLoadCRC);
            return decryptResult;
        }

        /// <summary>
        /// 获取解密的字节数据
        /// </summary>
        byte[] IDecryptionServices.ReadFileData(DecryptFileInfo fileInfo)
        {
            byte[] fileData = File.ReadAllBytes(fileInfo.FileLoadPath);
            ulong fileOffset = GetFileOffset();
            if ((ulong)fileData.Length <= fileOffset)
            {
                return System.Array.Empty<byte>();
            }

            int outputLength = fileData.Length - (int)fileOffset;
            byte[] output = new byte[outputLength];
            System.Buffer.BlockCopy(fileData, (int)fileOffset, output, 0, outputLength);
            return output;
        }

        /// <summary>
        /// 获取解密的文本数据
        /// </summary>
        string IDecryptionServices.ReadFileText(DecryptFileInfo fileInfo)
        {
            byte[] fileData = ((IDecryptionServices)this).ReadFileData(fileInfo);
            return Encoding.UTF8.GetString(fileData);
        }

        private static ulong GetFileOffset()
        {
            return 32;
        }
    }


    public class BundleStream : FileStream
    {
        public const byte KEY = 64;

        public BundleStream(string path, FileMode mode, FileAccess access, FileShare share) : base(path, mode, access, share)
        {
        }

        public BundleStream(string path, FileMode mode) : base(path, mode)
        {
        }

        public override int Read(byte[] array, int offset, int count)
        {
            var index = base.Read(array, offset, count);
            int end = offset + index;
            for (int i = offset; i < end; i++)
            {
                array[i] ^= KEY;
            }

            return index;
        }
    }
}
