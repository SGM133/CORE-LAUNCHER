using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CmlLib.Core.Auth;

namespace CoreLauncher.Core;

public record Textures(byte[]? Skin, byte[]? Cape, bool Slim);

/// <summary>
/// Система скинов и плащей. Всё, что отображается в игре, идёт через мод CustomSkinLoader:
///  C.S.S. - наша локальная библиотека (скин+плащ), Официальная - Mojang, Ely.by и TLauncher - по нику.
/// </summary>
public static class SkinService
{
    public static string Title(SkinSystem s) => s switch
    {
        SkinSystem.Css => "C.S.S.",
        SkinSystem.Official => "Официальная",
        SkinSystem.ElyBy => "Ely.by",
        _ => "TLauncher",
    };

    public static string Describe(SkinSystem s) => s switch
    {
        SkinSystem.Css =>
            "Core Skin System - наша система. Скины и плащи лежат в твоей библиотеке, работают офлайн и без регистрации. Видны тебе в игре.",
        SkinSystem.Official =>
            "Скины Mojang. В игре подтягивается скин аккаунта с ником, под которым ты играешь. Со входом через Microsoft можно загрузить скин в свой аккаунт.",
        SkinSystem.ElyBy =>
            "Скины с ely.by по нику. Зарегистрируйся на сайте, загрузи скин под своим ником - и он появится в игре.",
        _ => "Скины TLauncher по нику - как в привычных сборках.",
    };

    // ---------------- библиотека C.S.S. ----------------
    public static List<string> Skins() => Directory.Exists(Paths.CssSkins)
        ? Directory.GetFiles(Paths.CssSkins, "*.png").OrderBy(f => f).ToList() : new();

    public static List<string> Capes() => Directory.Exists(Paths.CssCapes)
        ? Directory.GetFiles(Paths.CssCapes, "*.png").OrderBy(f => f).ToList() : new();

    public static string Import(string src, bool cape)
    {
        var dir = cape ? Paths.CssCapes : Paths.CssSkins;
        Directory.CreateDirectory(dir);
        var px = Px.FromFile(src);
        if (!cape && !((px.W == 64 && px.H == 64) || (px.W == 64 && px.H == 32)))
            throw new InvalidOperationException("Скин должен быть PNG 64x64 (или старый 64x32).");
        if (cape && !(px.W % 64 == 0 && px.H * 2 == px.W) && !(px.W == 64 && px.H == 32))
            throw new InvalidOperationException("Плащ должен быть PNG 64x32.");
        var dst = Unique(dir, Path.GetFileNameWithoutExtension(src));
        File.Copy(src, dst);
        return dst;
    }

    public static string SaveBytes(byte[] png, string name, bool cape)
    {
        var dir = cape ? Paths.CssCapes : Paths.CssSkins;
        Directory.CreateDirectory(dir);
        var dst = Unique(dir, name);
        File.WriteAllBytes(dst, png);
        return dst;
    }

    static string Unique(string dir, string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        var p = Path.Combine(dir, name + ".png");
        for (int i = 2; File.Exists(p); i++) p = Path.Combine(dir, $"{name} ({i}).png");
        return p;
    }

    /// <summary>Встроенный набор, чтобы сразу было что выбрать: скин-путник и фирменные плащи Core.</summary>
    public static void EnsureDefaults()
    {
        Directory.CreateDirectory(Paths.CssSkins);
        Directory.CreateDirectory(Paths.CssCapes);
        if (Skins().Count == 0)
        {
            File.WriteAllBytes(Path.Combine(Paths.CssSkins, "Core Путник.png"), DefaultSkin());
            File.WriteAllBytes(Path.Combine(Paths.CssSkins, "Core Уютный.png"), DefaultSkin(cozy: true));
        }
        if (Capes().Count == 0)
        {
            File.WriteAllBytes(Path.Combine(Paths.CssCapes, "Core Синий.png"), MakeCape(0xFF1C2B77, 0xFF0F1745, 0xFFFFFFFF));
            File.WriteAllBytes(Path.Combine(Paths.CssCapes, "Core Солнце.png"), MakeCape(0xFFFFC857, 0xFFB7822A, 0xFF1C2B77));
            File.WriteAllBytes(Path.Combine(Paths.CssCapes, "Core Закат.png"), MakeCape(0xFFFF8A7A, 0xFF9C4A5A, 0xFFFFF4DE));
            File.WriteAllBytes(Path.Combine(Paths.CssCapes, "Core Мята.png"), MakeCape(0xFF7DE0B5, 0xFF3F8F78, 0xFF1C2B77));
        }
    }

