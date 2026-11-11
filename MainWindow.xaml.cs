using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CmlLib.Core.Auth;
using CoreLauncher.Core;

namespace CoreLauncher;

public partial class MainWindow : Window
{
    readonly LauncherSettings _s = LauncherSettings.Load();
    readonly GameService _game = new();
    MSession? _msSession;
    bool _busy, _loading, _noSave;
    List<string> _versions = new();
    Action<bool>? _fireEnable;
    FrameworkElement[] _pages = Array.Empty<FrameworkElement>();

    public static readonly Dictionary<string, string> Accents = new()
    {
        ["Янтарь"] = "#FFC857", ["Коралл"] = "#FF8A7A", ["Мята"] = "#7DE0B5", ["Лаванда"] = "#B7A4FF",
    };

    public MainWindow()
    {
        InitializeComponent();
        Paths.EnsureCreated();
        SkinService.EnsureDefaults();
        _pages = new FrameworkElement[] { PagePlay, PageProfiles, PageMods, PageSkins, PageSettings };

        ApplyAccent(_s.Accent);
        var (fire, enable) = PixelScene.Campfire(9);
        CampfireHost.Content = fire; _fireEnable = enable;

        NickBox.Text = _s.Nickname;
        NickBox.LostFocus += (_, _) => { _s.Nickname = NickBox.Text.Trim(); _s.Save(); UpdateGreeting(); };
        (_s.Mode == AccountMode.Microsoft ? ModeMs : ModeOffline).IsChecked = true;
        ApplyMode();

        InitProfiles();
        InitSettings();
        InitSkins();
        InitUpdates();
        UpdateGreeting();

        Loaded += async (_, _) =>
        {
            Animate(_s.Animations);
            if (!_s.FirstRunDone) { WelcomeNick.Text = _s.Nickname == "Player" ? "" : _s.Nickname; Welcome.Visibility = Visibility.Visible; WelcomeNick.Focus(); }
            await LoadVersionsAsync();
            _ = AutoCheckUpdateAsync();
        };
    }

    // ---------- тема ----------
    void ApplyAccent(string name)
    {
        if (!Accents.TryGetValue(name, out var hex)) { name = "Янтарь"; hex = Accents[name]; }
        var c = (Color)ColorConverter.ConvertFromString(hex);
        Application.Current.Resources["Accent"] = new SolidColorBrush(c);
        Application.Current.Resources["AccentColor"] = c;
        _s.Accent = name;
    }

    void Animate(bool on)
    {
        PixelScene.Stars(StarsCanvas, 70, Math.Max(ActualWidth - 240, 900), Math.Max(ActualHeight, 700), on);
        _fireEnable?.Invoke(on);
    }

    void UpdateGreeting()
    {
        var h = DateTime.Now.Hour;
        var part = h < 6 ? "Тихой ночи" : h < 12 ? "Доброе утро" : h < 18 ? "Добрый день" : "Добрый вечер";
        GreetText.Text = $"{part}, {(string.IsNullOrWhiteSpace(_s.Nickname) ? "путник" : _s.Nickname)}. Устраивайся поудобнее.";
    }

