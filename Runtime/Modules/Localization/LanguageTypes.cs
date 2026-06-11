using System.Collections.Generic;

public static class LanguageTypes
{
    public const string ChineseSimplified = "ChineseSimplified";
    public const string ChineseTraditional = "ChineseTraditional";
    public const string English = "English";
    public const string Japanese = "Japanese";
    public const string Korean = "Korean";
    public const string French = "French";
    public const string German = "German";
    public const string Spanish = "Spanish";
    public const string Portuguese = "Portuguese";
    public const string Russian = "Russian";
    public const string Italian = "Italian";
    public const string Dutch = "Dutch";
    public const string Turkish = "Turkish";
    public const string Vietnamese = "Vietnamese";
    public const string Thai = "Thai";
    public const string Indonesian = "Indonesian";
    public const string Arabic = "Arabic";
    public const string Hindi = "Hindi";

    public static readonly IReadOnlyList<string> Languages = new List<string>
    {
        "ChineseSimplified",
        "ChineseTraditional",
        "English",
        "Japanese",
        "Korean",
        "French",
        "German",
        "Spanish",
        "Portuguese",
        "Russian",
        "Italian",
        "Dutch",
        "Turkish",
        "Vietnamese",
        "Thai",
        "Indonesian",
        "Arabic",
        "Hindi",
    };

    public static string IndexToString(int index)
    {
        if (index < 0 || index >= Languages.Count) return "Unknown";
        return Languages[index];
    }

    public static int StringToIndex(string s)
    {
        int index = -1;
        for (int i = 0; i < Languages.Count; i++)
        {
            if (Languages[i] == s)
            {
                index = i;
                break;
            }
        }

        return index;
    }
}
