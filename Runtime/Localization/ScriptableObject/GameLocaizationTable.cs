using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlicizaX.Localization
{
    public class GameLocaizationTable : ScriptableObject
    {
#if UNITY_EDITOR
        [SerializeField] internal string GenerateScriptCodeFolderPath = string.Empty;
        [SerializeField] internal string GenerateScriptCodeClassName = "LocalizationKeyNew";
        [SerializeField] internal string GenerateScriptCodeNamespace = string.Empty;

        [Serializable]
        public struct SheetItem
        {
            public int Id;
            public string Key;
            public bool IsGen;

            public SheetItem(string key, int id, bool isGen)
            {
                Id = id;
                Key = key;
                IsGen = isGen;
            }
        }

        [Serializable]
        public struct TableData
        {
            public int Id;
            public string SectionName;
            public List<SheetItem> SectionSheet;

            public TableData(string section, int id)
            {
                Id = id;
                SectionName = section;
                SectionSheet = new List<SheetItem>();
            }
        }

        public List<TableData> TableSheet = new();
#endif
        public List<LocalizationLanguage> Languages = new();

        [NonSerialized] private Dictionary<string, LocalizationLanguage> _languageMap;
        [NonSerialized] private int _languageMapVersion = -1;

        internal LocalizationLanguage GetLanguage(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode) || Languages == null || Languages.Count == 0)
            {
                return null;
            }

            EnsureLanguageMap();
            _languageMap.TryGetValue(languageCode, out LocalizationLanguage language);
            return language;
        }

        public void InvalidateLanguageLookup()
        {
            _languageMapVersion = -1;
        }

        internal void PrewarmLanguageLookup()
        {
            EnsureLanguageMap();
        }

        private void EnsureLanguageMap()
        {
            int count = Languages?.Count ?? 0;
            if (_languageMap != null && _languageMapVersion == count)
            {
                return;
            }

            _languageMap ??= new Dictionary<string, LocalizationLanguage>(count, StringComparer.Ordinal);
            _languageMap.Clear();

            if (Languages != null)
            {
                for (int i = 0; i < Languages.Count; i++)
                {
                    LocalizationLanguage language = Languages[i];
                    if (language == null || string.IsNullOrEmpty(language.LanguageName))
                    {
                        continue;
                    }

                    _languageMap[language.LanguageName] = language;
                }
            }

            _languageMapVersion = count;
        }
    }
}

