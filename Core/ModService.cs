using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace CoreLauncher.Core;

public class ModItem
{
    public string FilePath { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Enabled { get; init; }
    public string Size { get; init; } = "";
    public BitmapSource? Icon { get; init; }
}

public class ModHit
{
    public string ProjectId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Downloads { get; init; } = "";
    /// <summary>Страница мода на выбранном сайте.</summary>
    public string PageUrl { get; init; } = "";
    public ModSite Site { get; init; }
}

public enum ModSite { Modrinth, CurseForge }

/// <summary>Моды профиля: вкл/выкл, добавление, удаление, значки и установка с Modrinth и CurseForge (с зависимостями).</summary>
public static class ModService
{
    const string Off = ".disabled";

    public static List<ModItem> List(Profile p)
    {
        var dir = Paths.InstanceMods(p.Id);
        Directory.CreateDirectory(dir);
        return Directory.EnumerateFiles(dir)
            .Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".jar" + Off, StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".litemod", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                bool enabled = !f.EndsWith(Off, StringComparison.OrdinalIgnoreCase);
                var name = Path.GetFileName(f);
                if (!enabled) name = name[..^Off.Length];
                var kb = new FileInfo(f).Length / 1024.0;
                return new ModItem
                {
                    FilePath = f, Name = Path.GetFileNameWithoutExtension(name), Enabled = enabled,
                    Size = kb > 1024 ? $"{kb / 1024:0.0} МБ" : $"{kb:0} КБ",
                    Icon = IconFor(dir, f, name),
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
        foreach (var f in files.Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".litemod", StringComparison.OrdinalIgnoreCase)))
        {
            File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), overwrite: true);
            n++;
        }
        return n;
    }

    // ---------------- значки ----------------
    static string IconDir(string modsDir) => Path.Combine(modsDir, ".icons");
    static string IconFile(string modsDir, string jarName) => Path.Combine(IconDir(modsDir), jarName + ".img");

    /// <summary>Значок мода: сохранённый при установке из каталога либо найденный внутри самого файла.</summary>
    static BitmapSource? IconFor(string modsDir, string path, string jarName)
    {
        try
        {
            var cache = IconFile(modsDir, jarName);
            if (!File.Exists(cache))
            {
                var bytes = IconFromJar(path);
                Directory.CreateDirectory(IconDir(modsDir));
                File.WriteAllBytes(cache, bytes ?? Array.Empty<byte>());   // пустой файл = "значка нет", второй раз jar не открываем
            }
            var data = File.ReadAllBytes(cache);
            return data.Length == 0 ? null : Decode(data);
        }
        catch { return null; }
    }

    static BitmapSource? Decode(byte[] data)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(data);
            bmp.DecodePixelWidth = 64;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit(); bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    static byte[]? IconFromJar(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            string? Text(string entry)
            {
                var e = zip.GetEntry(entry);
                if (e == null) return null;
                using var r = new StreamReader(e.Open());
                return r.ReadToEnd();
            }
            var names = new List<string>();
            // Fabric / Quilt: "icon": "assets/x/icon.png" (или набор размеров)
            foreach (var meta in new[] { "fabric.mod.json", "quilt.mod.json" })
            {
                var t = Text(meta);
                if (t == null) continue;
                foreach (Match m in Regex.Matches(t, "\"icon\"\\s*:\\s*(?:\"([^\"]+)\"|\\{[^}]*?\"\\d+\"\\s*:\\s*\"([^\"]+)\")"))
                    names.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            }
            // Forge / NeoForge: logoFile="x.png"; старый Forge: "logoFile": "x.png"
            foreach (var meta in new[] { "META-INF/mods.toml", "META-INF/neoforge.mods.toml", "mcmod.info", "litemod.json" })
            {
                var t = Text(meta);
                if (t == null) continue;
                foreach (Match m in Regex.Matches(t, "\"?logoFile\"?\\s*[=:]\\s*\"([^\"]+)\"")) names.Add(m.Groups[1].Value);
            }
            names.AddRange(new[] { "icon.png", "logo.png", "pack.png" });
            foreach (var n in names)
            {
                var e = zip.GetEntry(n.TrimStart('/', '.').Replace('\\', '/'));
                if (e == null || e.Length == 0 || e.Length > 2_000_000) continue;
                using var ms = new MemoryStream();
                using (var s = e.Open()) s.CopyTo(ms);
                return ms.ToArray();
            }
        }
        catch { }
        return null;
    }

    static async Task SaveIconAsync(string modsDir, string jarName, string? iconUrl)
    {
        if (string.IsNullOrEmpty(iconUrl) || !iconUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var bytes = await Net.GetBytesAsync(iconUrl);
            if (Decode(bytes) == null) return;   // формат не открывается - оставим значок из самого мода
            Directory.CreateDirectory(IconDir(modsDir));
            await File.WriteAllBytesAsync(IconFile(modsDir, jarName), bytes);
        }
        catch { }
    }

    static string Short(long n) => n >= 1_000_000 ? $"{n / 1_000_000.0:0.#}M" : n >= 1000 ? $"{n / 1000.0:0.#}K" : n.ToString();

    public static Task<List<ModHit>> SearchAsync(ModSite site, string query, Profile p) =>
        site == ModSite.CurseForge ? CurseSearchAsync(query, p) : ModrinthSearchAsync(query, p);

    public static Task<List<string>> InstallAsync(ModHit hit, Profile p) =>
        hit.Site == ModSite.CurseForge
            ? CurseInstallAsync(long.Parse(hit.ProjectId), hit.IconUrl, p, 0, new())
            : ModrinthInstallAsync(hit.ProjectId, hit.IconUrl, p, 0, new());

    // ---------------- Modrinth ----------------
    const string Api = "https://api.modrinth.com/v2/";

    static string LoaderJson(Profile p) => "[" + string.Join(",", Loaders.ModrinthNames(p.Loader).Select(n => $"\"{n}\"")) + "]";

    static async Task<List<ModHit>> ModrinthSearchAsync(string query, Profile p)
    {
        var cats = Loaders.ModrinthNames(p.Loader).Select(n => $"\"categories:{n}\"").ToList();
        var facets = "[[\"project_type:mod\"]" + (cats.Count > 0 ? $",[{string.Join(",", cats)}]" : "") +
                     (string.IsNullOrEmpty(p.McVersion) ? "" : $",[\"versions:{p.McVersion}\"]") + "]";
        var url = $"{Api}search?limit=20&index={(string.IsNullOrWhiteSpace(query) ? "downloads" : "relevance")}" +
                  $"&query={Uri.EscapeDataString(query ?? "")}&facets={Uri.EscapeDataString(facets)}";
        var root = JsonDocument.Parse(await Net.GetStringAsync(url)).RootElement;
        return root.GetProperty("hits").EnumerateArray().Select(h => new ModHit
        {
            Site = ModSite.Modrinth,
            ProjectId = h.GetProperty("project_id").GetString()!,
            Title = h.GetProperty("title").GetString() ?? "",
            Author = h.GetProperty("author").GetString() ?? "",
            Description = h.GetProperty("description").GetString() ?? "",
            IconUrl = h.TryGetProperty("icon_url", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString()! : "",
            Downloads = Short(h.GetProperty("downloads").GetInt64()),
            PageUrl = "https://modrinth.com/mod/" + (h.TryGetProperty("slug", out var sl) ? sl.GetString() : h.GetProperty("project_id").GetString()),
        }).ToList();
    }

    /// <summary>Ставит мод и его обязательные зависимости. Возвращает имена установленных файлов.</summary>
    static async Task<List<string>> ModrinthInstallAsync(string projectId, string? iconUrl, Profile p, int depth, HashSet<string> seen)
    {
        var done = new List<string>();
        if (!seen.Add(projectId) || depth > 3) return done;

        var url = $"{Api}project/{projectId}/version?loaders={Uri.EscapeDataString(LoaderJson(p))}" +
                  (string.IsNullOrEmpty(p.McVersion) ? "" : $"&game_versions={Uri.EscapeDataString($"[\"{p.McVersion}\"]")}");
        var versions = JsonDocument.Parse(await Net.GetStringAsync(url)).RootElement;
        if (versions.GetArrayLength() == 0)
            throw new InvalidOperationException($"Для {Loaders.Title(p.Loader)} {p.McVersion} подходящего файла нет.");
        var ver = versions[0];
        var files = ver.GetProperty("files");
        var file = files.EnumerateArray().FirstOrDefault(f => f.GetProperty("primary").GetBoolean());
        if (file.ValueKind == JsonValueKind.Undefined) file = files[0];

        var name = Path.GetFileName(file.GetProperty("filename").GetString()!);
        var dir = Paths.InstanceMods(p.Id);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, name);
        if (!File.Exists(target))
        {
            await File.WriteAllBytesAsync(target, await Net.GetBytesAsync(file.GetProperty("url").GetString()!));
            done.Add(name);
        }
        if (depth > 0 && iconUrl == null)
        {
            try
            {
                var proj = JsonDocument.Parse(await Net.GetStringAsync($"{Api}project/{projectId}")).RootElement;
                if (proj.TryGetProperty("icon_url", out var iu) && iu.ValueKind == JsonValueKind.String) iconUrl = iu.GetString();
            }
            catch { }
        }
        await SaveIconAsync(dir, name, iconUrl);

        foreach (var dep in ver.GetProperty("dependencies").EnumerateArray())
        {
            if (dep.GetProperty("dependency_type").GetString() != "required") continue;
            if (dep.GetProperty("project_id").ValueKind != JsonValueKind.String) continue;
            done.AddRange(await ModrinthInstallAsync(dep.GetProperty("project_id").GetString()!, null, p, depth + 1, seen));
        }
        return done;
    }

    // ---------------- CurseForge (через открытое зеркало API, ключ не нужен) ----------------
    const string Cf = "https://api.curse.tools/v1/";

    static string CfFilter(Profile p)
    {
        var s = string.IsNullOrEmpty(p.McVersion) ? "" : "&gameVersion=" + Uri.EscapeDataString(p.McVersion);
        var t = Loaders.CurseType(p.Loader);
        return t > 0 ? s + "&modLoaderType=" + t : s;
    }

    static async Task<List<ModHit>> CurseSearchAsync(string query, Profile p)
    {
        var url = $"{Cf}mods/search?gameId=432&classId=6&pageSize=20&sortField=2&sortOrder=desc" +
                  (string.IsNullOrWhiteSpace(query) ? "" : "&searchFilter=" + Uri.EscapeDataString(query.Trim())) + CfFilter(p);   // пустой фильтр сервер не принимает
        var root = JsonDocument.Parse(await Net.GetStringAsync(url)).RootElement;
        return root.GetProperty("data").EnumerateArray().Select(h => new ModHit
        {
            Site = ModSite.CurseForge,
            ProjectId = h.GetProperty("id").GetInt64().ToString(),
            Title = h.GetProperty("name").GetString() ?? "",
            Author = h.TryGetProperty("authors", out var a) && a.GetArrayLength() > 0 ? a[0].GetProperty("name").GetString() ?? "" : "",
            Description = h.TryGetProperty("summary", out var sm) ? sm.GetString() ?? "" : "",
            IconUrl = h.TryGetProperty("logo", out var lg) && lg.ValueKind == JsonValueKind.Object && lg.TryGetProperty("thumbnailUrl", out var tu)
                ? tu.GetString() ?? "" : "",
            Downloads = Short((long)h.GetProperty("downloadCount").GetDouble()),
            PageUrl = h.TryGetProperty("links", out var ln) && ln.TryGetProperty("websiteUrl", out var wu) ? wu.GetString() ?? "" : "",
        }).ToList();
    }

    static async Task<List<string>> CurseInstallAsync(long modId, string? iconUrl, Profile p, int depth, HashSet<long> seen)
    {
        var done = new List<string>();
        if (!seen.Add(modId) || depth > 3) return done;

        var root = JsonDocument.Parse(await Net.GetStringAsync($"{Cf}mods/{modId}/files?pageSize=1{CfFilter(p)}")).RootElement;
        var data = root.GetProperty("data");
        if (data.GetArrayLength() == 0)
            throw new InvalidOperationException($"Для {Loaders.Title(p.Loader)} {p.McVersion} подходящего файла нет.");
        var file = data[0];
        var name = Path.GetFileName(file.GetProperty("fileName").GetString()!);
        var fileId = file.GetProperty("id").GetInt64();
        // автор может запретить прямую ссылку - тогда собираем адрес файла сами
        var dl = file.TryGetProperty("downloadUrl", out var du) && du.ValueKind == JsonValueKind.String ? du.GetString()! : "";
        if (dl.Length == 0) dl = $"https://edge.forgecdn.net/files/{fileId / 1000}/{fileId % 1000}/{Uri.EscapeDataString(name)}";

        var dir = Paths.InstanceMods(p.Id);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, name);
        if (!File.Exists(target))
        {
            await File.WriteAllBytesAsync(target, await Net.GetBytesAsync(dl));
            done.Add(name);
        }
        if (depth > 0 && iconUrl == null)
        {
            try
            {
                var mod = JsonDocument.Parse(await Net.GetStringAsync($"{Cf}mods/{modId}")).RootElement.GetProperty("data");
                if (mod.TryGetProperty("logo", out var lg) && lg.ValueKind == JsonValueKind.Object) iconUrl = lg.GetProperty("thumbnailUrl").GetString();
            }
            catch { }
        }
        await SaveIconAsync(dir, name, iconUrl);

        if (file.TryGetProperty("dependencies", out var deps))
            foreach (var dep in deps.EnumerateArray())
                if (dep.GetProperty("relationType").GetInt32() == 3)   // 3 = обязательная зависимость
                    done.AddRange(await CurseInstallAsync(dep.GetProperty("modId").GetInt64(), null, p, depth + 1, seen));
        return done;
    }
}