    static readonly string[] Sun =
    {
        "..X..X..X.", "...X.X.X..", ".X.XXXX...", "..XX..XX..", "XXXX..XXXX", "..XX..XX..", ".X.XXXX.X.", "...X.X.X..", "..X..X..X.",
    };

    static byte[] MakeCape(uint main, uint dark, uint emblem)
    {
        var p = new Px(64, 32);
        p.Fill(0, 0, 22, 17, dark);            // каркас всех граней
        p.Fill(1, 1, 10, 16, main);            // наружная сторона
        p.Fill(12, 1, 10, 16, dark);           // внутренняя
        for (int x = 1; x < 11; x++) { p.Set(x, 1, dark); p.Set(x, 16, dark); }
        for (int y = 1; y < 17; y++) { p.Set(1, y, dark); p.Set(10, y, dark); }
        for (int r = 0; r < Sun.Length; r++)
            for (int c = 0; c < 10; c++)
                if (Sun[r][c] == 'X') p.Set(1 + c, 4 + r, emblem);
        return p.ToPng();
    }

    static byte[] DefaultSkin(bool cozy = false)
    {
        const uint skin = 0xFFE8B98F, hair = 0xFF5A3A22, shirtMain = 0xFF1C2B77, shirtCozy = 0xFFFF8A5B,
                   pants = 0xFF2A3466, boot = 0xFF3B2A20, white = 0xFFFFFFFF, eye = 0xFF1C1C3A, sun = 0xFFFFC857;
        uint shirt = cozy ? shirtCozy : shirtMain;
        var p = new Px(64, 64);
        p.Fill(0, 0, 32, 16, skin);                                  // голова
        p.Fill(8, 0, 8, 8, hair);                                    // макушка
        p.Fill(8, 8, 8, 3, hair); p.Fill(0, 8, 8, 3, hair); p.Fill(16, 8, 8, 3, hair); p.Fill(24, 8, 8, 8, hair);
        p.Fill(10, 12, 1, 1, white); p.Fill(11, 12, 1, 1, eye); p.Fill(13, 12, 1, 1, white); p.Fill(14, 12, 1, 1, eye);
        p.Fill(11, 14, 3, 1, 0xFFB36B52);                            // улыбка
        p.Fill(16, 16, 24, 16, shirt);                               // торс
        p.Fill(23, 23, 2, 2, sun); p.Fill(22, 24, 4, 1, sun);        // солнышко на груди
        p.Fill(40, 16, 16, 16, shirt); p.Fill(44, 28, 4, 4, skin); p.Fill(40, 28, 4, 4, skin); p.Fill(48, 28, 4, 4, skin); p.Fill(52, 28, 4, 4, skin);
        p.Fill(32, 48, 16, 16, shirt); p.Fill(36, 60, 4, 4, skin); p.Fill(32, 60, 4, 4, skin); p.Fill(40, 60, 4, 4, skin); p.Fill(44, 60, 4, 4, skin);
        p.Fill(0, 16, 16, 16, pants); p.Fill(0, 29, 16, 3, boot);
        p.Fill(16, 48, 16, 16, pants); p.Fill(16, 61, 16, 3, boot);
        return p.ToPng();
    }

    // ---------------- поиск скина по нику ----------------
    public static async Task<Textures> LookupAsync(SkinSystem sys, string nick)
    {
        if (sys == SkinSystem.Official) return await LookupMojangAsync(nick);
        var root = sys == SkinSystem.ElyBy
            ? "http://skinsystem.ely.by/textures/"
            : "https://auth.tlauncher.org/skin/profile/texture/login/";
        var json = JsonDocument.Parse(await Net.GetStringAsync(root + Uri.EscapeDataString(nick))).RootElement;
        return await FromTexturesJson(json);
    }

    static async Task<Textures> FromTexturesJson(JsonElement json)
    {
        byte[]? skin = null, cape = null; bool slim = false;
        if (json.TryGetProperty("SKIN", out var s) && s.TryGetProperty("url", out var su))
        {
            skin = await Net.GetBytesAsync(su.GetString()!);
            slim = s.TryGetProperty("metadata", out var m) && m.TryGetProperty("model", out var md) && md.GetString() == "slim";
        }
        if (json.TryGetProperty("CAPE", out var c) && c.TryGetProperty("url", out var cu))
            cape = await Net.GetBytesAsync(cu.GetString()!);
        return new Textures(skin, cape, slim);
    }

