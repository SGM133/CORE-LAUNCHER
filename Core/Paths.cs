using System;
using System.IO;

namespace CoreLauncher.Core;

/// <summary>Все пути лаунчера. Свой каталог, чтобы не трогать обычный .minecraft.</summary>
public static class Paths
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".corelauncher");

    /// <summary>Общие для всех профилей: библиотеки, версии, ассеты, Java.</summary>
    public static string Shared => Path.Combine(Root, "minecraft");
    /// <summary>Отдельная папка на каждый профиль: моды, миры, настройки игры.</summary>
    public static string Instances => Path.Combine(Root, "instances");
    public static string Instance(string id) => Path.Combine(Instances, id);
    public static string InstanceMods(string id) => Path.Combine(Instance(id), "mods");

    /// <summary>Библиотека C.S.S. (Core Skin System).</summary>
    public static string CssSkins => Path.Combine(Root, "css", "skins");
    public static string CssCapes => Path.Combine(Root, "css", "capes");

    public static string Logs => Path.Combine(Root, "logs");
    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static void EnsureCreated()
    {
        foreach (var d in new[] { Root, Shared, Instances, CssSkins, CssCapes, Logs })
            Directory.CreateDirectory(d);
    }
}
