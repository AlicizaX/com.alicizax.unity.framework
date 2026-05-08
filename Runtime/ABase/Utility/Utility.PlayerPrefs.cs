using System;
using UnityEngine;

namespace AlicizaX
{
    public static partial class Utility
    {
        public static class PlayerPrefsX
        {
            private static string _prefix = string.Empty;

            /// <summary>
            /// 当前用户前缀，用于隔离不同用户的配置项。
            /// </summary>
            public static string CurrentUserPrefix
            {
                get
                {
                    if (string.IsNullOrEmpty(_prefix))
                    {
                        _prefix = Application.productName.ToString();
                    }

                    return _prefix;
                }
                set => _prefix = value;
            }

            /// <summary>
            /// 组合完整键名，格式为"[前缀].[原始键名]"
            /// </summary>
            private static string CombineKey(string settingName)
            {
                return string.IsNullOrEmpty(CurrentUserPrefix)
                    ? settingName
                    : $"{CurrentUserPrefix}.{settingName}";
            }

            /// <summary>
            /// 保存游戏配置。
            /// </summary>
            public static bool Save()
            {
                PlayerPrefs.Save();
                return true;
            }

            /// <summary>
            /// 检查是否存在指定配置项。
            /// </summary>
            public static bool HasSetting(string settingName)
                => PlayerPrefs.HasKey(CombineKey(settingName));

            /// <summary>
            /// 移除指定配置项。
            /// </summary>
            public static bool RemoveSetting(string settingName)
            {
                var fullKey = CombineKey(settingName);
                if (!PlayerPrefs.HasKey(fullKey)) return false;

                PlayerPrefs.DeleteKey(fullKey);
                return true;
            }

            /// <summary>
            /// 清空所有配置项（包括所有用户）。
            /// </summary>
            public static void RemoveAllSettings() => PlayerPrefs.DeleteAll();

            // 布尔值处理
            public static bool GetBool(string settingName)
                => PlayerPrefs.GetInt(CombineKey(settingName)) != 0;

            public static bool GetBool(string settingName, bool defaultValue)
                => PlayerPrefs.GetInt(CombineKey(settingName), defaultValue ? 1 : 0) != 0;

            public static void SetBool(string settingName, bool value)
                => PlayerPrefs.SetInt(CombineKey(settingName), value ? 1 : 0);

            // 整型处理
            public static int GetInt(string settingName)
                => PlayerPrefs.GetInt(CombineKey(settingName));

            public static int GetInt(string settingName, int defaultValue)
                => PlayerPrefs.GetInt(CombineKey(settingName), defaultValue);

            public static void SetInt(string settingName, int value)
                => PlayerPrefs.SetInt(CombineKey(settingName), value);

            // 浮点数处理
            public static float GetFloat(string settingName)
                => PlayerPrefs.GetFloat(CombineKey(settingName));

            public static float GetFloat(string settingName, float defaultValue)
                => PlayerPrefs.GetFloat(CombineKey(settingName), defaultValue);

            public static void SetFloat(string settingName, float value)
                => PlayerPrefs.SetFloat(CombineKey(settingName), value);

            // 字符串处理
            public static string GetString(string settingName)
                => PlayerPrefs.GetString(CombineKey(settingName));

            public static string GetString(string settingName, string defaultValue)
                => PlayerPrefs.GetString(CombineKey(settingName), defaultValue);

            public static void SetString(string settingName, string value)
                => PlayerPrefs.SetString(CombineKey(settingName), value);

            // 对象序列化处理
            public static T GetObject<T>(string settingName)
                => Utility.Json.ToObject<T>(GetString(settingName));

            public static object GetObject(Type objectType, string settingName)
                => Utility.Json.ToObject(objectType, GetString(settingName));

            public static T GetObject<T>(string settingName, T defaultObj)
            {
                var json = GetString(settingName, null);
                return string.IsNullOrWhiteSpace(json) ? defaultObj : Utility.Json.ToObject<T>(json);
            }

            public static object GetObject(Type objectType, string settingName, object defaultObj)
            {
                var json = GetString(settingName, null);
                return string.IsNullOrWhiteSpace(json) ? defaultObj : Utility.Json.ToObject(objectType, json);
            }

            public static void SetObject<T>(string settingName, T obj)
                => SetString(settingName, Utility.Json.ToJson(obj));

            public static void SetObject(string settingName, object obj)
                => SetString(settingName, Utility.Json.ToJson(obj));
        }
    }
}
