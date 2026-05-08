using System;
using System.IO;
using ICSharpCode.SharpZipLib.Checksums;
using ICSharpCode.SharpZipLib.Zip;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace AlicizaX
{
    public static partial class Utility
    {
        /// <summary>
        /// Zip 压缩相关的实用函数。
        /// </summary>
        public static class Zip
        {
            private static readonly Crc32 s_Crc32 = new Crc32();

            public static bool CompressDirectoryToStream(string folderToZip, Stream stream, string password = null)
            {
                return CompressDirectoryToZipStream(folderToZip, stream, password) != null;
            }

            public static ZipOutputStream CompressDirectoryToZipStream(string folderToZip, Stream stream, string password = null)
            {
                if (!Directory.Exists(folderToZip) || stream == null)
                {
                    return null;
                }

                ZipOutputStream zipStream = new ZipOutputStream(stream);
                zipStream.SetLevel(6);
                if (!string.IsNullOrEmpty(password))
                {
                    zipStream.Password = password;
                }

                if (!CompressDirectoryInternal(folderToZip, zipStream, string.Empty))
                {
                    zipStream.Dispose();
                    return null;
                }

                zipStream.Finish();
                return zipStream;
            }

            public static bool CompressDirectory(string folderToZip, string zipFile, string password = null)
            {
                if (string.IsNullOrEmpty(folderToZip) || string.IsNullOrEmpty(zipFile))
                {
                    return false;
                }

                folderToZip = folderToZip.TrimEnd(System.IO.Path.DirectorySeparatorChar, '/');
                using (FileStream writeStream = new FileStream(zipFile, FileMode.Create, FileAccess.Write, FileShare.Write))
                {
                    return CompressDirectoryToZipStream(folderToZip, writeStream, password) != null;
                }
            }

            public static bool CompressFile(string fileToZip, string zipFile, string password = null)
            {
                if (!System.IO.File.Exists(fileToZip) || string.IsNullOrEmpty(zipFile))
                {
                    return false;
                }

                byte[] buffer = System.IO.File.ReadAllBytes(fileToZip);
                using (FileStream writeStream = System.IO.File.Create(zipFile))
                using (ZipOutputStream zipStream = new ZipOutputStream(writeStream))
                {
                    if (!string.IsNullOrEmpty(password))
                    {
                        zipStream.Password = password;
                    }

                    ZipEntry entry = new ZipEntry(System.IO.Path.GetFileName(fileToZip))
                    {
                        DateTime = DateTime.Now,
                        Size = buffer.Length
                    };

                    s_Crc32.Reset();
                    s_Crc32.Update(buffer);
                    entry.Crc = s_Crc32.Value;

                    zipStream.PutNextEntry(entry);
                    zipStream.SetLevel(Deflater.BEST_COMPRESSION);
                    zipStream.Write(buffer, 0, buffer.Length);
                }

                return true;
            }

            public static bool DecompressFile(string fileToUnzip, string zipFolder, string password = null)
            {
                if (!System.IO.File.Exists(fileToUnzip) || string.IsNullOrEmpty(zipFolder))
                {
                    return false;
                }

                Directory.CreateDirectory(zipFolder);

                using (ZipInputStream zipStream = new ZipInputStream(System.IO.File.OpenRead(fileToUnzip)))
                {
                    if (!string.IsNullOrEmpty(password))
                    {
                        zipStream.Password = password;
                    }

                    ZipEntry zipEntry;
                    while ((zipEntry = zipStream.GetNextEntry()) != null)
                    {
                        if (zipEntry.IsDirectory || string.IsNullOrEmpty(zipEntry.Name))
                        {
                            continue;
                        }

                        string relativePath = zipEntry.Name.Replace('/', System.IO.Path.DirectorySeparatorChar);
                        string outputPath = System.IO.Path.Combine(zipFolder, relativePath);
                        string directory = System.IO.Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        using (FileStream fileStream = System.IO.File.Create(outputPath))
                        {
                            zipStream.CopyTo(fileStream);
                        }
                    }
                }

                return true;
            }

            public static byte[] Compress(byte[] content)
            {
                if (content == null)
                {
                    return null;
                }

                Deflater compressor = new Deflater();
                compressor.SetLevel(Deflater.BEST_COMPRESSION);
                compressor.SetInput(content);
                compressor.Finish();

                using (MemoryStream output = new MemoryStream(content.Length))
                {
                    byte[] buffer = new byte[4096];
                    while (!compressor.IsFinished)
                    {
                        int read = compressor.Deflate(buffer);
                        output.Write(buffer, 0, read);
                    }

                    return output.ToArray();
                }
            }

            public static byte[] Decompress(byte[] content)
            {
                return content == null ? null : Decompress(content, 0, content.Length);
            }

            public static byte[] Decompress(byte[] content, int offset, int count)
            {
                if (content == null)
                {
                    return null;
                }

                Inflater decompressor = new Inflater();
                decompressor.SetInput(content, offset, count);
                using (MemoryStream output = new MemoryStream(content.Length))
                {
                    byte[] buffer = new byte[4096];
                    while (!decompressor.IsFinished)
                    {
                        int read = decompressor.Inflate(buffer);
                        output.Write(buffer, 0, read);
                    }

                    return output.ToArray();
                }
            }

            private static bool CompressDirectoryInternal(string folderToZip, ZipOutputStream zipStream, string parentFolderName)
            {
                if (!string.IsNullOrWhiteSpace(parentFolderName))
                {
                    ZipEntry entry = new ZipEntry(parentFolderName + "/");
                    zipStream.PutNextEntry(entry);
                    zipStream.Flush();
                }

                string[] files = Directory.GetFiles(folderToZip);
                foreach (string file in files)
                {
                    byte[] buffer = System.IO.File.ReadAllBytes(file);
                    string path = System.IO.Path.GetFileName(file);
                    if (!string.IsNullOrWhiteSpace(parentFolderName))
                    {
                        path = parentFolderName + System.IO.Path.DirectorySeparatorChar + System.IO.Path.GetFileName(file);
                    }

                    ZipEntry entry = new ZipEntry(path)
                    {
                        DateTime = DateTime.Now,
                        Size = buffer.Length
                    };

                    s_Crc32.Reset();
                    s_Crc32.Update(buffer);
                    entry.Crc = s_Crc32.Value;
                    zipStream.PutNextEntry(entry);
                    zipStream.Write(buffer, 0, buffer.Length);
                }

                string[] folders = Directory.GetDirectories(folderToZip);
                foreach (string folder in folders)
                {
                    string folderName = System.IO.Path.GetFileName(folder);
                    if (!string.IsNullOrWhiteSpace(parentFolderName))
                    {
                        folderName = parentFolderName + System.IO.Path.DirectorySeparatorChar + folderName;
                    }

                    if (!CompressDirectoryInternal(folder, zipStream, folderName))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
