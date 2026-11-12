using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CmlLib.Core;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installer.NeoForge.Installers;

namespace CoreLauncher.Core;

public record LoaderInfo(Loader Id, string Title, string Hint);

/// <summary>Загрузчики модов: что это, для каких версий и как поставить.</summary>
public static class Loaders
{
    public static readonly LoaderInfo[] All =
    {
        new(Loader.Vanilla, "Vanilla", "чистая игра без модов"),
        new(Loader.Fabric, "Fabric", "лёгкий и современный, 1.14+"),
        new(Loader.Quilt, "Quilt", "понимает моды Fabric, 1.14+"),
        new(Loader.Forge, "Forge", "классика, больше всего модов"),
        new(Loader.NeoForge, "NeoForge", "преемник Forge, 1.20.1+"),
        new(Loader.LiteLoader, "LiteLoader", "лёгкие моды, 1.5.2 - 1.12.2"),
        new(Loader.Rift, "Rift", "только Minecraft 1.13.2"),
        new(Loader.ModLoader, "Risugami's ModLoader", "старина: альфа - 1.6.2"),
    };

    public static string Title(Loader l) => All.First(i => i.Id == l).Title;

    /// <summary>Имена загрузчика в каталоге Modrinth (Quilt умеет и моды Fabric).</summary>
    public static string[] ModrinthNames(Loader l) => l switch
    {
        Loader.Fabric => new[] { "fabric" },
        Loader.Quilt => new[] { "quilt", "fabric" },
        Loader.Forge => new[] { "forge" },
        Loader.NeoForge => new[] { "neoforge" },
        Loader.LiteLoader => new[] { "liteloader" },
        Loader.Rift => new[] { "rift" },
        Loader.ModLoader => new[] { "modloader" },
        _ => Array.Empty<string>(),
    };

    /// <summary>Номер типа загрузчика в CurseForge (0 = любой).</summary>
    public static int CurseType(Loader l) => l switch
    {
        Loader.Forge => 1, Loader.LiteLoader => 3, Loader.Fabric => 4, Loader.Quilt => 5, Loader.NeoForge => 6, _ => 0,
    };

    /// <summary>Мод скинов (CustomSkinLoader) есть только под эти загрузчики.</summary>
    public static bool SupportsSkins(Loader l) => l is Loader.Fabric or Loader.Quilt or Loader.Forge or Loader.NeoForge;

    static (int minor, int patch)? Parse(string mc)
    {
        var m = Regex.Match(mc ?? "", @"^1\.(\d+)(?:\.(\d+))?$");
        return m.Success ? (int.Parse(m.Groups[1].Value), m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0) : null;
    }

    /// <summary>Предупреждение, если загрузчик и версия игры не дружат (null = всё в порядке).</summary>
    public static string? Check(Loader l, string mc)
    {
        if (string.IsNullOrEmpty(mc) || l == Loader.Vanilla) return null;
        var v = Parse(mc);
        bool old = mc.StartsWith("a") || mc.StartsWith("b") || mc.StartsWith("c") || mc.StartsWith("rd") || mc.StartsWith("inf");
        switch (l)
        {
            case Loader.Fabric:
                if (mc.StartsWith("26")) return "Fabric на сборках 26.x пока не запускается. Выбери 1.21.x.";
                if (old || v is { minor: < 14 }) return "Fabric работает с Minecraft 1.14 и новее.";
                break;
            case Loader.Quilt:
                if (old || v is { minor: < 14 }) return "Quilt работает с Minecraft 1.14 и новее.";
                break;
            case Loader.Forge:
                if (old) return "Forge нет для альфа- и бета-версий.";
                break;
            case Loader.NeoForge:
                if (old || v is { minor: < 20 } || v is { minor: 20, patch: < 1 }) return "NeoForge работает с Minecraft 1.20.1 и новее.";
                break;
            case Loader.LiteLoader:
                if (old || v is null || v.Value.minor > 12 || v.Value.minor < 5) return "LiteLoader есть только для Minecraft 1.5.2 - 1.12.2.";
                break;
            case Loader.Rift:
                if (mc != "1.13.2") return "Rift работает только на Minecraft 1.13.2.";
                break;
            case Loader.ModLoader:
                if (!old && (v is null || v.Value.minor > 6)) return "Risugami's ModLoader есть только для старых версий: от альфы до 1.6.2.";
                break;
        }
        return null;
    }

