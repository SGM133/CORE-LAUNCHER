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
        ["Апельсин"] = "#FF8C1A", ["Тыква"] = "#F26B1D", ["Абрикос"] = "#FFA85C", ["Янтарь"] = "#FFC857",
    };

    // фон каждого раздела
    static readonly Dictionary<string, string> Backgrounds = new()
    {
        ["Play"] = "bg_play", ["Console"] = "bg_settings", ["Skins"] = "bg_skins", ["Mods"] = "bg_mods", ["Settings"] = "bg_settings", ["Profiles"] = "bg_profiles",
    };
    readonly Dictionary<string, ImageSource> _bgCache = new();

    void SetBackground(string tag, bool animate = true)
    {
        if (!Backgrounds.TryGetValue(tag, out var file)) return;
        try
        {
            if (!_bgCache.TryGetValue(file, out var img))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri($"pack://application:,,,/Assets/{file}.jpg");
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit(); bmp.Freeze();
                _bgCache[file] = img = bmp;
            }
            BgA.Source = BgB.Source ?? img;   // прежний фон остаётся снизу, новый проявляется поверх
            BgB.Source = img;
            BgB.BeginAnimation(OpacityProperty, animate
                ? new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380))
                : new DoubleAnimation(1, 1, TimeSpan.Zero));
        }
        catch { }
    }

    public MainWindow()
    {
        InitializeComponent();
        Paths.EnsureCreated();
        SkinService.EnsureDefaults();
        _pages = new FrameworkElement[] { PagePlay, PageProfiles, PageMods, PageSkins, PageConsole, PageSettings };

        // без расспросов при первом запуске: ник по умолчанию - Minecrafter, поменять можно на вкладке «Играть»
        if (!_s.FirstRunDone)
        {
            if (string.IsNullOrWhiteSpace(_s.Nickname) || _s.Nickname == "Player") _s.Nickname = "Minecrafter";
            _s.FirstRunDone = true; _s.Save();
        }

        SetNav(NavPlay, "Играть", PixelScene.IcoPlay);
        SetNav(NavProfiles, "Профили", PixelScene.IcoProfiles);
        SetNav(NavMods, "Моды", PixelScene.IcoMods);
        SetNav(NavSkins, "Скины", PixelScene.IcoSkins);
        SetNav(NavConsole, "Консоль", PixelScene.IcoConsole);
        SetNav(NavSettings, "Настройки", PixelScene.IcoSettings);
        InitConsole();

        if (_s.ThemeVersion < 2) { _s.Accent = "Апельсин"; _s.ThemeVersion = 2; _s.Save(); }   // переход на оранжевый стиль
        ApplyAccent(_s.Accent);
        SetBackground("Play", animate: false);
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
            await LoadVersionsAsync();
            _ = AutoCheckUpdateAsync();
        };
    }

    // ---------- тема ----------
    void ApplyAccent(string name)
    {
        if (!Accents.TryGetValue(name, out var hex)) { name = "Апельсин"; hex = Accents[name]; }
        var c = (Color)ColorConverter.ConvertFromString(hex);
        Application.Current.Resources["Accent"] = new SolidColorBrush(c);
        Application.Current.Resources["AccentColor"] = c;
        _s.Accent = name;
    }

    void Animate(bool on)
    {
        PixelScene.Stars(StarsCanvas, 36, Math.Max(ActualWidth - 240, 900), Math.Max(ActualHeight, 700), on);
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
            "Profiles" => PageProfiles, "Mods" => PageMods, "Skins" => PageSkins, "Console" => PageConsole, "Settings" => PageSettings, _ => PagePlay,
        };
        Log.Info("Открыт раздел: " + tag);
        if (tag == "Console") ConsoleBox.ScrollToEnd();
        foreach (var p in _pages) p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        page.Opacity = 0;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        SetBackground(tag);
        if (tag == "Mods") RefreshMods();
        if (tag == "Skins") RefreshSkinsPage();
        if (tag == "Profiles") LoadEditor();
        if (tag == "Settings") UpdateDataInfo();
    }

    /// <summary>Кнопка раздела: пиксельный значок + подпись.</summary>
    void SetNav(RadioButton b, string title, string[] icon)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var ico = PixelScene.Icon(icon, 2);
        ico.Margin = new Thickness(0, 0, 14, 0);
        sp.Children.Add(ico);
        sp.Children.Add(new TextBlock { Text = title, FontFamily = (FontFamily)FindResource("Pixel"), VerticalAlignment = VerticalAlignment.Center });
        b.Content = sp;
    }

    // ---------- консоль: всё, что лаунчер делал за этот сеанс ----------
    void InitConsole()
    {
        ConsoleBox.Text = Log.SessionText;
        ConsoleFile.Text = Log.FilePath == null ? "Журнал на диск не пишется" : "Журнал сеанса: " + Path.GetFileName(Log.FilePath);
        Log.Line += line => Dispatcher.BeginInvoke(() =>
        {
            if (ConsoleBox.Text.Length > 300_000) ConsoleBox.Text = ConsoleBox.Text[100_000..];
            ConsoleBox.AppendText(line + Environment.NewLine);
            if (PageConsole.Visibility == Visibility.Visible) ConsoleBox.ScrollToEnd();
        });

        // каждое нажатие кнопки - в журнал
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Primitives.ButtonBase),
            System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((s, _) =>
            {
                if (s is not System.Windows.Controls.Primitives.ButtonBase b || b is System.Windows.Controls.Primitives.RepeatButton) return;
                var name = b.Content as string ?? System.Windows.Automation.AutomationProperties.GetName(b);
                if (string.IsNullOrEmpty(name)) name = b.ToolTip as string ?? b.Name;
                if (!string.IsNullOrEmpty(name)) Log.Info("Нажато: " + name.Trim());
            }));

        // все строки состояния тоже дублируем в журнал (кроме потока имён скачиваемых файлов)
        foreach (var tb in new[] { StatusText, SearchStatus, OnlineStatus, LookupStatus, UpdStatus, MsStatus, ProfileStatus })
        {
            var d = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            d.AddValueChanged(tb, (_, _) =>
            {
                var t = tb.Text;
                if (string.IsNullOrWhiteSpace(t)) return;
                if (t.StartsWith("Загрузка файлов:") || t.StartsWith("Forge: ") || t.StartsWith("NeoForge: ")) return;
                Log.Info(t);
            });
        }
    }

    void Doc_Click(object s, RoutedEventArgs e)
    {
        new DocWindow((string)((FrameworkElement)s).Tag) { Owner = this }.ShowDialog();
    }

    void ConsoleCopy_Click(object s, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ConsoleBox.Text); ConsoleFile.Text = "Скопировано в буфер обмена."; } catch { }
    }

    void ConsoleFolder_Click(object s, RoutedEventArgs e) => OpenFolder(Log.Dir);

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
        _versions = await _game.GetVersionsAsync(_s.ShowSnapshots, _s.ShowOldVersions);
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
            Log.Error("Подробности ошибки запуска: " + ex);
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


