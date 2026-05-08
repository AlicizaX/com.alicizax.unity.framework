using AlicizaX.Resource.Runtime;
using UnityEngine;

namespace AlicizaX.Localization.Runtime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game Framework/Localization")]
    public sealed class LocalizationComponent : MonoBehaviour
    {
        private const string DefaultLanguage = "ChineseSimplified";
        private const string RuntimeLanguagePrefsKey = "AlicizaX.Localization.Language";
        private const string EditorLanguagePrefsKey = "AlicizaX.Localization.Editor.Language";

        private ILocalizationService _mLocalizationService = null;

        public static string PrefsKey = EditorLanguagePrefsKey;

        [SerializeField] private string _language;

        internal void SetLanguage(string language)
        {
            if (!string.IsNullOrEmpty(language))
            {
                _language = language;
            }
        }

        internal static void SaveLanguagePreference(string language)
        {
            if (string.IsNullOrEmpty(language))
            {
                return;
            }

#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetString(PrefsKey, language);
#endif
            Utility.PlayerPrefsX.SetString(RuntimeLanguagePrefsKey, language);
        }

        private static string LoadLanguagePreference(string fallbackLanguage)
        {
            string fallback = string.IsNullOrEmpty(fallbackLanguage) ? DefaultLanguage : fallbackLanguage;
            string language = Utility.PlayerPrefsX.GetString(RuntimeLanguagePrefsKey, fallback);
#if UNITY_EDITOR
            language = UnityEditor.EditorPrefs.GetString(PrefsKey, language);
#endif
            return string.IsNullOrEmpty(language) ? fallback : language;
        }

        private void Awake()
        {
            if (!AppServices.TryGetApp<ILocalizationService>(out _mLocalizationService))
            {
                _mLocalizationService = AppServices.RegisterApp<ILocalizationService>(new LocalizationService());
            }

            if (_mLocalizationService == null)
            {
                Log.Info("Localization manager is invalid.");
                return;
            }

            _language = LoadLanguagePreference(_language);
            _mLocalizationService.Initialize(_language);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                Utility.PlayerPrefsX.Save();
            }
        }

        private void OnApplicationQuit()
        {
            Utility.PlayerPrefsX.Save();
        }
    }
}
