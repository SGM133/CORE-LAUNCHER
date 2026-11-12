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
    /// <summary>Версии, которые создал сам лаунчер под загрузчики, в список игры не попадают.</summary>
    static bool IsLoaderVersion(string name) =>
        name.StartsWith("fabric-loader", StringComparison.OrdinalIgnoreCase) || name.StartsWith("quilt-loader", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("forge", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-LiteLoader", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("-rift-", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-ModLoader", StringComparison.OrdinalIgnoreCase);

    /// <summary>Список версий: релизы всегда, снапшоты и старые беты/альфы - по своим галочкам. Без сети - то, что уже установлено.</summary>
    public async Task<List<string>> GetVersionsAsync(bool snapshots, bool oldVersions)
    {
        try
        {
            var launcher = new MinecraftLauncher(new MinecraftPath(Paths.Shared));
            var all = await launcher.GetAllVersionsAsync();
            return all
                .Where(v =>
                {
                    var t = (v.Type ?? "").ToLowerInvariant();
                    return t == "release" || (snapshots && t == "snapshot") || (oldVersions && (t == "old_beta" || t == "old_alpha"));
                })
                .Where(v => !IsLoaderVersion(v.Name))
                .Select(v => v.Name).ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Список версий не загрузился, показываем установленные", ex);
            var dir = Path.Combine(Paths.Shared, "versions");
            return Directory.Exists(dir)
                ? Directory.GetDirectories(dir).Select(d => Path.GetFileName(d)!)
                    .Where(n => !IsLoaderVersion(n)).ToList()
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

        var launcher = new MinecraftLauncher(PathFor(prof));
        launcher.FileProgressChanged += (_, e) =>
            progress.Report(new($"Загрузка файлов: {e.Name}",
                e.TotalTasks == 0 ? 0 : 100.0 * e.ProgressedTasks / e.TotalTasks));

        Log.Info($"Запуск профиля «{prof.Name}»: Minecraft {prof.McVersion}, {Loaders.Title(prof.Loader)}, игрок {session.Username}");
        var versionId = prof.McVersion;
        if (prof.Loader != Loader.Vanilla)
        {
            if (prof.Loader == Loader.Fabric && prof.McVersion.StartsWith("26"))
                progress.Report(new("Внимание: Fabric на сборках 26.x пока нестабилен - при вылете выбери 1.21.x", 0));
            versionId = await Loaders.EnsureAsync(prof, launcher, progress);
            Log.Info("Версия для запуска: " + versionId);

            if (s.ApplySkins && Loaders.SupportsSkins(prof.Loader))
            {
                progress.Report(new("Настраиваем скины…", 0));
                try { await SkinService.PrepareInstanceAsync(prof, s, session.Username ?? s.Nickname); }
                catch (Exception ex) { progress.Report(new("Скины не подключены: " + ex.Message, 0)); }
            }
        }

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
            bool open = true;
            void Put(string? line)
            {
                if (line == null) return;
                lock (writer) { if (open) writer.WriteLine(line); }
                Log.Game(line);   // и в консоль лаунчера
            }
            process.OutputDataReceived += (_, e) => Put(e.Data);
            process.ErrorDataReceived += (_, e) => Put(e.Data);
            process.Exited += (_, _) => { lock (writer) { open = false; writer.Dispose(); } };
        }

        progress.Report(new("Запуск игры…", 100));
        process.Start();
        if (s.SaveGameLog) { process.BeginOutputReadLine(); process.BeginErrorReadLine(); }
        return new LaunchResult(process, log);
    }
}
