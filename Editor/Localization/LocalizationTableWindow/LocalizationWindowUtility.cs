using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AlicizaX.Localization.Editor
{
    public sealed class LanguageEntry
    {
        public string LanguageName;
        public LocalizationLanguage Asset;

        public LanguageEntry()
        {
        }

        public LanguageEntry(LocalizationLanguage asset)
        {
            LanguageName = asset.LanguageName;
            Asset = asset;
        }
    }

    public class SheetSectionTreeView
    {
        public int Id;
        public string Name;

        public SheetSectionTreeView()
        {
            Id = Random.Range(10000000, 99999999);
            Name = LocalizationWindowUtility.NULL;
        }
    }

    public class SheetItemTreeView
    {
        public int Id;
        public string Key;
        public bool isGen;
        public SheetSectionTreeView Parent;

        public SheetItemTreeView()
        {
            Id = Random.Range(10000000, 99999999);
            Key = LocalizationWindowUtility.NULL;
        }
    }

    public sealed class TempSheetItem
    {
        // Reference to the original item (shared instance)
        public SheetItemTreeView Reference;

        // Language-specific localization value
        public string Value;

        public int Id => Reference.Id;

        public string Key
        {
            get => Reference.Key;
            set => Reference.Key = value;
        }

        public SheetSectionTreeView Parent => Reference.Parent;
        public Vector2 Scroll { get; set; }
        public bool IsExpanded { get; set; }
    }

    public sealed class TempSheetSection
    {
        // Reference to the original section (shared instance)
        public SheetSectionData Reference;

        // Language-specific items
        public List<TempSheetItem> Items = new();

        public int Id => Reference.Id;

        public string Name
        {
            get => Reference.Name;
            set => Reference.Name = value;
        }
    }

    public sealed class SheetSectionData : SheetSectionTreeView
    {
        public List<SheetItemTreeView> Items = new();
        public bool IsExpanded { get; set; }
    }

    public sealed class TempLanguageData
    {
        public LanguageEntry Entry;
        public List<TempSheetSection> TableSheet = new();
    }

    public sealed class LocalizationWindowData
    {
        public List<TempLanguageData> Languages = new();
        public List<SheetSectionData> TableSheet = new();

        public int LanguageCount = 0;
        public int SectionCount = 0;
        public int EntryCount = 0;
    }

    public static class LocalizationWindowUtility
    {
        public const string NULL = "null";
        private const string GeneratedIndent = "    ";
        private const int MaxGeneratedLocalizationArguments = 16;

        public static bool IsValidIdentifierName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            value = value.Replace(" ", string.Empty);
            if (value.Length == 0)
            {
                return false;
            }

            char first = value[0];
            if (!(char.IsLetter(first) || first == '_'))
            {
                return false;
            }

            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsValidKeyPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = NormalizeKeyPart(value);
            if (normalized.Length == 0 || normalized[0] == '.' || normalized[normalized.Length - 1] == '.')
            {
                return false;
            }

            int segmentStart = 0;
            for (int i = 0; i <= normalized.Length; i++)
            {
                if (i != normalized.Length && normalized[i] != '.')
                {
                    continue;
                }

                int segmentLength = i - segmentStart;
                if (segmentLength == 0 || !IsValidIdentifierName(normalized.Substring(segmentStart, segmentLength)))
                {
                    return false;
                }

                segmentStart = i + 1;
            }

            return true;
        }

        public static bool TryValidateSectionName(LocalizationWindowData data, SheetSectionTreeView section, string newName, out string error)
        {
            error = null;
            if (!IsValidIdentifierName(newName))
            {
                error = "Section name must be a valid C# identifier after removing spaces.";
                return false;
            }

            string normalized = NormalizeKeyPart(newName);
            for (int i = 0; i < data.TableSheet.Count; i++)
            {
                SheetSectionData candidate = data.TableSheet[i];
                if (candidate.Id != section.Id && string.Equals(NormalizeKeyPart(candidate.Name), normalized, StringComparison.Ordinal))
                {
                    error = "Section name is duplicated.";
                    return false;
                }
            }

            return true;
        }

        public static bool TryValidateItemKey(SheetSectionTreeView section, SheetItemTreeView item, string newKey, out string error)
        {
            error = null;
            if (!IsValidKeyPath(newKey))
            {
                error = "Key must be a dot-separated path of valid C# identifiers after removing spaces.";
                return false;
            }

            if (section is SheetSectionData sectionData)
            {
                string normalized = NormalizeKeyPart(newKey);
                for (int i = 0; i < sectionData.Items.Count; i++)
                {
                    SheetItemTreeView candidate = sectionData.Items[i];
                    if (candidate.Id != item.Id && string.Equals(NormalizeKeyPart(candidate.Key), normalized, StringComparison.Ordinal))
                    {
                        error = "Key is duplicated in this section.";
                        return false;
                    }
                }
            }

            return true;
        }

        public static string NormalizeKeyPart(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace(" ", string.Empty);
        }

        public static string EscapeCodeString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        public static string EscapeXmlDocText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        public static bool TryGetFormatPlaceholderSequence(string value, List<int> placeholders, out string error)
        {
            error = null;
            placeholders.Clear();

            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '{')
                {
                    if (i + 1 < value.Length && value[i + 1] == '{')
                    {
                        i++;
                        continue;
                    }

                    int indexStart = i + 1;
                    int index = 0;
                    bool hasDigit = false;
                    while (indexStart < value.Length)
                    {
                        char digit = value[indexStart];
                        if (digit < '0' || digit > '9')
                        {
                            break;
                        }

                        hasDigit = true;
                        index = index * 10 + digit - '0';
                        indexStart++;
                    }

                    if (!hasDigit)
                    {
                        error = "Format placeholder must contain a numeric index.";
                        return false;
                    }

                    if (indexStart < value.Length && value[indexStart] == ',')
                    {
                        indexStart++;
                        if (indexStart < value.Length && value[indexStart] == '-')
                        {
                            indexStart++;
                        }

                        bool hasAlignmentDigit = false;
                        while (indexStart < value.Length)
                        {
                            char alignmentDigit = value[indexStart];
                            if (alignmentDigit < '0' || alignmentDigit > '9')
                            {
                                break;
                            }

                            hasAlignmentDigit = true;
                            indexStart++;
                        }

                        if (!hasAlignmentDigit)
                        {
                            error = "Format placeholder alignment must contain a numeric value.";
                            return false;
                        }
                    }

                    if (indexStart < value.Length && value[indexStart] == ':')
                    {
                        indexStart++;
                        while (indexStart < value.Length && value[indexStart] != '}')
                        {
                            char formatChar = value[indexStart];
                            if (formatChar == '{')
                            {
                                error = "Nested format placeholders are not supported.";
                                return false;
                            }

                            indexStart++;
                        }
                    }

                    if (indexStart < value.Length && value[indexStart] != '}')
                    {
                        error = "Format placeholder must end after index, alignment, or format string.";
                        return false;
                    }

                    if (indexStart >= value.Length)
                    {
                        error = "Format placeholder is missing a closing brace.";
                        return false;
                    }

                    placeholders.Add(index);
                    i = indexStart;
                    continue;
                }

                if (c == '}')
                {
                    if (i + 1 < value.Length && value[i + 1] == '}')
                    {
                        i++;
                        continue;
                    }

                    error = "Format placeholder is missing an opening brace.";
                    return false;
                }
            }

            return true;
        }

        public static bool IsContinuousFormatPlaceholderSequence(IReadOnlyList<int> placeholders)
        {
            if (placeholders == null || placeholders.Count == 0)
            {
                return true;
            }

            int maxSeen = -1;
            for (int i = 0; i < placeholders.Count; i++)
            {
                int index = placeholders[i];
                if (index <= maxSeen)
                {
                    continue;
                }

                if (index != maxSeen + 1)
                {
                    return false;
                }

                maxSeen = index;
            }

            return true;
        }

        public static int GetFormatPlaceholderArgumentCount(IReadOnlyList<int> placeholders)
        {
            if (placeholders == null || placeholders.Count == 0)
            {
                return 0;
            }

            int maxIndex = -1;
            for (int i = 0; i < placeholders.Count; i++)
            {
                if (placeholders[i] > maxIndex)
                {
                    maxIndex = placeholders[i];
                }
            }

            return maxIndex + 1;
        }

        public static bool FormatPlaceholderSequenceEquals(IReadOnlyList<int> left, IReadOnlyList<int> right)
        {
            int leftCount = left?.Count ?? 0;
            int rightCount = right?.Count ?? 0;
            if (leftCount != rightCount)
            {
                return false;
            }

            for (int i = 0; i < leftCount; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        public static void BuildWindowData(GameLocaizationTable table, out LocalizationWindowData data)
        {
            LocalizationWindowData windowData = new();

            int languages = 0;
            int sections = 0;
            int entries = 0;

            // 1. Build the main table structure
            foreach (var tableData in table.TableSheet)
            {
                SheetSectionData sectionData = new()
                {
                    Id = tableData.Id,
                    Name = tableData.SectionName,
                    Items = new List<SheetItemTreeView>()
                };

                foreach (var item in tableData.SectionSheet)
                {
                    sectionData.Items.Add(new()
                    {
                        Id = item.Id,
                        Key = item.Key,
                        Parent = sectionData,
                        isGen = item.IsGen
                    });
                    entries++;
                }

                windowData.TableSheet.Add(sectionData);
                sections++;
            }

            windowData.SectionCount = sections;
            windowData.EntryCount = entries;

            // 2. Build language-specific data that references the same sections and items
            foreach (var lang in table.Languages)
            {
                TempLanguageData langData = new()
                {
                    Entry = new(lang)
                };

                Dictionary<long, LocalizationLanguage.LocalizationString> stringLookup = null;
                if (lang != null)
                {
                    string languageName = lang.LanguageName;
                    langData.Entry.LanguageName = languageName;
                    stringLookup = BuildStringLookup(lang.Strings);
                }

                foreach (var globalSection in windowData.TableSheet)
                {
                    int _sectionId = globalSection.Id;

                    // Create a TempSheetSection reference and assign shared section reference
                    TempSheetSection tempSection = new()
                    {
                        Reference = globalSection
                    };

                    // For each item in the global section, we create a TempSheetItem that references it
                    foreach (var globalItem in globalSection.Items)
                    {
                        int _entryId = globalItem.Id;
                        string value = NULL;

                        if (stringLookup != null && stringLookup.TryGetValue(MakeEntryLookupKey(_sectionId, _entryId), out LocalizationLanguage.LocalizationString localizedString))
                        {
                            value = localizedString.Value;
                        }

                        // Add SheetItem to section items list and assign shared item reference
                        tempSection.Items.Add(new()
                        {
                            Reference = globalItem,
                            Value = value
                        });
                    }

                    langData.TableSheet.Add(tempSection);
                }

                if (LocalizationConfiguration.Instance.IsLanguageSelected(langData.Entry.LanguageName))
                {
                    windowData.Languages.Add(langData);
                    languages++;
                }
            }

            windowData.LanguageCount = languages;
            data = windowData;
        }

        /// <summary>
        /// Assign a language asset to an existing TempLanguageData.
        /// </summary>
        public static void AssignLanguage(this LocalizationWindowData data, TempLanguageData languageData, LocalizationLanguage asset)
        {
            // Assign the asset to the language entry
            languageData.Entry.Asset = asset;

            if (asset == null)
            {
                // If no asset is assigned, clear all values
                foreach (var tempSection in languageData.TableSheet)
                {
                    foreach (var tempItem in tempSection.Items)
                    {
                        tempItem.Value = string.Empty;
                    }
                }

                return;
            }
            else
            {
                // Assign language name from asset
                languageData.Entry.LanguageName = asset.LanguageName;
            }

            Dictionary<long, LocalizationLanguage.LocalizationString> stringLookup = BuildStringLookup(asset.Strings);

            // If we have a valid asset, we try to match each TempSheetItem to a corresponding LocalizationString
            foreach (var tempSection in languageData.TableSheet)
            {
                int sectionId = tempSection.Reference.Id;

                foreach (var tempItem in tempSection.Items)
                {
                    int entryId = tempItem.Reference.Id;
                    string value = string.Empty;

                    if (stringLookup.TryGetValue(MakeEntryLookupKey(sectionId, entryId), out LocalizationLanguage.LocalizationString item))
                    {
                        value = item.Value;
                    }

                    // Assign the localization value
                    tempItem.Value = value;
                }
            }
        }

        /// <summary>
        /// Add a new section to the global TableSheet.
        /// </summary>
        public static SheetSectionData AddSection(this LocalizationWindowData data, string sectionName, bool withIndex = true)
        {
            if (withIndex)
            {
                int sectionIndex = ++data.SectionCount;
                sectionName += " " + sectionIndex;
            }

            SheetSectionData newSection = new()
            {
                Name = sectionName,
                Items = new List<SheetItemTreeView>()
            };

            data.TableSheet.Add(newSection);

            // Add corresponding section to each language
            foreach (var lang in data.Languages)
            {
                lang.TableSheet.Add(new()
                {
                    Reference = newSection,
                    Items = new List<TempSheetItem>()
                });
            }

            return newSection;
        }

        /// <summary>
        /// Remove a section from the global TableSheet by reference.
        /// </summary>
        public static void RemoveSection(this LocalizationWindowData data, SheetSectionTreeView section)
        {
            // Remove section
            int sectionIndex = data.TableSheet.FindIndex(x => x.Id == section.Id);
            if (sectionIndex != -1) data.TableSheet.RemoveAt(sectionIndex);

            // Remove the corresponding section from each language
            foreach (var lang in data.Languages)
            {
                var tempSection = lang.TableSheet.FirstOrDefault(ts => ts.Reference == section);
                if (tempSection != null) lang.TableSheet.Remove(tempSection);
            }
        }

        /// <summary>
        /// Add a new item to a given section.
        /// </summary>
        public static SheetItemTreeView AddItem(this LocalizationWindowData data, SheetSectionData section, string key, bool withIndex = true)
        {
            if (withIndex)
            {
                int keyIndex = ++data.EntryCount;
                key += " " + keyIndex;
            }

            SheetItemTreeView newItem = new()
            {
                Key = key,
                Parent = section
            };

            section.Items.Add(newItem);

            // Add corresponding item to each language
            foreach (var lang in data.Languages)
            {
                var tempSection = lang.TableSheet.FirstOrDefault(ts => ts.Reference == section);
                if (tempSection != null)
                {
                    tempSection.Items.Add(new()
                    {
                        Reference = newItem,
                        Value = string.Empty
                    });
                }
            }

            data.EntryCount++;
            return newItem;
        }

        /// <summary>
        /// Remove an item by reference from the given section.
        /// </summary>
        public static void RemoveItem(this LocalizationWindowData data, SheetSectionTreeView section, SheetItemTreeView item)
        {
            // Remove item from section
            int sectionIndex = data.TableSheet.FindIndex(x => x.Id == section.Id);
            if (sectionIndex != -1) data.TableSheet[sectionIndex].Items.Remove(item);

            // Remove corresponding item from each language
            foreach (var lang in data.Languages)
            {
                var tempSection = lang.TableSheet.FirstOrDefault(ts => ts.Reference == section);
                if (tempSection != null)
                {
                    var tempItem = tempSection.Items.FirstOrDefault(ti => ti.Reference == item);
                    if (tempItem != null) tempSection.Items.Remove(tempItem);
                }
            }
        }

        /// <summary>
        /// Moves an item within the same section to a new position.
        /// </summary>
        public static void OnMoveItemWithinSection(this LocalizationWindowData data, SheetSectionTreeView section, SheetItemTreeView item, int position)
        {
            // Find the corresponding SheetSectionData in data
            var sectionData = data.TableSheet.FirstOrDefault(s => s.Id == section.Id);
            if (sectionData == null)
                return;

            int oldIndex = sectionData.Items.IndexOf(item);
            if (oldIndex < 0)
                return;

            // Clamp position
            int insertTo = position > oldIndex ? position - 1 : position;
            insertTo = Mathf.Clamp(insertTo, 0, sectionData.Items.Count - 1);

            if (oldIndex == insertTo)
                return;

            sectionData.Items.RemoveAt(oldIndex);
            sectionData.Items.Insert(insertTo, item);

            // Update in each language
            foreach (var lang in data.Languages)
            {
                // Find the corresponding TempSheetSection
                var tempSection = lang.TableSheet.FirstOrDefault(ts => ts.Reference == sectionData);
                if (tempSection == null) continue;

                int langOldIndex = tempSection.Items.FindIndex(i => i.Reference == item);
                if (langOldIndex < 0) continue;

                // Clamp position
                int insertToLang = position > langOldIndex ? position - 1 : position;
                insertToLang = Mathf.Clamp(insertToLang, 0, tempSection.Items.Count - 1);
                if (langOldIndex == insertToLang) continue;

                var tempItem = tempSection.Items[langOldIndex];
                tempSection.Items.RemoveAt(langOldIndex);
                tempSection.Items.Insert(insertToLang, tempItem);
            }
        }

        /// <summary>
        /// Moves an item from one section to another section, inserting it at a specific position.
        /// </summary>
        public static void OnMoveItemToSectionAt(this LocalizationWindowData data, SheetSectionTreeView parent, SheetSectionTreeView section, SheetItemTreeView item, int position)
        {
            // Find both parent and target sections
            var parentSection = data.TableSheet.FirstOrDefault(s => s.Id == parent.Id);
            var targetSection = data.TableSheet.FirstOrDefault(s => s.Id == section.Id);

            if (parentSection == null || targetSection == null)
                return;

            // Remove from parent
            if (!parentSection.Items.Remove(item))
                return;

            // Insert into target at specified position
            position = Mathf.Max(0, Mathf.Min(position, targetSection.Items.Count));

            item.Parent = targetSection;
            targetSection.Items.Insert(position, item);

            // Update in each language
            foreach (var lang in data.Languages)
            {
                var langParentSection = lang.TableSheet.FirstOrDefault(ts => ts.Reference == parentSection);
                var langTargetSection = lang.TableSheet.FirstOrDefault(ts => ts.Reference == targetSection);

                if (langParentSection == null || langTargetSection == null)
                    continue;

                // Remove the corresponding temp item from the parent section
                var tempItem = langParentSection.Items.FirstOrDefault(i => i.Reference == item);
                if (tempItem == null) continue;

                langParentSection.Items.Remove(tempItem);

                // Insert into target section
                int insertToLang = Mathf.Clamp(position, 0, langTargetSection.Items.Count);
                langTargetSection.Items.Insert(insertToLang, tempItem);
            }
        }

        /// <summary>
        /// Moves an item from one section to another section, adding it to the end.
        /// </summary>
        public static void OnMoveItemToSection(this LocalizationWindowData data, SheetSectionTreeView parent, SheetSectionTreeView section, SheetItemTreeView item)
        {
            // Find both parent and target sections
            var parentSection = data.TableSheet.FirstOrDefault(s => s.Id == parent.Id);
            var targetSection = data.TableSheet.FirstOrDefault(s => s.Id == section.Id);

            if (parentSection == null || targetSection == null)
                return;

            // Remove from parent
            if (!parentSection.Items.Remove(item))
                return;

            item.Parent = targetSection;
            targetSection.Items.Add(item);

            // Update in each language
            foreach (var lang in data.Languages)
            {
                var langParentSection = lang.TableSheet.FirstOrDefault(ts => ts.Reference == parentSection);
                var langTargetSection = lang.TableSheet.FirstOrDefault(ts => ts.Reference == targetSection);

                if (langParentSection == null || langTargetSection == null)
                    continue;

                // Remove the corresponding temp item from the parent section
                var tempItem = langParentSection.Items.FirstOrDefault(i => i.Reference == item);
                if (tempItem == null) continue;

                langParentSection.Items.Remove(tempItem);
                langTargetSection.Items.Add(tempItem);
            }
        }

        /// <summary>
        /// Moves a section to a new position in the TableSheet list.
        /// </summary>
        public static void OnMoveSection(this LocalizationWindowData data, SheetSectionTreeView section, int position)
        {
            var sectionData = data.TableSheet.FirstOrDefault(s => s.Id == section.Id);
            if (sectionData == null)
                return;

            int oldIndex = data.TableSheet.IndexOf(sectionData);
            if (oldIndex < 0)
                return;

            // Clamp position
            int insertTo = position > oldIndex ? position - 1 : position;
            insertTo = Mathf.Clamp(insertTo, 0, data.TableSheet.Count - 1);

            if (oldIndex == insertTo)
                return;

            data.TableSheet.RemoveAt(oldIndex);
            data.TableSheet.Insert(insertTo, sectionData);

            // Update in each language
            foreach (var lang in data.Languages)
            {
                var langSection = lang.TableSheet.FirstOrDefault(ts => ts.Reference == sectionData);
                if (langSection == null) continue;

                int langOldIndex = lang.TableSheet.IndexOf(langSection);
                if (langOldIndex < 0) continue;

                // Clamp position
                int insertToLang = position > langOldIndex ? position - 1 : position;
                insertToLang = Mathf.Clamp(insertToLang, 0, lang.TableSheet.Count - 1);

                if (langOldIndex == insertToLang)
                    continue;

                lang.TableSheet.RemoveAt(langOldIndex);
                lang.TableSheet.Insert(insertToLang, langSection);
            }
        }

        public static void GenerateCode(GameLocaizationTable table)
        {
            string folderPath = table.GenerateScriptCodeFolderPath;
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                EditorUtility.DisplayDialog("Invalid Folder Path", "Localization key output folder path cannot be empty.", "OK");
                return;
            }

            string className = table.GenerateScriptCodeClassName;
            if (!IsValidIdentifierName(className))
            {
                EditorUtility.DisplayDialog("Invalid Class Name", "Localization key class name must be a valid C# identifier.", "OK");
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            var localizationLanguage = table.Languages.Find(t => t != null && t.LanguageName == LocalizationConfiguration.Instance.GenerateScriptCodeFirstConfigName);
            if (!TryBuildGeneratedKeyMetadata(table, localizationLanguage, out List<GeneratedLocalizationKey> generatedKeys, out string validationError))
            {
                EditorUtility.DisplayDialog("Invalid Localization Format", validationError, "OK");
                Debug.LogError(validationError);
                return;
            }

            string namespaceName = table.GenerateScriptCodeNamespace;
            string filePath = Path.Combine(folderPath, className + ".cs");

            StringBuilder sb = new StringBuilder();
            AppendGeneratedLine(sb, 0, "using AlicizaX;");
            AppendGeneratedLine(sb, 0, "using AlicizaX.Localization.Runtime;");
            AppendGeneratedLine(sb);

            int rootDepth = 0;
            if (!string.IsNullOrWhiteSpace(namespaceName))
            {
                AppendGeneratedLine(sb, 0, $"namespace {namespaceName}");
                AppendGeneratedLine(sb, 0, "{");
                rootDepth = 1;
            }

            AppendGeneratedXmlSummary(sb, rootDepth, "AutoGenerate");
            AppendGeneratedLine(sb, rootDepth, $"public static class {className}");
            AppendGeneratedLine(sb, rootDepth, "{");

            int classDepth = rootDepth + 1;
            int sectionDepth = classDepth + 1;
            AppendGeneratedLine(sb, classDepth, "private static ILocalizationService _localizationService;");
            AppendGeneratedLine(sb);
            AppendGeneratedLine(sb, classDepth, "private static ILocalizationService LocalizationService");
            AppendGeneratedLine(sb, classDepth, "{");
            AppendGeneratedLine(sb, classDepth + 1, "get");
            AppendGeneratedLine(sb, classDepth + 1, "{");
            AppendGeneratedLine(sb, classDepth + 2, "if (_localizationService == null)");
            AppendGeneratedLine(sb, classDepth + 2, "{");
            AppendGeneratedLine(sb, classDepth + 3, "_localizationService = AppServices.App.Require<ILocalizationService>();");
            AppendGeneratedLine(sb, classDepth + 2, "}");
            AppendGeneratedLine(sb);
            AppendGeneratedLine(sb, classDepth + 2, "return _localizationService;");
            AppendGeneratedLine(sb, classDepth + 1, "}");
            AppendGeneratedLine(sb, classDepth, "}");

            bool wroteSection = false;
            for (int i = 0; i < table.TableSheet.Count; i++)
            {
                var v = table.TableSheet[i];
                if (v.SectionSheet.FindIndex(t => t.IsGen) < 0) continue;
                if (!IsValidIdentifierName(v.SectionName))
                {
                    Debug.LogError($"Invalid localization section name for code generation: {v.SectionName}");
                    continue;
                }

                string sectionName = NormalizeKeyPart(v.SectionName);
                if (wroteSection)
                {
                    AppendGeneratedLine(sb);
                }
                else
                {
                    wroteSection = true;
                    AppendGeneratedLine(sb);
                }

                AppendGeneratedLine(sb, classDepth, $"public static class {sectionName}");
                AppendGeneratedLine(sb, classDepth, "{");

                bool wroteItem = false;
                for (int j = 0; j < v.SectionSheet.Count; j++)
                {
                    var item = v.SectionSheet[j];
                    if (!item.IsGen) continue;
                    string itemKey = NormalizeKeyPart(item.Key);
                    string combineKey = sectionName + "." + itemKey;

                    if (!IsValidKeyPath(item.Key))
                    {
                        Debug.LogError($"Invalid localization key for code generation: {item.Key}");
                        continue;
                    }

                    if (!TryFindGeneratedKey(generatedKeys, v.Id, item.Id, out GeneratedLocalizationKey generatedKey))
                    {
                        Debug.LogError($"Missing generated localization metadata: {combineKey}");
                        continue;
                    }

                    string varibleName = NormalizeKeyPart(item.Key).Replace(".", "_").ToUpperInvariant();
                    if (wroteItem)
                    {
                        AppendGeneratedLine(sb);
                    }

                    AppendGeneratedXmlSummary(sb, sectionDepth, EscapeXmlDocText(generatedKey.Comment));
                    if (generatedKey.ArgumentCount <= 0)
                    {
                        AppendGeneratedLine(sb, sectionDepth, $"public static string {varibleName} => LocalizationService.GetString(\"{EscapeCodeString(combineKey)}\");");
                    }
                    else
                    {
                        AppendGeneratedLine(sb, sectionDepth, $"public static string {varibleName}({BuildMethodParameters(generatedKey.ArgumentCount)})");
                        AppendGeneratedLine(sb, sectionDepth, "{");
                        AppendGeneratedLine(sb, sectionDepth + 1, $"return LocalizationService.GetString(\"{EscapeCodeString(combineKey)}\", {BuildMethodArguments(generatedKey.ArgumentCount)});");
                        AppendGeneratedLine(sb, sectionDepth, "}");
                    }

                    wroteItem = true;
                }

                AppendGeneratedLine(sb, classDepth, "}");
            }

            AppendGeneratedLine(sb, rootDepth, "}");

            if (!string.IsNullOrWhiteSpace(namespaceName))
            {
                AppendGeneratedLine(sb, 0, "}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();
        }

        private static void AppendGeneratedXmlSummary(StringBuilder sb, int depth, string summary)
        {
            AppendGeneratedLine(sb, depth, "/// <summary>");
            AppendGeneratedLine(sb, depth, $"/// {summary}");
            AppendGeneratedLine(sb, depth, "/// </summary>");
        }

        private static void AppendGeneratedLine(StringBuilder sb)
        {
            sb.AppendLine();
        }

        private static void AppendGeneratedLine(StringBuilder sb, int depth, string line)
        {
            for (int i = 0; i < depth; i++)
            {
                sb.Append(GeneratedIndent);
            }

            sb.AppendLine(line);
        }

        private sealed class GeneratedLocalizationKey
        {
            public int SectionId;
            public int EntryId;
            public string Key;
            public string Comment;
            public int ArgumentCount;
        }

        private static bool TryBuildGeneratedKeyMetadata(GameLocaizationTable table, LocalizationLanguage commentLanguage, out List<GeneratedLocalizationKey> generatedKeys, out string error)
        {
            generatedKeys = new List<GeneratedLocalizationKey>();
            error = null;
            if (commentLanguage == null)
            {
                error = $"Comment language [{LocalizationConfiguration.Instance.GenerateScriptCodeFirstConfigName}] is not available.";
                return false;
            }

            Dictionary<long, LocalizationLanguage.LocalizationString> commentLookup = commentLanguage != null
                ? BuildStringLookup(commentLanguage.Strings)
                : null;
            Dictionary<LocalizationLanguage, Dictionary<long, LocalizationLanguage.LocalizationString>> languageLookups = new(table.Languages.Count);
            List<int> basePlaceholders = new();
            List<int> languagePlaceholders = new();

            for (int i = 0; i < table.TableSheet.Count; i++)
            {
                GameLocaizationTable.TableData section = table.TableSheet[i];
                string sectionName = NormalizeKeyPart(section.SectionName);

                for (int j = 0; j < section.SectionSheet.Count; j++)
                {
                    GameLocaizationTable.SheetItem item = section.SectionSheet[j];
                    if (!item.IsGen)
                    {
                        continue;
                    }

                    string itemKey = NormalizeKeyPart(item.Key);
                    string combineKey = sectionName + "." + itemKey;
                    long lookupKey = MakeEntryLookupKey(section.Id, item.Id);
                    string comment = combineKey;
                    if (commentLookup != null && commentLookup.TryGetValue(lookupKey, out LocalizationLanguage.LocalizationString commentString) && commentString != null)
                    {
                        comment = commentString.Value;
                    }

                    if (!TryGetFormatPlaceholderSequence(comment, basePlaceholders, out string commentError))
                    {
                        error = $"Language [{commentLanguage?.LanguageName ?? LocalizationConfiguration.Instance.GenerateScriptCodeFirstConfigName}] key [{combineKey}] has invalid format placeholders: {commentError}";
                        return false;
                    }

                    if (!IsContinuousFormatPlaceholderSequence(basePlaceholders))
                    {
                        error = $"Language [{commentLanguage?.LanguageName ?? LocalizationConfiguration.Instance.GenerateScriptCodeFirstConfigName}] key [{combineKey}] format placeholders must be ordered as {{0}}, {{1}}, ...";
                        return false;
                    }

                    int argumentCount = GetFormatPlaceholderArgumentCount(basePlaceholders);
                    if (argumentCount > MaxGeneratedLocalizationArguments)
                    {
                        error = $"Language [{commentLanguage?.LanguageName ?? LocalizationConfiguration.Instance.GenerateScriptCodeFirstConfigName}] key [{combineKey}] uses {argumentCount} arguments, but generated localization keys support at most {MaxGeneratedLocalizationArguments}.";
                        return false;
                    }

                    for (int languageIndex = 0; languageIndex < table.Languages.Count; languageIndex++)
                    {
                        LocalizationLanguage language = table.Languages[languageIndex];
                        if (language == null || !LocalizationConfiguration.Instance.IsLanguageSelected(language.LanguageName))
                        {
                            continue;
                        }

                        if (!languageLookups.TryGetValue(language, out Dictionary<long, LocalizationLanguage.LocalizationString> lookup))
                        {
                            lookup = BuildStringLookup(language.Strings);
                            languageLookups.Add(language, lookup);
                        }

                        if (!lookup.TryGetValue(lookupKey, out LocalizationLanguage.LocalizationString localizedString) || localizedString == null)
                        {
                            continue;
                        }

                        if (!TryGetFormatPlaceholderSequence(localizedString.Value, languagePlaceholders, out string languageError))
                        {
                            error = $"Language [{language.LanguageName}] key [{combineKey}] has invalid format placeholders: {languageError}";
                            return false;
                        }

                        if (!FormatPlaceholderSequenceEquals(basePlaceholders, languagePlaceholders))
                        {
                            error = $"Language [{language.LanguageName}] key [{combineKey}] format placeholders must match comment language sequence.";
                            return false;
                        }
                    }

                    generatedKeys.Add(new GeneratedLocalizationKey()
                    {
                        SectionId = section.Id,
                        EntryId = item.Id,
                        Key = combineKey,
                        Comment = comment,
                        ArgumentCount = argumentCount
                    });
                }
            }

            return true;
        }

        private static bool TryFindGeneratedKey(List<GeneratedLocalizationKey> generatedKeys, int sectionId, int entryId, out GeneratedLocalizationKey generatedKey)
        {
            for (int i = 0; i < generatedKeys.Count; i++)
            {
                GeneratedLocalizationKey candidate = generatedKeys[i];
                if (candidate.SectionId == sectionId && candidate.EntryId == entryId)
                {
                    generatedKey = candidate;
                    return true;
                }
            }

            generatedKey = null;
            return false;
        }

        private static string BuildMethodParameters(int count)
        {
            StringBuilder sb = new();
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append("string arg");
                sb.Append(i + 1);
            }

            return sb.ToString();
        }

        private static string BuildMethodArguments(int count)
        {
            StringBuilder sb = new();
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append("arg");
                sb.Append(i + 1);
            }

            return sb.ToString();
        }

        private static Dictionary<long, LocalizationLanguage.LocalizationString> BuildStringLookup(List<LocalizationLanguage.LocalizationString> strings)
        {
            if (strings == null)
            {
                return new Dictionary<long, LocalizationLanguage.LocalizationString>(0);
            }

            Dictionary<long, LocalizationLanguage.LocalizationString> lookup = new(strings.Count);
            for (int i = 0; i < strings.Count; i++)
            {
                LocalizationLanguage.LocalizationString item = strings[i];
                lookup[MakeEntryLookupKey(item.SectionId, item.EntryId)] = item;
            }

            return lookup;
        }

        private static long MakeEntryLookupKey(int sectionId, int entryId)
        {
            return ((long)sectionId << 32) ^ (uint)entryId;
        }
    }
}
