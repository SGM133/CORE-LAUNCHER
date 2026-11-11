using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CoreLauncher.Core;

/// <summary>Установка Fabric без отдельных пакетов: профиль версии берём с meta.fabricmc.net.</summary>
public static class FabricService
{
    const string Meta = "https://meta.fabricmc.net/v2/versions/loader/";

    /// <summary>Возвращает id версии Fabric, готовой к запуску (скачивает json при необходимости).</summary>
    public static async Task<string> EnsureAsync(string mcVersion)
    {
        var list = JsonDocument.Parse(await Net.GetStringAsync(Meta + Uri.EscapeDataString(mcVersion))).RootElement;
        if (list.GetArrayLength() == 0)
            throw new InvalidOperationException($"Fabric пока не поддерживает версию {mcVersion}. Выбери другую версию или загрузчик «Vanilla».");

        // первый стабильный загрузчик, иначе самый новый
        var pick = list.EnumerateArray().FirstOrDefault(e => e.GetProperty("loader").GetProperty("stable").GetBoolean());
        if (pick.ValueKind == JsonValueKind.Undefined) pick = list[0];
        var loaderVer = pick.GetProperty("loader").GetProperty("version").GetString()!;

        var id = $"fabric-loader-{loaderVer}-{mcVersion}";
        var dir = Path.Combine(Paths.Shared, "versions", id);
        var file = Path.Combine(dir, id + ".json");
        if (!File.Exists(file))
        {
            Directory.CreateDirectory(dir);
            var json = await Net.GetStringAsync($"{Meta}{Uri.EscapeDataString(mcVersion)}/{loaderVer}/profile/json");
            await File.WriteAllTextAsync(file, json);
        }
        return id;
    }
}
