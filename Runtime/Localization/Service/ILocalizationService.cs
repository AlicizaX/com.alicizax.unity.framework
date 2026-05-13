using System.Threading;
using AlicizaX;
using Cysharp.Threading.Tasks;

namespace AlicizaX.Localization.Runtime
{
    /// <summary>
    /// 定义本地化服务的核心能力，包括语言切换、字符串查询与配置表管理。
    /// </summary>
    public interface ILocalizationService : IService
    {
        /// <summary>
        /// 获取当前正在使用的语言标识。
        /// </summary>
        public string Language { get; }

        /// <summary>
        /// 初始化本地化服务并设置当前语言。
        /// </summary>
        /// <param name="language">要设置或切换到的语言标识。</param>
        void Initialize(string language);

        /// <summary>
        /// 尝试按键获取未格式化的本地化字符串。
        /// </summary>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="value">输出获取到的原始字符串值。</param>
        /// <returns>如果找到对应键则返回 `true`；否则返回 `false`。</returns>
        bool TryGetRawString(string key, out string value);

        /// <summary>
        /// 按键获取本地化字符串。
        /// </summary>
        /// <param name="key">本地化字符串的键。</param>
        /// <returns>返回本地化结果；若键不存在则返回缺失标记字符串。</returns>
        string GetString(string key);


        /// <summary>
        /// 以增量方式添加本地化配置表内容。
        /// </summary>
        /// <param name="table">要处理的本地化配置表。</param>
        void IncreAddLocalizationConfig(GameLocaizationTable table);

        /// <summary>
        /// 清空当前数据后，用指定配置表覆盖本地化内容。
        /// </summary>
        /// <param name="table">要处理的本地化配置表。</param>
        void CoverAddLocalizationConfig(GameLocaizationTable table);

        /// <summary>
        /// 重新加载指定配置表在当前语言下的内容。
        /// </summary>
        /// <param name="table">要处理的本地化配置表。</param>
        void ReloadLocalizationConfig(GameLocaizationTable table);

        /// <summary>
        /// 异步切换当前语言并刷新已跟踪的本地化数据。
        /// </summary>
        /// <param name="language">目标语言</param>
        void SwitchLanguage(string language);

        /// <summary>
        /// 应用语言 调用该则广播事件
        /// </summary>
        void ApplyLanguage();

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T">格式化参数的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg">用于格式化本地化字符串的参数。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        string GetString<T>(string key, T arg);

        /// <summary>
        /// 按键获取并格式化本地化字符串。
        /// </summary>
        /// <typeparam name="T1">格式化参数 1 的类型。</typeparam>
        /// <typeparam name="T2">格式化参数 2 的类型。</typeparam>
        /// <param name="key">本地化字符串的键。</param>
        /// <param name="arg1">用于格式化本地化字符串的参数 1。</param>
        /// <param name="arg2">用于格式化本地化字符串的参数 2。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        string GetString<T1, T2>(string key, T1 arg1, T2 arg2);

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
        string GetString<T1, T2, T3>(string key, T1 arg1, T2 arg2, T3 arg3);

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
        string GetString<T1, T2, T3, T4>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4);

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
        string GetString<T1, T2, T3, T4, T5>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);

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
        string GetString<T1, T2, T3, T4, T5, T6>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6);

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
        string GetString<T1, T2, T3, T4, T5, T6, T7>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7);

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
        string GetString<T1, T2, T3, T4, T5, T6, T7, T8>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8);

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
        string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9);

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
        string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10);

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
        string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11);

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
        string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12);

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
        string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13);

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
        string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14);

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
        string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15);

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
        /// <param name="arg16">用于格式化本地化字符串的参数 16。</param>
        /// <returns>返回格式化后的本地化结果；若键不存在则返回缺失标记字符串。</returns>
        string GetString<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string key, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16);
    }
}
