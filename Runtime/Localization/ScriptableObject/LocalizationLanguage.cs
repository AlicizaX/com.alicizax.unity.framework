using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlicizaX.Localization
{
    public class LocalizationLanguage : ScriptableObject
    {
        [Serializable]
        public sealed class LocalizationString
        {
            public int EntryId;
            public int SectionId;

            public string Key;
            public string Value;
        }

        public string LanguageName;
        public List<LocalizationString> Strings = new();
    }
}