    static string VersionsDir => Path.Combine(Paths.Shared, "versions");
    static string LibrariesDir => Path.Combine(Paths.Shared, "libraries");

    /// <summary>Готовит загрузчик и возвращает id версии для запуска.</summary>
    public static async Task<string> EnsureAsync(Profile p, MinecraftLauncher launcher, IProgress<LaunchProgress> progress)
    {
        var mc = p.McVersion;
        var bad = Check(p.Loader, mc);
        if (bad != null && p.Loader != Loader.Fabric) throw new InvalidOperationException(bad);
        progress.Report(new($"Готовим {Title(p.Loader)}…", 0));
        Log.Info($"Загрузчик {Title(p.Loader)} для Minecraft {mc}");
        return p.Loader switch
        {
            Loader.Fabric => await FabricService.EnsureAsync(mc),
            Loader.Quilt => await QuiltAsync(mc),
            Loader.Forge => await ForgeAsync(mc, launcher, progress),
            Loader.NeoForge => await NeoForgeAsync(mc, launcher, progress),
            Loader.LiteLoader => await LiteLoaderAsync(mc),
            Loader.Rift => await RiftAsync(mc),
            Loader.ModLoader => await ModLoaderAsync(mc, progress),
            _ => mc,
        };
    }

    // ---------- Quilt: как Fabric, готовый профиль с meta.quiltmc.org ----------
    static async Task<string> QuiltAsync(string mc)
    {
        const string meta = "https://meta.quiltmc.org/v3/versions/loader/";
        var list = JsonDocument.Parse(await Net.GetStringAsync(meta + Uri.EscapeDataString(mc))).RootElement;
        if (list.GetArrayLength() == 0) throw new InvalidOperationException($"Quilt не поддерживает версию {mc}.");
        var vers = list.EnumerateArray().Select(e => e.GetProperty("loader").GetProperty("version").GetString()!).ToList();
        var ver = vers.FirstOrDefault(v => !v.Contains('-')) ?? vers[0];   // без beta/pre, если есть
        var id = $"quilt-loader-{ver}-{mc}";
        var dir = Path.Combine(VersionsDir, id);
        var file = Path.Combine(dir, id + ".json");
        if (!File.Exists(file))
        {
            var node = JsonNode.Parse(await Net.GetStringAsync($"{meta}{Uri.EscapeDataString(mc)}/{ver}/profile/json"))!;
            node["id"] = id;
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(file, node.ToJsonString());
        }
        return id;
    }

    // ---------- Forge / NeoForge: официальные установщики (нужна Java, лаунчер найдёт её сам) ----------
    static async Task<string> ForgeAsync(string mc, MinecraftLauncher launcher, IProgress<LaunchProgress> progress)
    {
        var forge = new ForgeInstaller(launcher);
        return await forge.Install(mc, new ForgeInstallOptions
        {
            FileProgress = new Progress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>(e =>
                progress.Report(new($"Forge: {e.Name}", e.TotalTasks == 0 ? 0 : 100.0 * e.ProgressedTasks / e.TotalTasks))),
            InstallerOutput = new Progress<string>(line => Log.Info("Forge: " + line)),
        });
    }

    static async Task<string> NeoForgeAsync(string mc, MinecraftLauncher launcher, IProgress<LaunchProgress> progress)
    {
        var neo = new NeoForgeInstaller(launcher);
        return await neo.Install(mc, new NeoForgeInstallOptions
        {
            FileProgress = new Progress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>(e =>
                progress.Report(new($"NeoForge: {e.Name}", e.TotalTasks == 0 ? 0 : 100.0 * e.ProgressedTasks / e.TotalTasks))),
            InstallerOutput = new Progress<string>(line => Log.Info("NeoForge: " + line)),
        });
    }

