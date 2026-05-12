using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AlicizaX.Localization.Editor
{
    /// <summary>
    /// 本地化表的纯静态编程接口，不含任何 GUI 或 MenuItem。
    /// 所有写操作会自动调用 EditorUtility.SetDirty，批量编辑完成后调用一次 SaveTable() 即可。
    /// </summary>
    public static class LocalizationEditorTools
    {
        // ────────────────────────────────────────────────────────────────
        // 结果类型
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 重复 key 报告中的单条出现记录。
        /// </summary>
        public sealed class DuplicateKeyOccurrence
        {
            public GameLocaizationTable Table;
            public int SectionId;
            public int EntryId;
        }

        /// <summary>
        /// 在扫描范围内出现超过一次的完整路径 key（SectionName.Key），以及每次出现的位置。
        /// </summary>
        public sealed class DuplicateKeyReport
        {
            public string NormalizedKey;
            public List<DuplicateKeyOccurrence> Occurrences = new();
        }

        // ────────────────────────────────────────────────────────────────
        // 资产发现
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 返回项目中所有 GameLocaizationTable 资产。
        /// </summary>
        public static List<GameLocaizationTable> FindAllTables()
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(GameLocaizationTable)}");
            var result = new List<GameLocaizationTable>(guids.Length);
            foreach (string guid in guids)
            {
                var table = AssetDatabase.LoadAssetAtPath<GameLocaizationTable>(AssetDatabase.GUIDToAssetPath(guid));
                if (table != null) result.Add(table);
            }
            return result;
        }

        // ────────────────────────────────────────────────────────────────
        // 重复 key 检测
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 查找单张表内重复的完整路径 key（SectionName.Key）。
        /// 可传入可选的 <paramref name="filter"/> 子字符串进行过滤（大小写不敏感）。
        /// </summary>
        public static List<DuplicateKeyReport> FindDuplicateKeys(
            GameLocaizationTable table, string filter = null)
            => FindDuplicateKeysAcrossTables(new[] { table }, filter);

        /// <summary>
        /// 跨多张表查找重复的完整路径 key。
        /// <paramref name="tables"/> 传 <c>null</c> 则扫描项目中所有表。
        /// 可传入可选的 <paramref name="filter"/> 子字符串进行过滤（大小写不敏感）。
        /// </summary>
        public static List<DuplicateKeyReport> FindDuplicateKeysAcrossTables(
            IEnumerable<GameLocaizationTable> tables = null, string filter = null)
        {
            if (tables == null) tables = FindAllTables();

            var keyMap = new Dictionary<string, DuplicateKeyReport>(StringComparer.Ordinal);

            foreach (var table in tables)
            {
                if (table == null) continue;
                foreach (var section in table.TableSheet)
                {
                    string sn = LocalizationWindowUtility.NormalizeKeyPart(section.SectionName);
                    foreach (var item in section.SectionSheet)
                    {
                        string fullKey = sn + "." + LocalizationWindowUtility.NormalizeKeyPart(item.Key);

                        if (!string.IsNullOrEmpty(filter) &&
                            fullKey.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        if (!keyMap.TryGetValue(fullKey, out var report))
                            keyMap[fullKey] = report = new DuplicateKeyReport { NormalizedKey = fullKey };

                        report.Occurrences.Add(new DuplicateKeyOccurrence
                        {
                            Table = table,
                            SectionId = section.Id,
                            EntryId = item.Id,
                        });
                    }
                }
            }

            var result = new List<DuplicateKeyReport>();
            foreach (var kv in keyMap)
                if (kv.Value.Occurrences.Count > 1) result.Add(kv.Value);
            return result;
        }

        // ────────────────────────────────────────────────────────────────
        // 查询
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 返回表中所有规范化完整路径 key（SectionName.Key）。
        /// </summary>
        public static List<string> GetAllKeys(GameLocaizationTable table)
        {
            var keys = new List<string>();
            foreach (var section in table.TableSheet)
            {
                string sn = LocalizationWindowUtility.NormalizeKeyPart(section.SectionName);
                foreach (var item in section.SectionSheet)
                    keys.Add(sn + "." + LocalizationWindowUtility.NormalizeKeyPart(item.Key));
            }
            return keys;
        }

        /// <summary>
        /// 判断指定名称的 section 是否存在（规范化比较，忽略空格）。
        /// </summary>
        public static bool ContainsSection(GameLocaizationTable table, string sectionName)
        {
            string norm = LocalizationWindowUtility.NormalizeKeyPart(sectionName);
            return table.TableSheet.Exists(s =>
                string.Equals(LocalizationWindowUtility.NormalizeKeyPart(s.SectionName), norm, StringComparison.Ordinal));
        }

        /// <summary>
        /// 判断指定 section 下是否存在给定 key（规范化比较，忽略空格）。
        /// </summary>
        public static bool ContainsKey(GameLocaizationTable table, string sectionName, string key)
        {
            if (!TryFindSectionIndex(table, sectionName, out int sIdx)) return false;
            string normKey = LocalizationWindowUtility.NormalizeKeyPart(key);
            return table.TableSheet[sIdx].SectionSheet.Exists(i =>
                string.Equals(LocalizationWindowUtility.NormalizeKeyPart(i.Key), normKey, StringComparison.Ordinal));
        }

        /// <summary>
        /// 读取某条 entry 在指定语言下的翻译值。
        /// </summary>
        public static bool TryGetValue(GameLocaizationTable table, string sectionName, string key,
            string languageName, out string value)
        {
            value = null;
            string fullKey = LocalizationWindowUtility.NormalizeKeyPart(sectionName)
                           + "." + LocalizationWindowUtility.NormalizeKeyPart(key);
            var lang = FindLanguage(table, languageName);
            if (lang == null) return false;
            var str = lang.Strings.Find(s => string.Equals(s.Key, fullKey, StringComparison.Ordinal));
            if (str == null) return false;
            value = str.Value;
            return true;
        }

        // ────────────────────────────────────────────────────────────────
        // Section 操作
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 新增一个 section，自动生成不重复的 ID。
        /// section 本身不携带语言数据，只有其下的 entry 才写入语言资产。
        /// 编辑完成后调用 SaveTable()。
        /// </summary>
        public static bool TryAddSection(GameLocaizationTable table, string sectionName, out string error)
        {
            if (!LocalizationWindowUtility.IsValidIdentifierName(sectionName))
            {
                error = "Section 名称去除空格后必须是合法的 C# 标识符。";
                return false;
            }

            string norm = LocalizationWindowUtility.NormalizeKeyPart(sectionName);
            if (table.TableSheet.Exists(s =>
                    string.Equals(LocalizationWindowUtility.NormalizeKeyPart(s.SectionName), norm, StringComparison.Ordinal)))
            {
                error = $"Section '{sectionName}' 已存在。";
                return false;
            }

            int id = GenerateUniqueId(CollectSectionIds(table));
            table.TableSheet.Add(new GameLocaizationTable.TableData(sectionName, id));
            EditorUtility.SetDirty(table);
            error = null;
            return true;
        }

        /// <summary>
        /// 删除指定 section 及其所有 entry，同时从所有语言资产中移除对应数据。
        /// </summary>
        public static bool TryRemoveSection(GameLocaizationTable table, string sectionName, out string error)
        {
            if (!TryFindSectionIndex(table, sectionName, out int idx))
            {
                error = $"Section '{sectionName}' 不存在。";
                return false;
            }

            int sectionId = table.TableSheet[idx].Id;
            table.TableSheet.RemoveAt(idx);

            foreach (var lang in table.Languages)
            {
                if (lang == null) continue;
                lang.Strings.RemoveAll(s => s.SectionId == sectionId);
                EditorUtility.SetDirty(lang);
            }

            EditorUtility.SetDirty(table);
            error = null;
            return true;
        }

        /// <summary>
        /// 重命名 section，并同步更新所有语言资产中对应条目的 Key 前缀。
        /// </summary>
        public static bool TryRenameSection(GameLocaizationTable table, string oldName, string newName, out string error)
        {
            if (!LocalizationWindowUtility.IsValidIdentifierName(newName))
            {
                error = "新名称去除空格后必须是合法的 C# 标识符。";
                return false;
            }

            if (!TryFindSectionIndex(table, oldName, out int idx))
            {
                error = $"Section '{oldName}' 不存在。";
                return false;
            }

            string normOld = LocalizationWindowUtility.NormalizeKeyPart(oldName);
            string normNew = LocalizationWindowUtility.NormalizeKeyPart(newName);

            if (!string.Equals(normOld, normNew, StringComparison.Ordinal) &&
                table.TableSheet.Exists(s =>
                    string.Equals(LocalizationWindowUtility.NormalizeKeyPart(s.SectionName), normNew, StringComparison.Ordinal)))
            {
                error = $"Section '{newName}' 已存在。";
                return false;
            }

            int sectionId = table.TableSheet[idx].Id;

            // TableData 是 struct，需要整体取出修改后写回
            var td = table.TableSheet[idx];
            td.SectionName = newName;
            table.TableSheet[idx] = td;

            // 更新所有语言资产中该 section 下所有条目的 Key 前缀
            foreach (var lang in table.Languages)
            {
                if (lang == null) continue;
                bool changed = false;
                foreach (var str in lang.Strings)
                {
                    if (str.SectionId != sectionId) continue;
                    int dot = str.Key.IndexOf('.');
                    str.Key = dot >= 0 ? normNew + str.Key.Substring(dot) : normNew;
                    changed = true;
                }
                if (changed) EditorUtility.SetDirty(lang);
            }

            EditorUtility.SetDirty(table);
            error = null;
            return true;
        }

        // ────────────────────────────────────────────────────────────────
        // Entry 操作
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 向指定 section 添加一条 entry，自动生成不重复的 ID，
        /// 并向所有语言资产写入空值占位条目。
        /// </summary>
        public static bool TryAddEntry(GameLocaizationTable table, string sectionName,
            string key, out string error, bool isGen = true)
        {
            if (!LocalizationWindowUtility.IsValidKeyPath(key))
            {
                error = "Key 必须是以点分隔的合法 C# 标识符路径。";
                return false;
            }

            if (!TryFindSectionIndex(table, sectionName, out int sIdx))
            {
                error = $"Section '{sectionName}' 不存在，请先调用 TryAddSection。";
                return false;
            }

            string normKey = LocalizationWindowUtility.NormalizeKeyPart(key);
            if (table.TableSheet[sIdx].SectionSheet.Exists(i =>
                    string.Equals(LocalizationWindowUtility.NormalizeKeyPart(i.Key), normKey, StringComparison.Ordinal)))
            {
                error = $"Key '{key}' 在 section '{sectionName}' 中已存在。";
                return false;
            }

            int entryId = GenerateUniqueId(CollectEntryIds(table));
            int sectionId = table.TableSheet[sIdx].Id;
            string normSection = LocalizationWindowUtility.NormalizeKeyPart(table.TableSheet[sIdx].SectionName);
            string fullKey = normSection + "." + normKey;

            // SectionSheet 是 struct 内的 List 引用，直接 Add 即可生效
            table.TableSheet[sIdx].SectionSheet.Add(new GameLocaizationTable.SheetItem(key, entryId, isGen));

            foreach (var lang in table.Languages)
            {
                if (lang == null) continue;
                lang.Strings.Add(new LocalizationLanguage.LocalizationString
                {
                    SectionId = sectionId,
                    EntryId = entryId,
                    Key = fullKey,
                    Value = string.Empty,
                });
                EditorUtility.SetDirty(lang);
            }

            EditorUtility.SetDirty(table);
            error = null;
            return true;
        }

        /// <summary>
        /// 从指定 section 删除一条 entry，并从所有语言资产中移除对应数据。
        /// </summary>
        public static bool TryRemoveEntry(GameLocaizationTable table, string sectionName, string key, out string error)
        {
            if (!TryFindSectionIndex(table, sectionName, out int sIdx))
            {
                error = $"Section '{sectionName}' 不存在。";
                return false;
            }

            string normKey = LocalizationWindowUtility.NormalizeKeyPart(key);
            int itemIdx = table.TableSheet[sIdx].SectionSheet.FindIndex(i =>
                string.Equals(LocalizationWindowUtility.NormalizeKeyPart(i.Key), normKey, StringComparison.Ordinal));
            if (itemIdx < 0)
            {
                error = $"Key '{key}' 在 section '{sectionName}' 中不存在。";
                return false;
            }

            int sectionId = table.TableSheet[sIdx].Id;
            int entryId = table.TableSheet[sIdx].SectionSheet[itemIdx].Id;

            table.TableSheet[sIdx].SectionSheet.RemoveAt(itemIdx);

            foreach (var lang in table.Languages)
            {
                if (lang == null) continue;
                lang.Strings.RemoveAll(s => s.SectionId == sectionId && s.EntryId == entryId);
                EditorUtility.SetDirty(lang);
            }

            EditorUtility.SetDirty(table);
            error = null;
            return true;
        }

        /// <summary>
        /// 重命名 section 内的一条 entry key，并同步更新所有语言资产中的 Key 字段。
        /// </summary>
        public static bool TryRenameEntry(GameLocaizationTable table, string sectionName,
            string oldKey, string newKey, out string error)
        {
            if (!LocalizationWindowUtility.IsValidKeyPath(newKey))
            {
                error = "新 Key 必须是以点分隔的合法 C# 标识符路径。";
                return false;
            }

            if (!TryFindSectionIndex(table, sectionName, out int sIdx))
            {
                error = $"Section '{sectionName}' 不存在。";
                return false;
            }

            string normOld = LocalizationWindowUtility.NormalizeKeyPart(oldKey);
            string normNew = LocalizationWindowUtility.NormalizeKeyPart(newKey);

            int itemIdx = table.TableSheet[sIdx].SectionSheet.FindIndex(i =>
                string.Equals(LocalizationWindowUtility.NormalizeKeyPart(i.Key), normOld, StringComparison.Ordinal));
            if (itemIdx < 0)
            {
                error = $"Key '{oldKey}' 在 section '{sectionName}' 中不存在。";
                return false;
            }

            if (!string.Equals(normOld, normNew, StringComparison.Ordinal) &&
                table.TableSheet[sIdx].SectionSheet.Exists(i =>
                    string.Equals(LocalizationWindowUtility.NormalizeKeyPart(i.Key), normNew, StringComparison.Ordinal)))
            {
                error = $"Key '{newKey}' 在 section '{sectionName}' 中已存在。";
                return false;
            }

            int sectionId = table.TableSheet[sIdx].Id;
            int entryId = table.TableSheet[sIdx].SectionSheet[itemIdx].Id;
            string normSection = LocalizationWindowUtility.NormalizeKeyPart(table.TableSheet[sIdx].SectionName);
            string newFullKey = normSection + "." + normNew;

            // SheetItem 是 struct，需要整体取出修改后写回
            var si = table.TableSheet[sIdx].SectionSheet[itemIdx];
            si.Key = newKey;
            table.TableSheet[sIdx].SectionSheet[itemIdx] = si;

            foreach (var lang in table.Languages)
            {
                if (lang == null) continue;
                bool changed = false;
                foreach (var str in lang.Strings)
                {
                    if (str.SectionId != sectionId || str.EntryId != entryId) continue;
                    str.Key = newFullKey;
                    changed = true;
                }
                if (changed) EditorUtility.SetDirty(lang);
            }

            EditorUtility.SetDirty(table);
            error = null;
            return true;
        }

        /// <summary>
        /// 设置某条 entry 在指定语言下的翻译值。
        /// </summary>
        public static bool TrySetValue(GameLocaizationTable table, string sectionName, string key,
            string languageName, string value, out string error)
        {
            if (!TryFindSectionIndex(table, sectionName, out int sIdx))
            {
                error = $"Section '{sectionName}' 不存在。";
                return false;
            }

            string normKey = LocalizationWindowUtility.NormalizeKeyPart(key);
            int itemIdx = table.TableSheet[sIdx].SectionSheet.FindIndex(i =>
                string.Equals(LocalizationWindowUtility.NormalizeKeyPart(i.Key), normKey, StringComparison.Ordinal));
            if (itemIdx < 0)
            {
                error = $"Key '{key}' 在 section '{sectionName}' 中不存在。";
                return false;
            }

            var lang = FindLanguage(table, languageName);
            if (lang == null)
            {
                error = $"语言 '{languageName}' 不存在。";
                return false;
            }

            int sectionId = table.TableSheet[sIdx].Id;
            int entryId = table.TableSheet[sIdx].SectionSheet[itemIdx].Id;

            var str = lang.Strings.Find(s => s.SectionId == sectionId && s.EntryId == entryId);
            if (str == null)
            {
                error = $"语言 '{languageName}' 中未找到 '{sectionName}.{key}'，"
                      + "语言资产可能与表不同步，请在 Localization 编辑窗口重新保存。";
                return false;
            }

            str.Value = value;
            EditorUtility.SetDirty(lang);
            error = null;
            return true;
        }

        /// <summary>
        /// 开启或关闭某条 entry 的代码生成开关（IsGen）。
        /// </summary>
        public static bool TrySetEntryIsGen(GameLocaizationTable table, string sectionName,
            string key, bool isGen, out string error)
        {
            if (!TryFindSectionIndex(table, sectionName, out int sIdx))
            {
                error = $"Section '{sectionName}' 不存在。";
                return false;
            }

            string normKey = LocalizationWindowUtility.NormalizeKeyPart(key);
            int itemIdx = table.TableSheet[sIdx].SectionSheet.FindIndex(i =>
                string.Equals(LocalizationWindowUtility.NormalizeKeyPart(i.Key), normKey, StringComparison.Ordinal));
            if (itemIdx < 0)
            {
                error = $"Key '{key}' 在 section '{sectionName}' 中不存在。";
                return false;
            }

            // SheetItem 是 struct，需要整体取出修改后写回
            var si = table.TableSheet[sIdx].SectionSheet[itemIdx];
            si.IsGen = isGen;
            table.TableSheet[sIdx].SectionSheet[itemIdx] = si;

            EditorUtility.SetDirty(table);
            error = null;
            return true;
        }

        // ────────────────────────────────────────────────────────────────
        // 批量辅助
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 确保指定 section 存在，不存在则创建。
        /// 仅当名称本身不合法时返回 false。
        /// </summary>
        public static bool TryEnsureSection(GameLocaizationTable table, string sectionName, out string error)
        {
            if (ContainsSection(table, sectionName))
            {
                error = null;
                return true;
            }
            return TryAddSection(table, sectionName, out error);
        }

        /// <summary>
        /// 确保指定 section 下的 entry 存在，不存在则创建。
        /// section 不存在或名称/key 不合法时返回 false。
        /// </summary>
        public static bool TryEnsureEntry(GameLocaizationTable table, string sectionName,
            string key, out string error, bool isGen = true)
        {
            if (ContainsKey(table, sectionName, key))
            {
                error = null;
                return true;
            }
            return TryAddEntry(table, sectionName, key, out error, isGen);
        }

        // ────────────────────────────────────────────────────────────────
        // 保存 / 代码生成
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 将表及其所有语言资产标记为 dirty，调用 AssetDatabase.SaveAssets，
        /// 并使运行时语言查找缓存失效。
        /// </summary>
        public static void SaveTable(GameLocaizationTable table, bool refreshAssets = true)
        {
            EditorUtility.SetDirty(table);
            foreach (var lang in table.Languages)
                if (lang != null) EditorUtility.SetDirty(lang);

            AssetDatabase.SaveAssets();
            if (refreshAssets) AssetDatabase.Refresh();
            table.InvalidateLanguageLookup();
        }

        /// <summary>
        /// 触发 C# key 类代码生成，与编辑器中的 Gen Code 按钮完全等价。
        /// </summary>
        public static void GenerateCode(GameLocaizationTable table)
            => LocalizationWindowUtility.GenerateCode(table);

        // ────────────────────────────────────────────────────────────────
        // 私有辅助
        // ────────────────────────────────────────────────────────────────

        private static bool TryFindSectionIndex(GameLocaizationTable table, string sectionName, out int index)
        {
            string norm = LocalizationWindowUtility.NormalizeKeyPart(sectionName);
            index = table.TableSheet.FindIndex(s =>
                string.Equals(LocalizationWindowUtility.NormalizeKeyPart(s.SectionName), norm, StringComparison.Ordinal));
            return index >= 0;
        }

        private static LocalizationLanguage FindLanguage(GameLocaizationTable table, string languageName)
            => table.Languages.Find(l =>
                l != null && string.Equals(l.LanguageName, languageName, StringComparison.Ordinal));

        private static HashSet<int> CollectSectionIds(GameLocaizationTable table)
        {
            var ids = new HashSet<int>(table.TableSheet.Count);
            foreach (var s in table.TableSheet) ids.Add(s.Id);
            return ids;
        }

        private static HashSet<int> CollectEntryIds(GameLocaizationTable table)
        {
            var ids = new HashSet<int>();
            foreach (var s in table.TableSheet)
                foreach (var i in s.SectionSheet) ids.Add(i.Id);
            return ids;
        }

        private static int GenerateUniqueId(HashSet<int> existingIds)
        {
            int id;
            do { id = Random.Range(10000000, 99999999); } while (existingIds.Contains(id));
            return id;
        }
    }
}
