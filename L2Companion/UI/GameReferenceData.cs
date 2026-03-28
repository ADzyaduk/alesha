using System.IO;
using System.Text.Json;
using L2Companion.Core;

namespace L2Companion.UI;

public sealed class GameReferenceData
{
    private readonly Dictionary<int, string> _skills;
    private readonly Dictionary<int, string> _items;
    private readonly Dictionary<int, string> _npcs;

    public string SkillSourcePath { get; }
    public string ItemSourcePath { get; }
    public string NpcSourcePath { get; }

    public GameReferenceData(string appBaseDir, LogService log)
    {
        (SkillSourcePath, _skills) = LoadSkillMap(appBaseDir, log);
        (ItemSourcePath, _items) = LoadItemMap(appBaseDir, log);
        (NpcSourcePath, _npcs) = LoadNpcMap(appBaseDir, log);
    }

    public string ResolveSkillName(int skillId)
    {
        return _skills.TryGetValue(skillId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"Skill {skillId}";
    }

    public string ResolveItemName(int itemId)
    {
        return _items.TryGetValue(itemId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"Item {itemId}";
    }

    public string ResolveNpcName(int npcId)
    {
        return _npcs.TryGetValue(npcId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"NPC {npcId}";
    }

    public IReadOnlyList<(int SkillId, string Name)> GetSkillCatalog(int limit = 1500)
    {
        IEnumerable<KeyValuePair<int, string>> query = _skills.OrderBy(x => x.Key);
        if (limit > 0)
        {
            query = query.Take(limit);
        }

        return query.Select(x => (x.Key, x.Value)).ToList();
    }

    public IReadOnlyList<(int SkillId, string Name)> SearchSkills(string query, int limit = 200)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0)
        {
            return GetSkillCatalog(limit);
        }

        var byId = int.TryParse(q, out var idFilter);

        return _skills
            .Where(x =>
                (byId && x.Key == idFilter)
                || x.Key.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
                || x.Value.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Key)
            .Take(Math.Max(1, limit))
            .Select(x => (x.Key, x.Value))
            .ToList();
    }

    private static (string sourcePath, Dictionary<int, string> map) LoadSkillMap(string appBaseDir, LogService log)
    {
        var json = LoadJsonMap(appBaseDir, "skills_en.json", log);
        if (json.map.Count > 0)
        {
            return json;
        }

        return LoadL2NetTxtMap("skillname.txt", appBaseDir, log, 2, 1);
    }

    private static (string sourcePath, Dictionary<int, string> map) LoadItemMap(string appBaseDir, LogService log)
    {
        var json = LoadJsonMap(appBaseDir, "items_en.json", log);
        if (json.map.Count > 0)
        {
            return json;
        }

        var primary = LoadL2NetTxtMap("itemname.txt", appBaseDir, log, 0, 1);
        if (primary.map.Count > 0)
        {
            return primary;
        }

        return LoadL2NetTxtMap("itemname_.txt", appBaseDir, log, 0, 1);
    }

    private static (string sourcePath, Dictionary<int, string> map) LoadNpcMap(string appBaseDir, LogService log)
    {
        var primary = LoadL2NetTxtMap("npcname.txt", appBaseDir, log, 0, 1);
        if (primary.map.Count > 0)
        {
            return primary;
        }

        return LoadL2NetTxtMap("npcstring.txt", appBaseDir, log, 0, 1);
    }

    private static (string sourcePath, Dictionary<int, string> map) LoadJsonMap(string appBaseDir, string fileName, LogService log)
    {
        var candidates = new[]
        {
            Path.Combine(appBaseDir, "Config", fileName),
            Path.Combine(appBaseDir, "data", fileName),
            Path.Combine(appBaseDir, "..", "..", "..", "..", "Новая папка", "data", fileName),
            Path.Combine("C:\\pj\\Новая папка\\data", fileName)
        };

        foreach (var candidate in candidates)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate);
            }
            catch
            {
                continue;
            }

            if (!File.Exists(fullPath))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(fullPath);
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (raw is null || raw.Count == 0)
                {
                    continue;
                }

                var map = new Dictionary<int, string>(raw.Count);
                foreach (var (key, value) in raw)
                {
                    if (int.TryParse(key, out var id) && id > 0 && !string.IsNullOrWhiteSpace(value))
                    {
                        map[id] = value.Trim();
                    }
                }

                log.Info($"Loaded {fileName}: {map.Count} entries from {fullPath}");
                return (fullPath, map);
            }
            catch (Exception ex)
            {
                log.Info($"Failed to read {fileName} at {fullPath}: {ex.Message}");
            }
        }

        return ("-", new Dictionary<int, string>());
    }

    private static (string sourcePath, Dictionary<int, string> map) LoadL2NetTxtMap(string fileName, string appBaseDir, LogService log, int idColumn, int nameColumn)
    {
        var candidates = new[]
        {
            Path.Combine("C:\\pj\\L2Net\\Data", fileName),
            Path.Combine(appBaseDir, "..", "..", "..", "..", "L2Net", "Data", fileName)
        };

        foreach (var candidate in candidates)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate);
            }
            catch
            {
                continue;
            }

            if (!File.Exists(fullPath))
            {
                continue;
            }

            try
            {
                var map = ParseL2NetTable(fullPath, idColumn, nameColumn);
                if (map.Count == 0)
                {
                    continue;
                }

                log.Info($"Loaded {fileName}: {map.Count} entries from {fullPath}");
                return (fullPath, map);
            }
            catch (Exception ex)
            {
                log.Info($"Failed to read {fileName} at {fullPath}: {ex.Message}");
            }
        }

        log.Info($"Reference file {fileName} not found, using IDs only.");
        return ("-", new Dictionary<int, string>());
    }

    private static Dictionary<int, string> ParseL2NetTable(string path, int idColumn, int nameColumn)
    {
        var map = new Dictionary<int, string>();

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line
                .Split(['\t', ';', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().Trim('"'))
                .Where(x => x.Length > 0)
                .ToArray();

            if (parts.Length <= Math.Max(idColumn, nameColumn))
            {
                continue;
            }

            if (!int.TryParse(parts[idColumn], out var id) || id <= 0)
            {
                continue;
            }

            var name = parts[nameColumn].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            map[id] = name;
        }

        return map;
    }
}
