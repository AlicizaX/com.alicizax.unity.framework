using System;
using System.Collections.Generic;
using System.Threading;
using AlicizaX;
using Cysharp.Text;
using Cysharp.Threading.Tasks;

namespace AlicizaX.Localization.Runtime
{
    /// <summary>
    /// 本地化服务实现，负责维护当前语言对应的字符串缓存，并管理配置表的装载与刷新。
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    internal sealed partial class LocalizationService : ServiceBase, ILocalizationService
    {
        /// <summary>
        /// 存储当前语言下可用的本地化键值映射。
        /// </summary>
        private readonly Dictionary<string, string> Dic = new();

        /// <summary>
        /// 记录已被服务跟踪的本地化配置表。
        /// </summary>
        private readonly List<GameLocaizationTable> _trackedTables = new();
        private readonly HashSet<GameLocaizationTable> _trackedTableSet = new();

        /// <summary>
        /// 记录每个配置表当前写入到缓存中的键集合，便于重载时移除旧数据。
        /// </summary>
        private readonly Dictionary<GameLocaizationTable, List<string>> _trackedTableKeys = new();

        /// <summary>
        /// 保存当前启用的语言标识。
        /// </summary>
        private string _language;

        /// <summary>
        /// 获取当前正在使用的语言标识。
        /// </summary>
        public string Language
        {
            get => _language;
        }

        /// <summary>
        /// 请求切换当前语言。
        /// </summary>
        /// <param name="language">要设置或切换到的语言标识。</param>
        public void ChangedLanguage(string language)
        {
            SwitchLanguageAsync(language).Forget();
        }

        /// <summary>
        /// 异步切换当前语言并刷新已跟踪的本地化数据。
        /// </summary>
        /// <param name="language">要设置或切换到的语言标识。</param>
        /// <param name="cancellationToken">用于取消异步切换操作的令牌。</param>
        /// <returns>表示语言切换流程的异步任务。</returns>
        public UniTask SwitchLanguageAsync(string language, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(language) || _language == language)
            {
                return UniTask.CompletedTask;
            }

            _language = language;
            RebuildTrackedTables();
            LocalizationComponent.SaveLanguagePreference(language);
            LocalizationChangeEvent.Publisher(_language);
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 初始化本地化服务并设置当前语言。
        /// </summary>
        /// <param name="language">要设置或切换到的语言标识。</param>
        public void Initialize(string language)
        {
            _language = language;
            LocalizationComponent.SaveLanguagePreference(language);
            Log.Info(ZString.Format("Initializing LocalizationModule :{0}", language));
        }

        /// <summary>
        /// 尝试按键获取未格式化的本地化字符串。
        /// </summary>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="value">输出获取到的原始字符串值。</param>
        /// <returns>如果找到对应键则返回 `true`；否则返回 `false`。</returns>
        public bool TryGetRawString(string key, out string value)
        {
            return Dic.TryGetValue(key, out value);
        }

        /// <summary>
        /// 按键获取本地化字符串。
        /// </summary>
        /// <param name="key">本地化字符串的键。</param>
        /// <returns>返回本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString(string key)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return value;
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T">格式化参数的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg">用于格式化本地化字符串的参数。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T>(string key, T arg)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg);
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg1">用于格式化本地化字符串的参数 1。</param>
        /// <param name="arg2">用于格式化本地化字符串的参数 2。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T1, T2>(string key, T1 arg1, T2 arg2)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg1, arg2);
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <typeparam name="T3">格式化参数 3 的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg1">用于格式化本地化字符串的参数 1。</param>
        /// <param name="arg2">用于格式化本地化字符串的参数 2。</param>
        /// <param name="arg3">用于格式化本地化字符串的参数 3。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T1, T2, T3>(string key, T1 arg1, T2 arg2, T3 arg3)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg1, arg2, arg3);
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <typeparam name="T3">格式化参数 3 的类型。</typeparam>
        /// <typeparam name="T4">格式化参数 4 的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg1">用于格式化本地化字符串的参数 1。</param>
        /// <param name="arg2">用于格式化本地化字符串的参数 2。</param>
        /// <param name="arg3">用于格式化本地化字符串的参数 3。</param>
        /// <param name="arg4">用于格式化本地化字符串的参数 4。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T1, T2, T3, T4>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg1, arg2, arg3, arg4);
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <typeparam name="T3">格式化参数 3 的类型。</typeparam>
        /// <typeparam name="T4">格式化参数 4 的类型。</typeparam>
        /// <typeparam name="T5">格式化参数 5 的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg1">用于格式化本地化字符串的参数 1。</param>
        /// <param name="arg2">用于格式化本地化字符串的参数 2。</param>
        /// <param name="arg3">用于格式化本地化字符串的参数 3。</param>
        /// <param name="arg4">用于格式化本地化字符串的参数 4。</param>
        /// <param name="arg5">用于格式化本地化字符串的参数 5。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T1, T2, T3, T4, T5>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg1, arg2, arg3, arg4, arg5);
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <typeparam name="T3">格式化参数 3 的类型。</typeparam>
        /// <typeparam name="T4">格式化参数 4 的类型。</typeparam>
        /// <typeparam name="T5">格式化参数 5 的类型。</typeparam>
        /// <typeparam name="T6">格式化参数 6 的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg1">用于格式化本地化字符串的参数 1。</param>
        /// <param name="arg2">用于格式化本地化字符串的参数 2。</param>
        /// <param name="arg3">用于格式化本地化字符串的参数 3。</param>
        /// <param name="arg4">用于格式化本地化字符串的参数 4。</param>
        /// <param name="arg5">用于格式化本地化字符串的参数 5。</param>
        /// <param name="arg6">用于格式化本地化字符串的参数 6。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T1, T2, T3, T4, T5, T6>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg1, arg2, arg3, arg4, arg5, arg6);
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <typeparam name="T3">格式化参数 3 的类型。</typeparam>
        /// <typeparam name="T4">格式化参数 4 的类型。</typeparam>
        /// <typeparam name="T5">格式化参数 5 的类型。</typeparam>
        /// <typeparam name="T6">格式化参数 6 的类型。</typeparam>
        /// <typeparam name="T7">格式化参数 7 的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg1">用于格式化本地化字符串的参数 1。</param>
        /// <param name="arg2">用于格式化本地化字符串的参数 2。</param>
        /// <param name="arg3">用于格式化本地化字符串的参数 3。</param>
        /// <param name="arg4">用于格式化本地化字符串的参数 4。</param>
        /// <param name="arg5">用于格式化本地化字符串的参数 5。</param>
        /// <param name="arg6">用于格式化本地化字符串的参数 6。</param>
        /// <param name="arg7">用于格式化本地化字符串的参数 7。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T1, T2, T3, T4, T5, T6, T7>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg1, arg2, arg3, arg4, arg5, arg6, arg7);
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <typeparam name="T3">格式化参数 3 的类型。</typeparam>
        /// <typeparam name="T4">格式化参数 4 的类型。</typeparam>
        /// <typeparam name="T5">格式化参数 5 的类型。</typeparam>
        /// <typeparam name="T6">格式化参数 6 的类型。</typeparam>
        /// <typeparam name="T7">格式化参数 7 的类型。</typeparam>
        /// <typeparam name="T8">格式化参数 8 的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg1">用于格式化本地化字符串的参数 1。</param>
        /// <param name="arg2">用于格式化本地化字符串的参数 2。</param>
        /// <param name="arg3">用于格式化本地化字符串的参数 3。</param>
        /// <param name="arg4">用于格式化本地化字符串的参数 4。</param>
        /// <param name="arg5">用于格式化本地化字符串的参数 5。</param>
        /// <param name="arg6">用于格式化本地化字符串的参数 6。</param>
        /// <param name="arg7">用于格式化本地化字符串的参数 7。</param>
        /// <param name="arg8">用于格式化本地化字符串的参数 8。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T1, T2, T3, T4, T5, T6, T7, T8>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <typeparam name="T3">格式化参数 3 的类型。</typeparam>
        /// <typeparam name="T4">格式化参数 4 的类型。</typeparam>
        /// <typeparam name="T5">格式化参数 5 的类型。</typeparam>
        /// <typeparam name="T6">格式化参数 6 的类型。</typeparam>
        /// <typeparam name="T7">格式化参数 7 的类型。</typeparam>
        /// <typeparam name="T8">格式化参数 8 的类型。</typeparam>
        /// <typeparam name="T9">格式化参数 9 的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg1">用于格式化本地化字符串的参数 1。</param>
        /// <param name="arg2">用于格式化本地化字符串的参数 2。</param>
        /// <param name="arg3">用于格式化本地化字符串的参数 3。</param>
        /// <param name="arg4">用于格式化本地化字符串的参数 4。</param>
        /// <param name="arg5">用于格式化本地化字符串的参数 5。</param>
        /// <param name="arg6">用于格式化本地化字符串的参数 6。</param>
        /// <param name="arg7">用于格式化本地化字符串的参数 7。</param>
        /// <param name="arg8">用于格式化本地化字符串的参数 8。</param>
        /// <param name="arg9">用于格式化本地化字符串的参数 9。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <typeparam name="T3">格式化参数 3 的类型。</typeparam>
        /// <typeparam name="T4">格式化参数 4 的类型。</typeparam>
        /// <typeparam name="T5">格式化参数 5 的类型。</typeparam>
        /// <typeparam name="T6">格式化参数 6 的类型。</typeparam>
        /// <typeparam name="T7">格式化参数 7 的类型。</typeparam>
        /// <typeparam name="T8">格式化参数 8 的类型。</typeparam>
        /// <typeparam name="T9">格式化参数 9 的类型。</typeparam>
        /// <typeparam name="T10">格式化参数 10 的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg1">用于格式化本地化字符串的参数 1。</param>
        /// <param name="arg2">用于格式化本地化字符串的参数 2。</param>
        /// <param name="arg3">用于格式化本地化字符串的参数 3。</param>
        /// <param name="arg4">用于格式化本地化字符串的参数 4。</param>
        /// <param name="arg5">用于格式化本地化字符串的参数 5。</param>
        /// <param name="arg6">用于格式化本地化字符串的参数 6。</param>
        /// <param name="arg7">用于格式化本地化字符串的参数 7。</param>
        /// <param name="arg8">用于格式化本地化字符串的参数 8。</param>
        /// <param name="arg9">用于格式化本地化字符串的参数 9。</param>
        /// <param name="arg10">用于格式化本地化字符串的参数 10。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <typeparam name="T3">格式化参数 3 的类型。</typeparam>
        /// <typeparam name="T4">格式化参数 4 的类型。</typeparam>
        /// <typeparam name="T5">格式化参数 5 的类型。</typeparam>
        /// <typeparam name="T6">格式化参数 6 的类型。</typeparam>
        /// <typeparam name="T7">格式化参数 7 的类型。</typeparam>
        /// <typeparam name="T8">格式化参数 8 的类型。</typeparam>
        /// <typeparam name="T9">格式化参数 9 的类型。</typeparam>
        /// <typeparam name="T10">格式化参数 10 的类型。</typeparam>
        /// <typeparam name="T11">格式化参数 11 的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg1">用于格式化本地化字符串的参数 1。</param>
        /// <param name="arg2">用于格式化本地化字符串的参数 2。</param>
        /// <param name="arg3">用于格式化本地化字符串的参数 3。</param>
        /// <param name="arg4">用于格式化本地化字符串的参数 4。</param>
        /// <param name="arg5">用于格式化本地化字符串的参数 5。</param>
        /// <param name="arg6">用于格式化本地化字符串的参数 6。</param>
        /// <param name="arg7">用于格式化本地化字符串的参数 7。</param>
        /// <param name="arg8">用于格式化本地化字符串的参数 8。</param>
        /// <param name="arg9">用于格式化本地化字符串的参数 9。</param>
        /// <param name="arg10">用于格式化本地化字符串的参数 10。</param>
        /// <param name="arg11">用于格式化本地化字符串的参数 11。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <typeparam name="T3">格式化参数 3 的类型。</typeparam>
        /// <typeparam name="T4">格式化参数 4 的类型。</typeparam>
        /// <typeparam name="T5">格式化参数 5 的类型。</typeparam>
        /// <typeparam name="T6">格式化参数 6 的类型。</typeparam>
        /// <typeparam name="T7">格式化参数 7 的类型。</typeparam>
        /// <typeparam name="T8">格式化参数 8 的类型。</typeparam>
        /// <typeparam name="T9">格式化参数 9 的类型。</typeparam>
        /// <typeparam name="T10">格式化参数 10 的类型。</typeparam>
        /// <typeparam name="T11">格式化参数 11 的类型。</typeparam>
        /// <typeparam name="T12">格式化参数 12 的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg1">用于格式化本地化字符串的参数 1。</param>
        /// <param name="arg2">用于格式化本地化字符串的参数 2。</param>
        /// <param name="arg3">用于格式化本地化字符串的参数 3。</param>
        /// <param name="arg4">用于格式化本地化字符串的参数 4。</param>
        /// <param name="arg5">用于格式化本地化字符串的参数 5。</param>
        /// <param name="arg6">用于格式化本地化字符串的参数 6。</param>
        /// <param name="arg7">用于格式化本地化字符串的参数 7。</param>
        /// <param name="arg8">用于格式化本地化字符串的参数 8。</param>
        /// <param name="arg9">用于格式化本地化字符串的参数 9。</param>
        /// <param name="arg10">用于格式化本地化字符串的参数 10。</param>
        /// <param name="arg11">用于格式化本地化字符串的参数 11。</param>
        /// <param name="arg12">用于格式化本地化字符串的参数 12。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <typeparam name="T3">格式化参数 3 的类型。</typeparam>
        /// <typeparam name="T4">格式化参数 4 的类型。</typeparam>
        /// <typeparam name="T5">格式化参数 5 的类型。</typeparam>
        /// <typeparam name="T6">格式化参数 6 的类型。</typeparam>
        /// <typeparam name="T7">格式化参数 7 的类型。</typeparam>
        /// <typeparam name="T8">格式化参数 8 的类型。</typeparam>
        /// <typeparam name="T9">格式化参数 9 的类型。</typeparam>
        /// <typeparam name="T10">格式化参数 10 的类型。</typeparam>
        /// <typeparam name="T11">格式化参数 11 的类型。</typeparam>
        /// <typeparam name="T12">格式化参数 12 的类型。</typeparam>
        /// <typeparam name="T13">格式化参数 13 的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg1">用于格式化本地化字符串的参数 1。</param>
        /// <param name="arg2">用于格式化本地化字符串的参数 2。</param>
        /// <param name="arg3">用于格式化本地化字符串的参数 3。</param>
        /// <param name="arg4">用于格式化本地化字符串的参数 4。</param>
        /// <param name="arg5">用于格式化本地化字符串的参数 5。</param>
        /// <param name="arg6">用于格式化本地化字符串的参数 6。</param>
        /// <param name="arg7">用于格式化本地化字符串的参数 7。</param>
        /// <param name="arg8">用于格式化本地化字符串的参数 8。</param>
        /// <param name="arg9">用于格式化本地化字符串的参数 9。</param>
        /// <param name="arg10">用于格式化本地化字符串的参数 10。</param>
        /// <param name="arg11">用于格式化本地化字符串的参数 11。</param>
        /// <param name="arg12">用于格式化本地化字符串的参数 12。</param>
        /// <param name="arg13">用于格式化本地化字符串的参数 13。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <typeparam name="T3">格式化参数 3 的类型。</typeparam>
        /// <typeparam name="T4">格式化参数 4 的类型。</typeparam>
        /// <typeparam name="T5">格式化参数 5 的类型。</typeparam>
        /// <typeparam name="T6">格式化参数 6 的类型。</typeparam>
        /// <typeparam name="T7">格式化参数 7 的类型。</typeparam>
        /// <typeparam name="T8">格式化参数 8 的类型。</typeparam>
        /// <typeparam name="T9">格式化参数 9 的类型。</typeparam>
        /// <typeparam name="T10">格式化参数 10 的类型。</typeparam>
        /// <typeparam name="T11">格式化参数 11 的类型。</typeparam>
        /// <typeparam name="T12">格式化参数 12 的类型。</typeparam>
        /// <typeparam name="T13">格式化参数 13 的类型。</typeparam>
        /// <typeparam name="T14">格式化参数 14 的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg1">用于格式化本地化字符串的参数 1。</param>
        /// <param name="arg2">用于格式化本地化字符串的参数 2。</param>
        /// <param name="arg3">用于格式化本地化字符串的参数 3。</param>
        /// <param name="arg4">用于格式化本地化字符串的参数 4。</param>
        /// <param name="arg5">用于格式化本地化字符串的参数 5。</param>
        /// <param name="arg6">用于格式化本地化字符串的参数 6。</param>
        /// <param name="arg7">用于格式化本地化字符串的参数 7。</param>
        /// <param name="arg8">用于格式化本地化字符串的参数 8。</param>
        /// <param name="arg9">用于格式化本地化字符串的参数 9。</param>
        /// <param name="arg10">用于格式化本地化字符串的参数 10。</param>
        /// <param name="arg11">用于格式化本地化字符串的参数 11。</param>
        /// <param name="arg12">用于格式化本地化字符串的参数 12。</param>
        /// <param name="arg13">用于格式化本地化字符串的参数 13。</param>
        /// <param name="arg14">用于格式化本地化字符串的参数 14。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <typeparam name="T3">格式化参数 3 的类型。</typeparam>
        /// <typeparam name="T4">格式化参数 4 的类型。</typeparam>
        /// <typeparam name="T5">格式化参数 5 的类型。</typeparam>
        /// <typeparam name="T6">格式化参数 6 的类型。</typeparam>
        /// <typeparam name="T7">格式化参数 7 的类型。</typeparam>
        /// <typeparam name="T8">格式化参数 8 的类型。</typeparam>
        /// <typeparam name="T9">格式化参数 9 的类型。</typeparam>
        /// <typeparam name="T10">格式化参数 10 的类型。</typeparam>
        /// <typeparam name="T11">格式化参数 11 的类型。</typeparam>
        /// <typeparam name="T12">格式化参数 12 的类型。</typeparam>
        /// <typeparam name="T13">格式化参数 13 的类型。</typeparam>
        /// <typeparam name="T14">格式化参数 14 的类型。</typeparam>
        /// <typeparam name="T15">格式化参数 15 的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg1">用于格式化本地化字符串的参数 1。</param>
        /// <param name="arg2">用于格式化本地化字符串的参数 2。</param>
        /// <param name="arg3">用于格式化本地化字符串的参数 3。</param>
        /// <param name="arg4">用于格式化本地化字符串的参数 4。</param>
        /// <param name="arg5">用于格式化本地化字符串的参数 5。</param>
        /// <param name="arg6">用于格式化本地化字符串的参数 6。</param>
        /// <param name="arg7">用于格式化本地化字符串的参数 7。</param>
        /// <param name="arg8">用于格式化本地化字符串的参数 8。</param>
        /// <param name="arg9">用于格式化本地化字符串的参数 9。</param>
        /// <param name="arg10">用于格式化本地化字符串的参数 10。</param>
        /// <param name="arg11">用于格式化本地化字符串的参数 11。</param>
        /// <param name="arg12">用于格式化本地化字符串的参数 12。</param>
        /// <param name="arg13">用于格式化本地化字符串的参数 13。</param>
        /// <param name="arg14">用于格式化本地化字符串的参数 14。</param>
        /// <param name="arg15">用于格式化本地化字符串的参数 15。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15);
        }

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <typeparam name="T3">格式化参数 3 的类型。</typeparam>
        /// <typeparam name="T4">格式化参数 4 的类型。</typeparam>
        /// <typeparam name="T5">格式化参数 5 的类型。</typeparam>
        /// <typeparam name="T6">格式化参数 6 的类型。</typeparam>
        /// <typeparam name="T7">格式化参数 7 的类型。</typeparam>
        /// <typeparam name="T8">格式化参数 8 的类型。</typeparam>
        /// <typeparam name="T9">格式化参数 9 的类型。</typeparam>
        /// <typeparam name="T10">格式化参数 10 的类型。</typeparam>
        /// <typeparam name="T11">格式化参数 11 的类型。</typeparam>
        /// <typeparam name="T12">格式化参数 12 的类型。</typeparam>
        /// <typeparam name="T13">格式化参数 13 的类型。</typeparam>
        /// <typeparam name="T14">格式化参数 14 的类型。</typeparam>
        /// <typeparam name="T15">格式化参数 15 的类型。</typeparam>
        /// <typeparam name="T16">格式化参数 16 的类型。</typeparam>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        public string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14,
            T15 arg15, T16 arg16)
        {
            if (!Dic.TryGetValue(key, out string value))
            {
                return key;
            }

            return Utility.Text.Format(value, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16);
        }


        /// <summary>
        /// 以增量方式添加本地化配置表内容。
        /// </summary>
        /// <param name="table">要处理的本地化配置表。</param>
        public void IncreAddLocalizationConfig(GameLocaizationTable table)
        {
            TrackTable(table);
            ReapplyTrackedTable(table);
        }

        /// <summary>
        /// 清空当前数据后，用指定配置表覆盖本地化内容。
        /// </summary>
        /// <param name="table">要处理的本地化配置表。</param>
        public void CoverAddLocalizationConfig(GameLocaizationTable table)
        {
            Dic.Clear();
            _trackedTables.Clear();
            _trackedTableSet.Clear();
            _trackedTableKeys.Clear();
            TrackTable(table);
            ReapplyTrackedTable(table);
        }

        /// <summary>
        /// 重新加载指定配置表在当前语言下的内容。
        /// </summary>
        /// <param name="table">要处理的本地化配置表。</param>
        public void ReloadLocalizationConfig(GameLocaizationTable table)
        {
            TrackTable(table);
            ReapplyTrackedTable(table);
        }

        /// <summary>
        /// 初始化本地化服务并设置当前语言。
        /// </summary>
        protected override void OnInitialize() { }

        /// <summary>
        /// 销毁服务时清理已缓存的本地化数据。
        /// </summary>
        protected override void OnDestroyService()
        {
            Dic.Clear();
            _trackedTables.Clear();
            _trackedTableSet.Clear();
            _trackedTableKeys.Clear();
        }

        /// <summary>
        /// 将配置表加入跟踪列表，便于后续语言切换时重新应用。
        /// </summary>
        /// <param name="table">要处理的本地化配置表。</param>
        private void TrackTable(GameLocaizationTable table)
        {
            if (table == null)
            {
                return;
            }

            table.PrewarmLanguageLookup();
            if (_trackedTableSet.Add(table))
            {
                _trackedTables.Add(table);
            }
        }

        /// <summary>
        /// 按当前语言重新构建所有已跟踪配置表的缓存内容。
        /// </summary>
        private void RebuildTrackedTables()
        {
            Dic.Clear();

            for (int i = 0; i < _trackedTables.Count; i++)
            {
                ReapplyTrackedTable(_trackedTables[i]);
            }
        }

        /// <summary>
        /// 重新将指定配置表在当前语言下的内容写入缓存。
        /// </summary>
        /// <param name="table">要处理的本地化配置表。</param>
        private void ReapplyTrackedTable(GameLocaizationTable table)
        {
            if (table == null)
            {
                return;
            }

            RemoveTrackedTableEntries(table);

            LocalizationLanguage localizationLanguage = table.GetLanguage(_language);
            if (localizationLanguage == null)
            {
                Log.Warning(ZString.Format("Can not Find {0} Strins ", _language));
                if (_trackedTableKeys.TryGetValue(table, out List<string> missingKeys))
                {
                    missingKeys.Clear();
                }

                return;
            }

            Dic.EnsureCapacity(Dic.Count + localizationLanguage.Strings.Count);
            if (!_trackedTableKeys.TryGetValue(table, out List<string> keys))
            {
                keys = new List<string>(localizationLanguage.Strings.Count);
                _trackedTableKeys.Add(table, keys);
            }
            else
            {
                keys.Clear();
                if (keys.Capacity < localizationLanguage.Strings.Count)
                {
                    keys.Capacity = localizationLanguage.Strings.Count;
                }
            }

            for (int i = 0; i < localizationLanguage.Strings.Count; i++)
            {
                LocalizationLanguage.LocalizationString item = localizationLanguage.Strings[i];
                Dic[item.Key] = item.Value;
                keys.Add(item.Key);
            }
        }

        /// <summary>
        /// 从缓存中移除指定配置表此前写入的所有键值。
        /// </summary>
        /// <param name="table">要处理的本地化配置表。</param>
        private void RemoveTrackedTableEntries(GameLocaizationTable table)
        {
            if (!_trackedTableKeys.TryGetValue(table, out List<string> keys))
            {
                return;
            }

            for (int i = 0; i < keys.Count; i++)
            {
                Dic.Remove(keys[i]);
            }
        }
    }
}
