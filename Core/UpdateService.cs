using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CoreLauncher.Core;

public record UpdateInfo(Version Version, string Url, string? Sha256, string Notes);

/// <summary>
/// Самообновление одного exe. Источник - манифест JSON
/// {"version":"0.3.0","url":"https://.../CoreLauncher.exe","sha256":"...","notes":"..."}
/// или GitHub Releases ("github:владелец/репозиторий", файл CoreLauncher.exe в релизе).
/// </summary>
public static class UpdateService
{
    public static Version Current => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Math.Max(0, Current.Build)}";

    static string UpdateDir => Path.Combine(Paths.Root, "update");

    /// <summary>Возвращает информацию о более новой версии или null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(string source)
    {
        source = (source ?? "").Trim();
        if (source.Length == 0) return null;

        string ver, url, notes; string? sha;
        if (source.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
        {
            var repo = source[7..].Trim('/');
            string relJson;
            try { relJson = await Net.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest"); }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; } // релизов ещё нет
            var rel = JsonDocument.Parse(relJson).RootElement;
            ver = rel.GetProperty("tag_name").GetString()!.TrimStart('v', 'V');
            notes = rel.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()! : "";
            url = ""; sha = null;
            string? shaUrl = null;
            foreach (var a in rel.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString();
                if (string.Equals(name, "CoreLauncher.exe", StringComparison.OrdinalIgnoreCase))
                {
                    url = a.GetProperty("browser_download_url").GetString()!;
                    if (a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String)
                        sha = d.GetString()!.Replace("sha256:", "", StringComparison.OrdinalIgnoreCase);
                }
                else if (string.Equals(name, "CoreLauncher.exe.sha256", StringComparison.OrdinalIgnoreCase))
                    shaUrl = a.GetProperty("browser_download_url").GetString();
            }
            if (url.Length == 0) throw new InvalidOperationException("В релизе нет файла CoreLauncher.exe.");
            // хеш из файла .sha256 в релизе (формат: "<хеш>" или "<хеш>  CoreLauncher.exe")
            if (shaUrl != null)
                sha = (await Net.GetStringAsync(shaUrl)).Trim().Split(' ', '\t', '\r', '\n')[0];
        }
        else
        {
            RequireSafe(source);
            var m = JsonDocument.Parse(await Net.GetStringAsync(source)).RootElement;
            ver = m.GetProperty("version").GetString()!.TrimStart('v', 'V');
            url = m.GetProperty("url").GetString()!;
            sha = m.TryGetProperty("sha256", out var s) ? s.GetString() : null;
            notes = m.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute)) url = new Uri(new Uri(source), url).ToString();
        }

        if (!Version.TryParse(ver, out var v)) throw new InvalidOperationException($"Непонятный номер версии: {ver}");
        return v > Current ? new UpdateInfo(v, url, sha, notes) : null;
    }

    static void RequireSafe(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) throw new InvalidOperationException("Некорректный адрес обновлений.");
        bool local = u.IsLoopback;
        if (u.Scheme != Uri.UriSchemeHttps && !(local && u.Scheme == Uri.UriSchemeHttp))
            throw new InvalidOperationException("Адрес обновлений должен начинаться с https://");
    }

    /// <summary>Скачивает новый exe и проверяет контрольную сумму. Без суммы обновление не ставим.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double> progress)
    {
        RequireSafe(info.Url);
        if (string.IsNullOrWhiteSpace(info.Sha256))
            throw new InvalidOperationException("У обновления нет контрольной суммы SHA-256 - ставить такое небезопасно.");

        Directory.CreateDirectory(UpdateDir);
        var file = Path.Combine(UpdateDir, "CoreLauncher.new.exe");
        using var resp = await Net.Http.SendAsync(new HttpRequestMessage(HttpMethod.Get, info.Url), HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 0;
        using (var src = await resp.Content.ReadAsStreamAsync())
        using (var dst = File.Create(file))
        {
            var buf = new byte[81920]; long done = 0; int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                if (total > 0) progress.Report(100.0 * done / total);
            }
        }

        string actual;
        using (var fs = File.OpenRead(file)) actual = Convert.ToHexString(await SHA256.HashDataAsync(fs));
        if (!string.Equals(actual, info.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(file);
            throw new InvalidOperationException("Контрольная сумма не совпала - файл повреждён или подменён. Обновление отменено.");
        }
        return file;
    }

    /// <summary>Запускает скрипт, который после выхода лаунчера подменяет exe и запускает его снова.</summary>
    public static void ApplyAndRestart(string newExe)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Не найден путь к exe.");
        var pid = Environment.ProcessId;
        var script = Path.Combine(UpdateDir, "apply-update.cmd");
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine(":wait");
        sb.AppendLine($"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul");
        sb.AppendLine("if not errorlevel 1 (ping -n 2 127.0.0.1 >nul & goto wait)");
        sb.AppendLine("set /a tries=0");
        sb.AppendLine(":retry");
        sb.AppendLine($"move /Y \"{newExe}\" \"{exe}\" >nul 2>&1");
        sb.AppendLine("if errorlevel 1 (set /a tries+=1 & if %tries% GEQ 30 exit /b 1 & ping -n 2 127.0.0.1 >nul & goto retry)");
        sb.AppendLine($"start \"\" \"{exe}\"");
        sb.AppendLine("(goto) 2>nul & del \"%~f0\"");
        File.WriteAllText(script, sb.ToString(), new UTF8Encoding(false));

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false });
    }

    /// <summary>Убирает остатки прошлого обновления.</summary>
    public static void Cleanup()
    {
        try { var f = Path.Combine(UpdateDir, "CoreLauncher.new.exe"); if (File.Exists(f)) File.Delete(f); } catch { }
    }
}
