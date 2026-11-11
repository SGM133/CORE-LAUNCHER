using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.ProcessBuilder;

namespace CoreLauncher.Core;

public record LaunchProgress(string Text, double Percent);

public record LaunchResult(Process Process, string? LogPath);

/// <summary>Вся работа с Minecraft: версии, вход, установка, запуск профилей.</summary>
public sealed class GameService
{
    /// <summary>Общие библиотеки/версии/ассеты, а игровая папка (моды, миры) - у каждого профиля своя.</summary>
    static MinecraftPath PathFor(Profile p)
    {
        var shared = Paths.Shared;
        var path = new MinecraftPath(Paths.Instance(p.Id))
        {
            Library = Path.Combine(shared, "libraries"),
            Versions = Path.Combine(shared, "versions"),
            Resource = Path.Combine(shared, "resources"),
            Assets = Path.Combine(shared, "assets"),
            Runtime = Path.Combine(shared, "runtime"),
        };
        path.CreateDirs();
        return path;
    }

    /// <summary>Список версий. Без сети - показываем то, что уже установлено.</summary>
    public async Task<List<string>> GetVersionsAsync(bool snapshots)
    {
        try
        {
            var launcher = new MinecraftLauncher(new MinecraftPath(Paths.Shared));
            var all = await launcher.GetAllVersionsAsync();
            return all
                .Where(v => snapshots || string.Equals(v.Type, "release", StringComparison.OrdinalIgnoreCase))
                .Where(v => !v.Name.StartsWith("fabric-loader", StringComparison.OrdinalIgnoreCase))
                .Select(v => v.Name).ToList();
        }
        catch
        {
            var dir = Path.Combine(Paths.Shared, "versions");
            return Directory.Exists(dir)
                ? Directory.GetDirectories(dir).Select(d => Path.GetFileName(d)!)
                    .Where(n => !n.StartsWith("fabric-loader")).ToList()
                : new();
        }
    }

    /// <summary>Офлайн-сессия - UUID считается из ника.</summary>
    public static MSession OfflineSession(string nick) => MSession.CreateOfflineSession(nick);

    /// <summary>Лицензионный вход через Microsoft (сначала тихо, иначе окно входа).</summary>
    public static async Task<MSession> MicrosoftLoginAsync()
    {
        var handler = JELoginHandlerBuilder.BuildDefault();
        return await handler.Authenticate();
    }

    public static int RecommendedRamMb()
    {
        long totalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024;
        return (int)Math.Clamp(totalMb / 2 / 512 * 512, 2048, 8192);
    }

    public async Task<LaunchResult> LaunchAsync(Profile prof, MSession session, LauncherSettings s,
        IProgress<LaunchProgress> progress)
    {
        if (string.IsNullOrWhiteSpace(prof.McVersion)) throw new InvalidOperationException("В профиле не выбрана версия игры.");
        Directory.CreateDirectory(Paths.InstanceMods(prof.Id));

        var versionId = prof.McVersion;
        if (prof.Loader == Loader.Fabric)
        {
            if (prof.McVersion.StartsWith("26"))
                progress.Report(new("Внимание: Fabric на сборках 26.x пока нестабилен - при вылете выбери 1.21.x", 0));
            progress.Report(new("Готовим Fabric…", 0));
            versionId = await FabricService.EnsureAsync(prof.McVersion);

            if (s.ApplySkins)
            {
                progress.Report(new("Настраиваем скины…", 0));
                try { await SkinService.PrepareInstanceAsync(prof, s, session.Username ?? s.Nickname); }
                catch (Exception ex) { progress.Report(new("Скины не подключены: " + ex.Message, 0)); }
            }
        }

        var launcher = new MinecraftLauncher(PathFor(prof));
        launcher.FileProgressChanged += (_, e) =>
            progress.Report(new($"Загрузка файлов: {e.Name}",
                e.TotalTasks == 0 ? 0 : 100.0 * e.ProgressedTasks / e.TotalTasks));

        int ram = s.AutoRam ? RecommendedRamMb() : s.RamMb;
        if (prof.RamMb > 0) ram = prof.RamMb;

        var opt = new MLaunchOption
        {
            Session = session,
            MaximumRamMb = ram,
            MinimumRamMb = Math.Min(ram, 1024),
            ScreenWidth = s.Width,
            ScreenHeight = s.Height,
            FullScreen = s.Fullscreen,
            JavaPath = string.IsNullOrWhiteSpace(s.JavaPath) ? null : s.JavaPath,
        };
        if (!string.IsNullOrWhiteSpace(s.ServerIp))
        {
            var parts = s.ServerIp.Trim().Split(':');
            opt.ServerIp = parts[0];
            if (parts.Length > 1 && int.TryParse(parts[1], out var port)) opt.ServerPort = port;
        }

        var jvm = (LauncherSettings.JvmPresets.GetValueOrDefault(s.JvmPreset, "") + " " + s.JvmArgs).Trim();
        if (jvm.Length > 0)
            opt.ExtraJvmArguments = jvm.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(a => new MArgument(a)).ToList();

        progress.Report(new("Проверка файлов игры…", 0));
        var process = await launcher.InstallAndBuildProcessAsync(versionId, opt);
        process.EnableRaisingEvents = true;

        string? log = null;
        if (s.SaveGameLog)
        {
            Directory.CreateDirectory(Paths.Logs);
            log = Path.Combine(Paths.Logs, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{prof.Name}.log");
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            var writer = new StreamWriter(log, false) { AutoFlush = true };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (writer) writer.WriteLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (writer) writer.WriteLine(e.Data); };
            process.Exited += (_, _) => { lock (writer) writer.Dispose(); };
        }

        progress.Report(new("Запуск игры…", 100));
        process.Start();
        if (s.SaveGameLog) { process.BeginOutputReadLine(); process.BeginErrorReadLine(); }
        return new LaunchResult(process, log);
    }
}
