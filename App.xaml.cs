using System;
using System.IO;
using System.Windows;
using CoreLauncher.Core;

namespace CoreLauncher;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Report(e.Exception);
            e.Handled = true;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception);
    }

    static void Report(Exception? ex)
    {
        string path = "";
        try
        {
            Directory.CreateDirectory(Paths.Logs);
            path = Path.Combine(Paths.Logs, "launcher-crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:u}] {ex}\n\n");
        }
        catch { }
        MessageBox.Show("Core Launcher неожиданно остановился.\n\n" + (ex?.GetBaseException().Message ?? "неизвестная ошибка") +
                        (path.Length > 0 ? $"\n\nПодробности: {path}" : ""),
            "Core Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
