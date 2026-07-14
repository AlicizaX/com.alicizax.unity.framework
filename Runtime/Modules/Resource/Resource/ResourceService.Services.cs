using System.IO;
using System.Collections.Generic;
using Cysharp.Text;
using YooAsset;

namespace AlicizaX.Resource.Runtime
{
    /// <summary>
    /// 远端资源地址查询服务类
    /// </summary>
    class RemoteServices : IRemoteService
    {
        private readonly string _defaultHostServer;
        private readonly string _fallbackHostServer;

        public RemoteServices(string defaultHostServer, string fallbackHostServer)
        {
            _defaultHostServer = defaultHostServer;
            _fallbackHostServer = fallbackHostServer;
        }

        IReadOnlyList<string> IRemoteService.GetRemoteUrls(string fileName)
        {
            if (string.IsNullOrEmpty(_fallbackHostServer))
            {
                return new[] { ZString.Concat(_defaultHostServer, "/", fileName) };
            }

            return new[]
            {
                ZString.Concat(_defaultHostServer, "/", fileName),
                ZString.Concat(_fallbackHostServer, "/", fileName)
            };
        }
    }

    /// <summary>
    /// 资源文件流加载解密类
    /// </summary>
    class FileStreamDecryption : IBundleStreamDecryptor, IBundleMemoryDecryptor
    {
        /// <summary>
        /// 同步方式获取解密的资源包对象
        /// </summary>
        Stream IBundleStreamDecryptor.CreateDecryptionStream(BundleDecryptArgs args)
        {
            return new BundleStream(args.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        /// <summary>
        /// 异步方式获取解密的资源包对象
        /// </summary>
        int IBundleStreamDecryptor.GetBufferSize(BundleDecryptArgs args)
        {
            return 1024;
        }

        /// <summary>
        /// 后备方式获取解密的资源包
        /// 注意：当正常解密方法失败后，会触发后备加载！
        /// 说明：建议通过LoadFromMemory()方法加载资源包作为保底机制。
        /// </summary>
        byte[] IBundleMemoryDecryptor.GetDecryptedData(BundleDecryptArgs args)
        {
            byte[] fileData = args.FileData ?? File.ReadAllBytes(args.FilePath);
            for (int i = 0; i < fileData.Length; i++)
            {
                fileData[i] ^= BundleStream.KEY;
            }

            return fileData;
        }
    }

    /// <summary>
    /// 资源文件偏移加载解密类
    /// </summary>
    class FileOffsetDecryption : IBundleOffsetDecryptor, IBundleMemoryDecryptor
    {
        /// <summary>
        /// 同步方式获取解密的资源包对象
        /// 注意：加载流对象在资源包对象释放的时候会自动释放
        /// </summary>
        long IBundleOffsetDecryptor.GetFileOffset(BundleDecryptArgs args)
        {
            return (long)GetFileOffset();
        }

        /// <summary>
        /// 异步方式获取解密的资源包对象
        /// 注意：加载流对象在资源包对象释放的时候会自动释放
        /// </summary>
        byte[] IBundleMemoryDecryptor.GetDecryptedData(BundleDecryptArgs args)
        {
            byte[] fileData = args.FileData ?? File.ReadAllBytes(args.FilePath);
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