    // ---------- окно и навигация ----------
    void DragWindow(object s, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Close_Click(object s, RoutedEventArgs e) { Close(); }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_noSave) { try { ReadSettings(); _s.Save(); } catch { } }
        base.OnClosing(e);
    }

    void Nav_Click(object s, RoutedEventArgs e)
    {
        if (_pages.Length == 0) return;
        var tag = (string)((RadioButton)s).Tag;
        FrameworkElement page = tag switch
        {
            "Profiles" => PageProfiles, "Mods" => PageMods, "Skins" => PageSkins, "Settings" => PageSettings, _ => PagePlay,
        };
        foreach (var p in _pages) p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        page.Opacity = 0;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        if (tag == "Mods") RefreshMods();
        if (tag == "Skins") RefreshSkinsPage();
        if (tag == "Profiles") LoadEditor();
        if (tag == "Settings") UpdateDataInfo();
    }

    void WelcomeGo_Click(object s, RoutedEventArgs e)
    {
        var nick = WelcomeNick.Text.Trim();
        if (!Regex.IsMatch(nick, "^[A-Za-z0-9_]{3,16}$"))
        {
            WelcomeNick.BorderBrush = (Brush)FindResource("Danger");
            return;
        }
        _s.Nickname = nick; NickBox.Text = nick; _s.FirstRunDone = true; _s.Save();
        UpdateGreeting();
        Welcome.Visibility = Visibility.Collapsed;
    }

    // ---------- аккаунт ----------
    void Mode_Click(object s, RoutedEventArgs e) { ApplyMode(); _s.Mode = ModeMs.IsChecked == true ? AccountMode.Microsoft : AccountMode.Offline; _s.Save(); }

    void ApplyMode()
    {
        bool ms = ModeMs.IsChecked == true;
        OfflinePanel.Visibility = ms ? Visibility.Collapsed : Visibility.Visible;
        MsPanel.Visibility = ms ? Visibility.Visible : Visibility.Collapsed;
    }

    async void MsLogin_Click(object s, RoutedEventArgs e) => await DoMicrosoftLoginAsync();

    async Task<bool> DoMicrosoftLoginAsync()
    {
        try
        {
            MsStatus.Text = "Открываем окно входа Microsoft…";
            _msSession = await GameService.MicrosoftLoginAsync();
            MsStatus.Text = $"✔ Вход выполнен: {_msSession.Username}";
            return true;
        }
        catch (Exception ex)
        {
            MsStatus.Text = "Не удалось войти: " + ex.Message;
            return false;
        }
    }

    // ---------- версии ----------
    async Task LoadVersionsAsync()
    {
        StatusText.Text = "Загружаем список версий…";
        _versions = await _game.GetVersionsAsync(_s.ShowSnapshots);
        if (_versions.Count == 0) { StatusText.Text = "Не удалось получить версии (нет сети?)"; return; }

        bool changed = false;
        // по умолчанию - свежая 1.21.x: на ней Fabric (моды, скины) работает стабильно
        var preferred = _versions.FirstOrDefault(v => v.StartsWith("1.")) ?? _versions[0];
        foreach (var p in _s.Profiles.Where(p => string.IsNullOrEmpty(p.McVersion))) { p.McVersion = preferred; changed = true; }
        if (changed) _s.Save();
        PVersion.ItemsSource = _versions;
        LoadEditor();
        UpdateProfileInfo();
        StatusText.Text = "Готов к запуску";
    }

    // ---------- запуск ----------
    async void Play_Click(object s, RoutedEventArgs e)
    {
        if (_busy) return;
        var prof = _s.ActiveProfile;
        if (string.IsNullOrEmpty(prof.McVersion)) { StatusText.Text = "В профиле не выбрана версия - открой «Профили»"; return; }

        ReadSettings(); _s.Save();
        _busy = true; PlayBtn.IsEnabled = false;
        try
        {
            MSession session;
            if (_s.Mode == AccountMode.Microsoft)
            {
                if (_msSession is null && !await DoMicrosoftLoginAsync()) return;
                session = _msSession!;
            }
            else
            {
                var nick = NickBox.Text.Trim();
                if (!Regex.IsMatch(nick, "^[A-Za-z0-9_]{3,16}$"))
                {
                    StatusText.Text = "Ник: 3-16 символов, только латиница, цифры и _";
                    return;
                }
                session = GameService.OfflineSession(nick);
            }

            var progress = new Progress<LaunchProgress>(p => { StatusText.Text = p.Text; Bar.Value = p.Percent; });
            var result = await _game.LaunchAsync(prof, session, _s, progress);
            var process = result.Process;
            StatusText.Text = "Игра запущена. Приятной игры!";

            if (_s.OnLaunch == LaunchBehavior.Hide) Hide();
            else if (_s.OnLaunch == LaunchBehavior.Minimize) WindowState = WindowState.Minimized;

            process.Exited += (_, _) => Dispatcher.Invoke(() =>
            {
                if (_s.ReopenOnExit || _s.OnLaunch != LaunchBehavior.Keep)
                {
                    if (!IsVisible) Show();
                    WindowState = WindowState.Normal; Activate();
                }
                Bar.Value = 0;
                StatusText.Text = process.ExitCode == 0
                    ? "Игра закрыта. Возвращайся - костёр ещё тёплый."
                    : $"Игра завершилась с ошибкой (код {process.ExitCode}). " +
                      (result.LogPath != null ? $"Журнал: {result.LogPath}" : "Включи журнал в настройках, чтобы найти причину.");
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка запуска: " + ex.Message;
        }
        finally
        {
            _busy = false; PlayBtn.IsEnabled = true;
        }
    }

    static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}


