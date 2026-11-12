using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CoreLauncher.Core;

public record SkinHit(string Name, string SkinUrl, string Site);

/// <summary>
/// Поиск скинов в интернете: три популярных каталога.
/// Каждый отдаёт список (название, прямая ссылка на PNG скина).
/// </summary>
public static class SkinCatalog
{
    public static readonly string[] Sites = { "Ely.by", "MinecraftSkins.net", "MSkins.net" };
    public const int MaxHits = 24;

    public static async Task<List<SkinHit>> SearchAsync(int site, string query)
    {
        query = (query ?? "").Trim();
        var hits = site switch
        {
            0 => await ElyAsync(query),
            1 => await MinecraftSkinsNetAsync(query),
            _ => await MSkinsAsync(query),
        };
        return hits.Where(h => h.SkinUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                   .GroupBy(h => h.SkinUrl).Select(g => g.First()).Take(MaxHits).ToList();
    }

    // ely.by: каталог с поиском по тегам, отвечает JSON
    static async Task<List<SkinHit>> ElyAsync(string q)
    {
        var url = "https://ely.by/api/legacy/skins" + (q.Length > 0 ? "?tags%5B%5D=" + Uri.EscapeDataString(q) : "");
        var root = JsonDocument.Parse(await Net.GetStringAsync(url)).RootElement;
        var list = new List<SkinHit>();
        foreach (var it in root.GetProperty("items").EnumerateArray())
        {
            if (it.TryGetProperty("is_sensitive", out var sens) && sens.ValueKind == JsonValueKind.True) continue;
            var skin = it.GetProperty("skin_url").GetString();
            if (string.IsNullOrEmpty(skin)) continue;
            string name = "";
            if (it.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array && tags.GetArrayLength() > 0)
                name = string.Join(", ", tags.EnumerateArray().Take(2).Select(t => t.GetString()));
            if (name.Length == 0) name = "Скин №" + it.GetProperty("id").GetInt64();
            list.Add(new SkinHit(name, skin, Sites[0]));
        }
        return list;
    }

    // minecraftskins.net: страница поиска, скин скачивается по /<имя>/download
    static async Task<List<SkinHit>> MinecraftSkinsNetAsync(string q)
    {
        var url = q.Length > 0
            ? "https://www.minecraftskins.net/search/" + Uri.EscapeDataString(q)
            : "https://www.minecraftskins.net/category/people";
        var html = await Net.GetStringAsync(url);
        var list = new List<SkinHit>();
        foreach (Match m in Regex.Matches(html, "class=\"panel-link\"\\s+href=\"/([A-Za-z0-9_-]+)[^\"]*\"[\\s\\S]*?class=\"card-title\">([^<]*)<"))
            list.Add(new SkinHit(WebUtility.HtmlDecode(m.Groups[2].Value).Trim(),
                $"https://www.minecraftskins.net/{m.Groups[1].Value}/download", Sites[1]));
        return list;
    }

    // mskins.net: скины игроков по нику; по картинке-превью восстанавливаем путь к файлу скина
    static async Task<List<SkinHit>> MSkinsAsync(string q)
    {
        var url = q.Length > 0 ? "https://mskins.net/en/search?q=" + Uri.EscapeDataString(q) : "https://mskins.net/en";
        var html = await Net.GetStringAsync(url);
        var list = new List<SkinHit>();
        foreach (Match m in Regex.Matches(html, "class=\"item-img\"[^>]*src=\"/uploads/preview3d/([^\"?]+)_350\\.png[^\"]*\"\\s+alt=\"Minecraft skin ([^\"]*)\""))
            list.Add(new SkinHit(WebUtility.HtmlDecode(m.Groups[2].Value).Trim(),
                $"https://mskins.net/uploads/skins/{m.Groups[1].Value}.png", Sites[2]));
        return list;
    }
}
