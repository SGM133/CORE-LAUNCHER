using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CoreLauncher.Core;

public class ModItem
{
    public string FilePath { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Enabled { get; init; }
    public string Size { get; init; } = "";
}

public class ModHit
{
    public string ProjectId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Downloads { get; init; } = "";
}

/// <summary>Моды профиля: вкл/выкл, добавление, удаление и установка с Modrinth (с зависимостями).</summary>
public static class ModService
{
    const string Off = ".disabled";

    public static List<ModItem> List(Profile p)
    {
        var dir = Paths.InstanceMods(p.Id);
        Directory.CreateDirectory(dir);
        return Directory.EnumerateFiles(dir)
            .Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".jar" + Off, StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                bool enabled = !f.EndsWith(Off, StringComparison.OrdinalIgnoreCase);
                var name = Path.GetFileName(f);
                if (!enabled) name = name[..^Off.Length];
                var kb = new FileInfo(f).Length / 1024.0;
                return new ModItem
                {
                    FilePath = f, Name = Path.GetFileNameWithoutExtension(name), Enabled = enabled,
                    Size = kb > 1024 ? $"{kb / 1024:0.0} МБ" : $"{kb:0} КБ"
                };
            })
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void SetEnabled(ModItem mod, bool enabled)
    {
        if (mod.Enabled == enabled) return;
        var target = enabled ? mod.FilePath[..^Off.Length] : mod.FilePath + Off;
        File.Move(mod.FilePath, target, overwrite: true);
    }

    public static void Delete(ModItem mod) => File.Delete(mod.FilePath);

    public static int Add(Profile p, IEnumerable<string> files)
    {
        var dir = Paths.InstanceMods(p.Id);
        Directory.CreateDirectory(dir);
        int n = 0;
        foreach (var f in files.Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
        {
            File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), overwrite: true);
            n++;
        }
        return n;
    }

    // ---------------- Modrinth ----------------
    const string Api = "https://api.modrinth.com/v2/";

    public static async Task<List<ModHit>> SearchAsync(string query, Profile p)
    {
        var facets = $"[[\"project_type:mod\"],[\"categories:fabric\"]" +
                     (string.IsNullOrEmpty(p.McVersion) ? "" : $",[\"versions:{p.McVersion}\"]") + "]";
        var url = $"{Api}search?limit=20&index={(string.IsNullOrWhiteSpace(query) ? "downloads" : "relevance")}" +
                  $"&query={Uri.EscapeDataString(query ?? "")}&facets={Uri.EscapeDataString(facets)}";
        var root = JsonDocument.Parse(await Net.GetStringAsync(url)).RootElement;
        return root.GetProperty("hits").EnumerateArray().Select(h => new ModHit
        {
            ProjectId = h.GetProperty("project_id").GetString()!,
            Title = h.GetProperty("title").GetString() ?? "",
            Author = h.GetProperty("author").GetString() ?? "",
            Description = h.GetProperty("description").GetString() ?? "",
            IconUrl = h.TryGetProperty("icon_url", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString()! : "",
            Downloads = Short(h.GetProperty("downloads").GetInt64()),
        }).ToList();
    }

    static string Short(long n) => n >= 1_000_000 ? $"{n / 1_000_000.0:0.#}M" : n >= 1000 ? $"{n / 1000.0:0.#}K" : n.ToString();

    /// <summary>Ставит мод и его обязательные зависимости. Возвращает имена установленных файлов.</summary>
    public static async Task<List<string>> InstallAsync(string projectId, Profile p, int depth = 0, HashSet<string>? seen = null)
    {
        seen ??= new();
        var done = new List<string>();
        if (!seen.Add(projectId) || depth > 3) return done;

        var url = $"{Api}project/{projectId}/version?loaders={Uri.EscapeDataString("[\"fabric\"]")}" +
                  (string.IsNullOrEmpty(p.McVersion) ? "" : $"&game_versions={Uri.EscapeDataString($"[\"{p.McVersion}\"]")}");
        var versions = JsonDocument.Parse(await Net.GetStringAsync(url)).RootElement;
        if (versions.GetArrayLength() == 0)
            throw new InvalidOperationException($"Для версии {p.McVersion} (Fabric) подходящего файла нет.");
        var ver = versions[0];
        var files = ver.GetProperty("files");
        var file = files.EnumerateArray().FirstOrDefault(f => f.GetProperty("primary").GetBoolean());
        if (file.ValueKind == JsonValueKind.Undefined) file = files[0];

        var name = file.GetProperty("filename").GetString()!;
        var dir = Paths.InstanceMods(p.Id);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, name);
        if (!File.Exists(target))
        {
            await File.WriteAllBytesAsync(target, await Net.GetBytesAsync(file.GetProperty("url").GetString()!));
            done.Add(name);
        }

        foreach (var dep in ver.GetProperty("dependencies").EnumerateArray())
        {
            if (dep.GetProperty("dependency_type").GetString() != "required") continue;
            if (dep.GetProperty("project_id").ValueKind != JsonValueKind.String) continue;
            done.AddRange(await InstallAsync(dep.GetProperty("project_id").GetString()!, p, depth + 1, seen));
        }
        return done;
    }
}
