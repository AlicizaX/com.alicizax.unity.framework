using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Localization.Editor
{
    public static class LocalizationExporter
    {
        public static void ExportLocalizationToCSV(LocalizationWindowData windowData, string filePath)
        {
            if (windowData == null || windowData.Languages.Count == 0)
            {
                Debug.LogError("Localization window data is empty.");
                return;
            }

            var allKeys = new List<string>();
            foreach (var section in windowData.TableSheet)
            {
                foreach (var item in section.Items)
                {
                    string key = $"{section.Id}:{item.Id}";
                    allKeys.Add(key);
                }
            }

            var languageLookups = windowData.Languages.ToDictionary(
                lang => lang.Entry.LanguageName,
                lang => lang.TableSheet.SelectMany(section => section.Items)
                                       .ToDictionary(item => $"{item.Parent.Id}:{item.Id}", item => item.Value)
            );

            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {

                writer.Write("Key");
                foreach (var lang in windowData.Languages)
                {
                    writer.Write(",");
                    WriteEscapedCsvField(writer, lang.Entry.LanguageName);
                }
                writer.WriteLine();

                foreach (var key in allKeys)
                {
                    WriteEscapedCsvField(writer, key);
                    foreach (var lang in windowData.Languages)
                    {
                        writer.Write(",");
                        var translations = languageLookups[lang.Entry.LanguageName];
                        string value = translations.ContainsKey(key) ? translations[key] : "";
                        WriteEscapedCsvField(writer, value);
                    }
                    writer.WriteLine();
                }
            }

            Debug.Log($"Localization exported successfully to: {filePath}");
        }


        private static void WriteEscapedCsvField(StreamWriter writer, string field)
        {
            if (string.IsNullOrEmpty(field))
            {
                return;
            }


            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {

                writer.Write("\"");
                writer.Write(field.Replace("\"", "\"\""));
                writer.Write("\"");
            }
            else
            {
                writer.Write(field);
            }
        }

        public static void ImportLocalizationFromCSV(LocalizationWindowData windowData, string filePath)
        {
            if (!File.Exists(filePath))
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    return;
                }

                Debug.LogError($"CSV file not found: {filePath}");
                return;
            }

            using (var reader = new StreamReader(filePath, Encoding.UTF8))
            {

                string headerLine = ReadCsvRecord(reader);
                if (headerLine == null)
                {
                    Debug.LogError("CSV file is empty");
                    return;
                }

                var headers = ParseCsvLine(headerLine);
                var languageNames = headers.Skip(1)
                    .Where(LocalizationConfiguration.Instance.IsLanguageSelected)
                    .ToList();


                var existingLanguages = windowData.Languages
                    .Where(l => languageNames.Contains(l.Entry.LanguageName))
                    .ToDictionary(l => l.Entry.LanguageName);

                if (existingLanguages.Count == 0)
                {
                    Debug.LogError("No matching languages in the Localization Window Data.");
                    return;
                }

                int updatedCount = 0;
                int missingKeyCount = 0;
                int invalidKeyCount = 0;

                string line;
                while ((line = ReadCsvRecord(reader)) != null)
                {
                    var fields = ParseCsvLine(line);
                    if (fields.Length == 0) continue;

                    string key = fields[0];
                    string[] keyParts = key.Split(':');

                    // Get sectionId and itemId
                    if (keyParts.Length != 2 ||
                        !int.TryParse(keyParts[0], out int sectionId) ||
                        !int.TryParse(keyParts[1], out int itemId))
                    {
                        invalidKeyCount++;
                        Debug.LogWarning($"Invalid key format: {key}");
                        continue;
                    }

                    for (int i = 0; i < languageNames.Count; i++)
                    {
                        if (i + 1 >= fields.Length) break;

                        string languageName = languageNames[i];
                        if (existingLanguages.TryGetValue(languageName, out var languageData))
                        {
                            string value = fields[i + 1];

                            var matchingSection = languageData.TableSheet.FirstOrDefault(sec => sec.Id == sectionId);
                            var matchingItem = matchingSection?.Items.FirstOrDefault(item => item.Id == itemId);
                            if (matchingItem != null)
                            {
                                matchingItem.Value = value;
                                updatedCount++;
                            }
                            else
                            {
                                missingKeyCount++;
                            }
                        }
                    }
                }

                EditorUtility.DisplayDialog(
                    "CSV Import Summary",
                    $"Updated: {updatedCount}\nMissing keys skipped: {missingKeyCount}\nInvalid rows skipped: {invalidKeyCount}",
                    "OK");
            }

            Debug.Log("Localization CSV imported successfully into existing languages.");
        }

        private static string[] ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var currentField = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"' && (i == 0 || line[i - 1] != '\\'))
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }

            fields.Add(currentField.ToString());

            return fields.ToArray();
        }

        private static string ReadCsvRecord(StreamReader reader)
        {
            string line = reader.ReadLine();
            if (line == null)
            {
                return null;
            }

            StringBuilder record = new StringBuilder(line);
            while (HasOpenQuotedField(record))
            {
                string nextLine = reader.ReadLine();
                if (nextLine == null)
                {
                    break;
                }

                record.Append('\n');
                record.Append(nextLine);
            }

            return record.ToString();
        }

        private static bool HasOpenQuotedField(StringBuilder record)
        {
            bool inQuotes = false;
            for (int i = 0; i < record.Length; i++)
            {
                char c = record[i];
                if (c != '"')
                {
                    continue;
                }

                if (inQuotes && i + 1 < record.Length && record[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
            }

            return inQuotes;
        }

    }
}
