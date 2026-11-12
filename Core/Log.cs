using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CoreLauncher.Core;

/// <summary>
/// Журнал лаунчера. Один сеанс (от запуска до закрытия) = один файл в logs\sessions.
/// Если сеанс оборвался вылетом, файл получает приставку CRITICAL_ERROR_.
/// </summary>
public static class Log
{
    public const string CrashPrefix = "CRITICAL_ERROR_";
    const string EndMark = "[SESSION END]";

    public static event Action<string>? Line;
    public static string Dir => Path.Combine(Paths.Logs, "sessions");
    public static string? FilePath { get; private set; }

    static readonly object L = new();
    static readonly StringBuilder Buf = new();
    static StreamWriter? _w;
    static string _last = "";

    /// <summary>Всё, что написано за этот сеанс (для вкладки «Консоль»).</summary>
    public static string SessionText { get { lock (L) return Buf.ToString(); } }

    public static void Start()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            MarkOldCrashes();
            FilePath = Path.Combine(Dir, $"launcher_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
            _w = new StreamWriter(new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch { _w = null; }
        Info($"Core Launcher {UpdateService.CurrentText} запущен. Windows {Environment.OSVersion.Version}, .NET {Environment.Version}");
    }

    /// <summary>Сеансы, которые не дописали отметку о закрытии (вылет, зависание, выключение ПК), помечаем как критические.</summary>
    static void MarkOldCrashes()
    {
        foreach (var f in Directory.GetFiles(Dir, "launcher_*.log"))
        {
            try
            {
                string text;
                // файл живого соседнего лаунчера занят - его не трогаем
                using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None))
                using (var r = new StreamReader(fs)) text = r.ReadToEnd();
                if (text.Contains(EndMark)) continue;
                File.AppendAllText(f, $"[{DateTime.Now:HH:mm:ss}] [CRITICAL] Сеанс не был закрыт пользователем: лаунчер вылетел или был остановлен принудительно.{Environment.NewLine}");
                File.Move(f, Path.Combine(Dir, CrashPrefix + Path.GetFileName(f)), true);
            }
            catch { }
        }
    }

    public static void Info(string text) => Write("INFO", text);
    public static void Warn(string text) => Write("WARN", text);
    public static void Error(string text, Exception? ex = null) => Write("ERROR", ex == null ? text : $"{text}: {ex.GetBaseException().Message}");
    public static void Game(string text) => Write("GAME", text);

    static void Write(string level, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {text.TrimEnd()}";
        lock (L)
        {
            if (text == _last && level != "GAME") return;   // не повторяем одно и то же подряд
            _last = text;
            Buf.AppendLine(line);
            if (Buf.Length > 400_000) Buf.Remove(0, 100_000);
            try { _w?.WriteLine(line); } catch { }
        }
        try { Line?.Invoke(line); } catch { }
    }

    /// <summary>Обычное закрытие по желанию пользователя.</summary>
    public static void End()
    {
        lock (L)
        {
            if (_w == null) return;
            try { _w.WriteLine($"[{DateTime.Now:HH:mm:ss}] [INFO] {EndMark} Лаунчер закрыт пользователем."); _w.Dispose(); } catch { }
            _w = null;
        }
    }

    /// <summary>Вылет: дописываем причину и переименовываем файл в CRITICAL_ERROR_…</summary>
    public static string? Crash(Exception? ex)
    {
        lock (L)
        {
            try
            {
                _w?.WriteLine($"[{DateTime.Now:HH:mm:ss}] [CRITICAL] Лаунчер вылетел: {ex}");
                _w?.Dispose();
            }
            catch { }
            _w = null;
            try
            {
                if (FilePath != null && File.Exists(FilePath))
                {
                    var dst = Path.Combine(Dir, CrashPrefix + Path.GetFileName(FilePath));
                    File.Move(FilePath, dst, true);
                    FilePath = dst;
                }
            }
            catch { }
            return FilePath;
        }
    }
}