    static async Task<Textures> LookupMojangAsync(string nick)
    {
        var prof = JsonDocument.Parse(await Net.GetStringAsync(
            "https://api.minecraftservices.com/minecraft/profile/lookup/name/" + Uri.EscapeDataString(nick))).RootElement;
        var id = prof.GetProperty("id").GetString()!;
        var sess = JsonDocument.Parse(await Net.GetStringAsync(
            "https://sessionserver.mojang.com/session/minecraft/profile/" + id)).RootElement;
        var val = sess.GetProperty("properties")[0].GetProperty("value").GetString()!;
        var tex = JsonDocument.Parse(Convert.FromBase64String(val)).RootElement.GetProperty("textures");
        return await FromTexturesJson(tex);
    }

    // ---------------- загрузка в официальный аккаунт ----------------
    public static async Task UploadOfficialAsync(MSession session, string skinPath, bool slim)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(slim ? "slim" : "classic"), "variant" },
            { new ByteArrayContent(File.ReadAllBytes(skinPath)) { Headers = { ContentType = new("image/png") } }, "file", "skin.png" },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.minecraftservices.com/minecraft/profile/skins") { Content = form };
        req.Headers.Authorization = new("Bearer", session.AccessToken);
        using var resp = await Net.Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Mojang ответил {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
    }

    // ---------------- подготовка профиля к запуску ----------------
    const string CslSlug = "customskinloader";

    /// <summary>Ставит CustomSkinLoader в моды профиля (если нет) и пишет его конфиг под выбранную систему.</summary>
    public static async Task PrepareInstanceAsync(Profile prof, LauncherSettings s, string nick)
    {
        var modsDir = Paths.InstanceMods(prof.Id);
        Directory.CreateDirectory(modsDir);
        bool has = Directory.GetFiles(modsDir, "CustomSkinLoader*.jar*").Any();
        if (!has)
        {
            var url = $"https://api.modrinth.com/v2/project/{CslSlug}/version?loaders={Uri.EscapeDataString("[\"fabric\"]")}" +
                      $"&game_versions={Uri.EscapeDataString($"[\"{prof.McVersion}\"]")}";
            var vs = JsonDocument.Parse(await Net.GetStringAsync(url)).RootElement;
            if (vs.GetArrayLength() == 0) throw new InvalidOperationException("Для этой версии нет мода скинов.");
            var f = vs[0].GetProperty("files")[0];
            await File.WriteAllBytesAsync(Path.Combine(modsDir, f.GetProperty("filename").GetString()!),
                await Net.GetBytesAsync(f.GetProperty("url").GetString()!));
        }

        var dataDir = Path.Combine(Paths.Instance(prof.Id), "CustomSkinLoader");
        var localSkins = Path.Combine(dataDir, "LocalSkin", "skins");
        var localCapes = Path.Combine(dataDir, "LocalSkin", "capes");
        Directory.CreateDirectory(localSkins); Directory.CreateDirectory(localCapes);

        // файлы C.S.S. под текущий ник
        var skinDst = Path.Combine(localSkins, nick + ".png");
        var capeDst = Path.Combine(localCapes, nick + ".png");
        foreach (var f in new[] { skinDst, capeDst }) if (File.Exists(f)) File.Delete(f);
        if (s.SkinSystem == SkinSystem.Css)
        {
            if (string.IsNullOrEmpty(s.ActiveSkin) || !File.Exists(s.ActiveSkin)) s.ActiveSkin = Skins().FirstOrDefault();
            if (!string.IsNullOrEmpty(s.ActiveSkin) && File.Exists(s.ActiveSkin)) File.Copy(s.ActiveSkin, skinDst);
            if (!string.IsNullOrEmpty(s.ActiveCape) && File.Exists(s.ActiveCape)) File.Copy(s.ActiveCape, capeDst);
        }

        object[] loadlist = s.SkinSystem switch
        {
            SkinSystem.Css => new object[]
            {
                new { name = "LocalSkin", type = "Legacy", checkPNG = false, model = s.SlimModel ? "slim" : "default",
                      skin = "LocalSkin/skins/{USERNAME}.png", cape = "LocalSkin/capes/{USERNAME}.png",
                      elytra = "LocalSkin/elytras/{USERNAME}.png" },
            },
            SkinSystem.ElyBy => new object[] { new { name = "ElyBy", type = "ElyByAPI", root = "http://skinsystem.ely.by/textures/" } },
            SkinSystem.TLauncher => new object[] { new { name = "TLauncher", type = "ElyByAPI", root = "https://auth.tlauncher.org/skin/profile/texture/login/" } },
            _ => new object[] { new { name = "Mojang", type = "MojangAPI", apiRoot = "https://api.mojang.com/", sessionRoot = "https://sessionserver.mojang.com/" } },
        };
        var cfg = new { enableTransparentSkin = true, forceLoadAllTextures = true, enableCape = true, threadPoolSize = 8, loadlist };
        await File.WriteAllTextAsync(Path.Combine(dataDir, "CustomSkinLoader.json"),
            JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }
}