    // ---------- ванильный json версии (нужен как основа для старых загрузчиков) ----------
    static async Task<JsonNode> VanillaJsonAsync(string mc)
    {
        var local = Path.Combine(VersionsDir, mc, mc + ".json");
        if (File.Exists(local)) return JsonNode.Parse(await File.ReadAllTextAsync(local))!;
        var manifest = JsonDocument.Parse(await Net.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json")).RootElement;
        var entry = manifest.GetProperty("versions").EnumerateArray().FirstOrDefault(v => v.GetProperty("id").GetString() == mc);
        if (entry.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException($"Версия {mc} не найдена у Mojang.");
        var text = await Net.GetStringAsync(entry.GetProperty("url").GetString()!);
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        await File.WriteAllTextAsync(local, text);
        return JsonNode.Parse(text)!;
    }

    static async Task WriteVersionAsync(string id, JsonNode json)
    {
        var dir = Path.Combine(VersionsDir, id);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, id + ".json"), json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    static bool HasVersion(string id) => File.Exists(Path.Combine(VersionsDir, id, id + ".json"));

    // ---------- LiteLoader: описание берём из каталога Prism Launcher ----------
    static async Task<string> LiteLoaderAsync(string mc)
    {
        var id = $"{mc}-LiteLoader";
        if (HasVersion(id)) return id;
        const string meta = "https://meta.prismlauncher.org/v1/com.mumfrey.liteloader/";
        var index = JsonDocument.Parse(await Net.GetStringAsync(meta + "index.json")).RootElement;
        var fits = index.GetProperty("versions").EnumerateArray().Where(v =>
            v.GetProperty("requires").EnumerateArray().Any(r => r.TryGetProperty("equals", out var eq) && eq.GetString() == mc)).ToList();
        if (fits.Count == 0) throw new InvalidOperationException($"LiteLoader нет для версии {mc}.");
        var pick = fits.FirstOrDefault(v => v.TryGetProperty("recommended", out var r) && r.GetBoolean());
        if (pick.ValueKind == JsonValueKind.Undefined) pick = fits[0];
        var lite = JsonDocument.Parse(await Net.GetStringAsync(meta + pick.GetProperty("version").GetString() + ".json")).RootElement;

        var vanilla = await VanillaJsonAsync(mc);
        var args = vanilla["minecraftArguments"]?.GetValue<string>()
                   ?? throw new InvalidOperationException("LiteLoader не подходит к этой версии игры.");
        foreach (var t in lite.GetProperty("+tweakers").EnumerateArray()) args += " --tweakClass " + t.GetString();

        var libs = new JsonArray();
        foreach (var l in lite.GetProperty("libraries").EnumerateArray())
        {
            var o = new JsonObject { ["name"] = l.GetProperty("name").GetString() };
            if (l.TryGetProperty("url", out var u))
            {
                var url = u.GetString()!;
                if (url.Contains("repo.liteloader.com")) url = "https://repo1.maven.org/maven2/";   // старое зеркало закрыто
                o["url"] = url;
            }
            libs.Add(o);
        }
        await WriteVersionAsync(id, new JsonObject
        {
            ["id"] = id, ["inheritsFrom"] = mc, ["type"] = "release",
            ["mainClass"] = lite.GetProperty("mainClass").GetString(),
            ["minecraftArguments"] = args, ["libraries"] = libs,
        });
        return id;
    }

    // ---------- Rift: родной сервер закрыт, берём сборку с GitHub и Mixin у Sponge ----------
    static async Task<string> RiftAsync(string mc)
    {
        const string riftVer = "1.0.4-106", mixinBuild = "0.7.11-20180703.121122-1";
        var id = $"{mc}-rift-{riftVer}";
        var rift = Path.Combine(LibrariesDir, "org", "dimdev", "rift", riftVer, $"rift-{riftVer}.jar");
        var mixin = Path.Combine(LibrariesDir, "org", "spongepowered", "mixin", "0.7.11-SNAPSHOT", "mixin-0.7.11-SNAPSHOT.jar");
        await FetchAsync(rift, $"https://github.com/DimensionalDevelopment/Rift/releases/download/v{riftVer}/Rift-{riftVer}.jar");
        await FetchAsync(mixin, $"https://repo.spongepowered.org/repository/maven-public/org/spongepowered/mixin/0.7.11-SNAPSHOT/mixin-{mixinBuild}.jar");
        if (HasVersion(id)) return id;

        const string central = "https://repo1.maven.org/maven2/";
        await WriteVersionAsync(id, new JsonObject
        {
            ["id"] = id, ["inheritsFrom"] = mc, ["type"] = "release",
            ["mainClass"] = "net.minecraft.launchwrapper.Launch",
            ["arguments"] = new JsonObject { ["game"] = new JsonArray("--tweakClass", "org.dimdev.riftloader.launch.RiftLoaderClientTweaker") },
            ["libraries"] = new JsonArray(
                new JsonObject { ["name"] = $"org.dimdev:rift:{riftVer}" },
                new JsonObject { ["name"] = "org.spongepowered:mixin:0.7.11-SNAPSHOT" },
                new JsonObject { ["name"] = "org.ow2.asm:asm:6.2", ["url"] = central },
                new JsonObject { ["name"] = "org.ow2.asm:asm-commons:6.2", ["url"] = central },
                new JsonObject { ["name"] = "org.ow2.asm:asm-tree:6.2", ["url"] = central },
                new JsonObject { ["name"] = "net.minecraft:launchwrapper:1.12" }),
        });
        return id;
    }

    static async Task FetchAsync(string file, string url)
    {
        if (File.Exists(file) && new FileInfo(file).Length > 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, await Net.GetBytesAsync(url));
    }

    // ---------- Risugami's ModLoader: классы мода вшиваются прямо в jar игры ----------
    static async Task<string> ModLoaderAsync(string mc, IProgress<LaunchProgress> progress)
    {
        var id = $"{mc}-ModLoader";
        var dir = Path.Combine(VersionsDir, id);
        var jar = Path.Combine(dir, id + ".jar");
        if (HasVersion(id) && File.Exists(jar)) return id;

        // архив ModLoader хранится в mcarchive.net
        var api = JsonDocument.Parse(await Net.GetStringAsync("https://mcarchive.net/api/v1/mods/by_slug/modloader")).RootElement;
        string? zipUrl = null;
        foreach (var v in api.GetProperty("mod_versions").EnumerateArray())
        {
            if (!v.GetProperty("game_versions").EnumerateArray().Any(g => g.GetProperty("name").GetString() == mc)) continue;
            foreach (var f in v.GetProperty("files").EnumerateArray())
            {
                var name = f.GetProperty("name").GetString() ?? "";
                var url = f.TryGetProperty("archive_url", out var a) ? a.GetString() : null;
                if (!string.IsNullOrEmpty(url) && (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
                { zipUrl = url; break; }
            }
            if (zipUrl != null) break;
        }
        if (zipUrl == null) throw new InvalidOperationException($"Risugami's ModLoader для версии {mc} в архиве не найден.");

        var vanilla = await VanillaJsonAsync(mc);
        var clientUrl = vanilla["downloads"]?["client"]?["url"]?.GetValue<string>()
                        ?? throw new InvalidOperationException("У этой версии нет файла игры для скачивания.");
        progress.Report(new("ModLoader: скачиваем игру и мод…", 0));
        var gameJar = Path.Combine(VersionsDir, mc, mc + ".jar");
        var game = File.Exists(gameJar) ? await File.ReadAllBytesAsync(gameJar) : await Net.GetBytesAsync(clientUrl);
        var mod = await Net.GetBytesAsync(zipUrl);

        progress.Report(new("ModLoader: вшиваем мод в игру…", 50));
        Directory.CreateDirectory(dir);
        var tmp = jar + ".tmp";
        await Task.Run(() =>
        {
            using var modZip = new ZipArchive(new MemoryStream(mod), ZipArchiveMode.Read);
            var modNames = modZip.Entries.Where(e => e.Length > 0 || !e.FullName.EndsWith("/")).Select(e => e.FullName).ToHashSet();
            using var gameZip = new ZipArchive(new MemoryStream(game), ZipArchiveMode.Read);
            using var fs = File.Create(tmp);
            using var dst = new ZipArchive(fs, ZipArchiveMode.Create);
            void Copy(ZipArchiveEntry e)
            {
                if (e.FullName.EndsWith("/")) return;
                using var src = e.Open();
                using var o = dst.CreateEntry(e.FullName, CompressionLevel.Fastest).Open();
                src.CopyTo(o);
            }
            // META-INF убираем: подпись Mojang после правки jar уже не сходится
            foreach (var e in gameZip.Entries)
                if (!e.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) && !modNames.Contains(e.FullName)) Copy(e);
            foreach (var e in modZip.Entries)
                if (!e.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)) Copy(e);
        });
        File.Move(tmp, jar, true);

        // json - копия ванильного, но без ссылки на оригинальный jar (иначе его перекачает поверх нашего)
        vanilla["id"] = id;
        (vanilla as JsonObject)!.Remove("downloads");
        await WriteVersionAsync(id, vanilla);
        return id;
    }
}
