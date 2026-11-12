using System;
using System.Threading.Tasks;
using System.Windows;
using CoreLauncher.Core;

namespace CoreLauncher;

public partial class App : Application
{
    public App()
    {
        Paths.EnsureCreated();
        Log.Start();
        DispatcherUnhandledException += (_, e) =>
        {
            Report(e.Exception);
            e.Handled = true;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception);
    }

    static readonly string[] Steps =
    {
        "Подкладываем дрова…", "Разжигаем костёр…", "Достаём скины из сундука…", "Греем чай…", "Почти уютно…",
    };

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;   // пока висит заставка, приложение не должно закрыться
            var splash = new SplashWindow();
            splash.Show();

            // заставка живёт ~2 секунды: шаги идут по очереди, последний - открытие основного окна
            for (int i = 0; i < Steps.Length - 1; i++)
            {
                splash.SetProgress((i + 0.5) / Steps.Length, Steps[i]);
                await Task.Delay(380);
            }
            splash.SetProgress((Steps.Length - 1.0) / Steps.Length, Steps[^1]);
            await Task.Delay(60);

            var main = new MainWindow();   // тяжёлая часть: настройки, версии, фоны
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            splash.SetProgress(1, "Готово");
            main.Show();
            splash.FadeOutAndClose();
        }
        catch (Exception ex) { Report(ex); Shutdown(1); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (e.ApplicationExitCode == 0) Log.End();   // закрыт по желанию пользователя
        base.OnExit(e);
    }

    static bool _reported;

    static void Report(Exception? ex)
    {
        if (_reported) return;
        _reported = true;
        var path = Log.Crash(ex) ?? "";
        MessageBox.Show("Core Launcher неожиданно остановился.\n\n" + (ex?.GetBaseException().Message ?? "неизвестная ошибка") +
                        (path.Length > 0 ? $"\n\nПодробности: {path}" : ""),
            "Core Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
