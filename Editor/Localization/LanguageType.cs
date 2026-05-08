using System;

namespace AlicizaX.Localization.Editor
{
    [Flags]
    internal enum LanguageType
    {
        None = 0,
        ChineseSimplified = 1 << 0,
        ChineseTraditional = 1 << 1,
        English = 1 << 2,
        Japanese = 1 << 3,
        Korean = 1 << 4,
        French = 1 << 5,
        German = 1 << 6,
        Spanish = 1 << 7,
        Portuguese = 1 << 8,
        Russian = 1 << 9,
        Italian = 1 << 10,
        Dutch = 1 << 11,
        Turkish = 1 << 12,
        Vietnamese = 1 << 13,
        Thai = 1 << 14,
        Indonesian = 1 << 15,
        Arabic = 1 << 16,
        Hindi = 1 << 17,
        All = ChineseSimplified |
              ChineseTraditional |
              English |
              Japanese |
              Korean |
              French |
              German |
              Spanish |
              Portuguese |
              Russian |
              Italian |
              Dutch |
              Turkish |
              Vietnamese |
              Thai |
              Indonesian |
              Arabic |
              Hindi,
    }

    internal static class LanguageTypeUtility
    {
        public const LanguageType Default = LanguageType.ChineseSimplified;

        public static bool IsSingle(LanguageType languageType)
        {
            int value = (int)languageType;
            return languageType != LanguageType.None &&
                   (languageType & LanguageType.All) == languageType &&
                   (value & (value - 1)) == 0;
        }

        public static string ToName(LanguageType languageType)
        {
            return languageType switch
            {
                LanguageType.ChineseSimplified => nameof(LanguageType.ChineseSimplified),
                LanguageType.ChineseTraditional => nameof(LanguageType.ChineseTraditional),
                LanguageType.English => nameof(LanguageType.English),
                LanguageType.Japanese => nameof(LanguageType.Japanese),
                LanguageType.Korean => nameof(LanguageType.Korean),
                LanguageType.French => nameof(LanguageType.French),
                LanguageType.German => nameof(LanguageType.German),
                LanguageType.Spanish => nameof(LanguageType.Spanish),
                LanguageType.Portuguese => nameof(LanguageType.Portuguese),
                LanguageType.Russian => nameof(LanguageType.Russian),
                LanguageType.Italian => nameof(LanguageType.Italian),
                LanguageType.Dutch => nameof(LanguageType.Dutch),
                LanguageType.Turkish => nameof(LanguageType.Turkish),
                LanguageType.Vietnamese => nameof(LanguageType.Vietnamese),
                LanguageType.Thai => nameof(LanguageType.Thai),
                LanguageType.Indonesian => nameof(LanguageType.Indonesian),
                LanguageType.Arabic => nameof(LanguageType.Arabic),
                LanguageType.Hindi => nameof(LanguageType.Hindi),
                _ => string.Empty
            };
        }
    }
}
