using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoreLauncher.Core;

public enum AccountMode { Offline, Microsoft }
public enum SkinSystem { Css, Official, ElyBy, TLauncher }
public enum Loader { Vanilla, Fabric, Quilt, Forge, NeoForge, LiteLoader, Rift, ModLoader }
public enum LaunchBehavior { Hide, Minimize, Keep }

public class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Новый профиль";
    public string McVersion { get; set; } = "";
    public Loader Loader { get; set; } = Loader.Fabric;
    /// <summary>0 = брать из общих настроек</summary>
    public int RamMb { get; set; }
    public override string ToString() => Name;
}

public class LauncherSettings
{
    // --- аккаунт
    public string Nickname { get; set; } = "Minecrafter";
    public AccountMode Mode { get; set; } = AccountMode.Offline;

    // --- профили
    public List<Profile> Profiles { get; set; } = new();
    public string? ActiveProfileId { get; set; }
    public bool ShowSnapshots { get; set; }
    /// <summary>Показывать старые беты и альфы (отдельно от снапшотов).</summary>
    public bool ShowOldVersions { get; set; }

    // --- игра
    public bool AutoRam { get; set; } = true;
    public int RamMb { get; set; } = 4096;
    public string JavaPath { get; set; } = "";
    public string JvmPreset { get; set; } = "Стандарт";
    public string JvmArgs { get; set; } = "";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool Fullscreen { get; set; }
    public string ServerIp { get; set; } = "";

    // --- лаунчер
    public LaunchBehavior OnLaunch { get; set; } = LaunchBehavior.Hide;
    public bool ReopenOnExit { get; set; } = true;
    public bool SaveGameLog { get; set; } = true;
    public string Accent { get; set; } = "Апельсин";
    /// <summary>Версия оформления: при переходе на новое один раз сбрасываем акцент.</summary>
    public int ThemeVersion { get; set; }
    public bool Animations { get; set; } = true;
    public bool FirstRunDone { get; set; }
    public string UpdateSource { get; set; } = "github:SGM133/CORE-LAUNCHER";
    public bool AutoCheckUpdates { get; set; } = true;

    // --- скины (C.S.S. и остальные)
    public SkinSystem SkinSystem { get; set; } = SkinSystem.Css;
    public bool ApplySkins { get; set; } = true;
    public string? ActiveSkin { get; set; }
    public string? ActiveCape { get; set; }
    public bool SlimModel { get; set; }

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public Profile ActiveProfile =>
        Profiles.FirstOrDefault(p => p.Id == ActiveProfileId) ?? Profiles.First();

    public static LauncherSettings Load()
    {
        LauncherSettings s;
        try
        {
            s = File.Exists(Paths.SettingsFile)
                ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(Paths.SettingsFile), Json) ?? new()
                : new();
        }
        catch { s = new(); }
        if (s.Profiles.Count == 0)
            s.Profiles.Add(new Profile { Name = "Основной" });
        return s;
    }

    public void Save()
    {
        Paths.EnsureCreated();
        File.WriteAllText(Paths.SettingsFile, JsonSerializer.Serialize(this, Json));
    }

    public static readonly Dictionary<string, string> JvmPresets = new()
    {
        ["Стандарт"] = "",
        ["Оптимизированный (G1)"] =
            "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions " +
            "-XX:+DisableExplicitGC -XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8M " +
            "-XX:G1ReservePercent=20 -XX:G1HeapWastePercent=5 -XX:G1MixedGCCountTarget=4 " +
            "-XX:InitiatingHeapOccupancyPercent=15 -XX:G1MixedGCLiveThresholdPercent=90 " +
            "-XX:G1RSetUpdatingPauseTimePercent=5 -XX:SurvivorRatio=32 -XX:+PerfDisableSharedMem " +
            "-XX:MaxTenuringThreshold=1",
        ["Экономный (слабый ПК)"] = "-XX:+UseSerialGC -XX:TieredStopAtLevel=1",
    };
}

