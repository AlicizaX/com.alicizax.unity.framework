namespace AlicizaX.Resource.Runtime
{
    /// <summary>
    /// 检查资源是否存在的结果。
    /// </summary>
    public enum HasAssetResult : byte
    {
        /// <summary>
        /// 资源不存在。
        /// </summary>
        NotExist = 0,

        /// <summary>
        /// 资源需要从远端更新下载。
        /// </summary>
        AssetOnline,

        /// <summary>
        /// 存在资源且存储在磁盘上。
        /// </summary>
        AssetOnDisk,
    }
}