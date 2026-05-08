using System.Collections.Generic;
using UnityEngine;

namespace AlicizaX.Localization.Editor
{
    [AlicizaX.Editor.Setting.FilePath("ProjectSettings/LocalizationConfiguration.asset")]
    public class LocalizationConfiguration : AlicizaX.Editor.Setting.ScriptableSingleton<LocalizationConfiguration>
    {
        internal static readonly LanguageType[] AllLanguageTypes =
        {
            LanguageType.ChineseSimplified,
            LanguageType.ChineseTraditional,
            LanguageType.English,
            LanguageType.Japanese,
            LanguageType.Korean,
            LanguageType.French,
            LanguageType.German,
            LanguageType.Spanish,
            LanguageType.Portuguese,
            LanguageType.Russian,
            LanguageType.Italian,
            LanguageType.Dutch,
            LanguageType.Turkish,
            LanguageType.Vietnamese,
            LanguageType.Thai,
            LanguageType.Indonesian,
            LanguageType.Arabic,
            LanguageType.Hindi,
        };

        [SerializeField] internal LanguageType selectedLanguageTypes =
            LanguageType.ChineseSimplified |
            LanguageType.English |
            LanguageType.Japanese |
            LanguageType.Russian;

        [SerializeField] internal LanguageType generateScriptCodeFirstConfig = LanguageTypeUtility.Default;
        [SerializeField] internal string generateLanguageTypesPath = "Assets/LanguageTypes";
        [SerializeField] internal string generateLanguageTypesClassName = "LanguageTypes";
        [SerializeField] internal string generateLanguageTypesNamespace = string.Empty;

        private readonly List<string> _languageTypeNames = new();

        public IReadOnlyList<string> LanguageTypeNames
        {
            get
            {
                RebuildLanguageTypeNames();
                return _languageTypeNames;
            }
        }

        internal LanguageType SelectedLanguageTypes => selectedLanguageTypes;
        internal LanguageType GenerateScriptCodeFirstConfig => generateScriptCodeFirstConfig;
        public string GenerateScriptCodeFirstConfigName => LanguageTypeUtility.ToName(generateScriptCodeFirstConfig);
        public string GenerateLanguageTypesPath => generateLanguageTypesPath;
        public string GenerateLanguageTypesClassName => generateLanguageTypesClassName;
        public string GenerateLanguageTypesNamespace => generateLanguageTypesNamespace;

        internal bool IsLanguageSelected(LanguageType languageType)
        {
            return (selectedLanguageTypes & languageType) != 0;
        }

        internal bool IsLanguageSelected(string languageName)
        {
            for (int i = 0; i < AllLanguageTypes.Length; i++)
            {
                LanguageType languageType = AllLanguageTypes[i];
                if (LanguageTypeUtility.ToName(languageType) == languageName)
                {
                    return IsLanguageSelected(languageType);
                }
            }

            return false;
        }

        private void RebuildLanguageTypeNames()
        {
            _languageTypeNames.Clear();
            for (int i = 0; i < AllLanguageTypes.Length; i++)
            {
                LanguageType languageType = AllLanguageTypes[i];
                if ((selectedLanguageTypes & languageType) != 0)
                {
                    _languageTypeNames.Add(LanguageTypeUtility.ToName(languageType));
                }
            }
        }
    }
}
