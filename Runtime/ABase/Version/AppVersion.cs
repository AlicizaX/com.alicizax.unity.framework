using UnityEngine;

namespace AlicizaX
{
    /// <summary>
    /// 版本号类。
    /// </summary>
    public static partial class AppVersion
    {
        private const string GameFrameworkVersionString = "1.0.0";

        /// <summary>
        /// 获取游戏框架版本号。
        /// </summary>
        public static string GameFrameworkVersion
        {
            get { return GameFrameworkVersionString; }
        }

        /// <summary>
        /// 获取游戏版本号。
        /// </summary>
        public static string GameVersion
        {
            get { return Application.version; }
        }
    }
}
