using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using CoreLauncher.Core;

namespace CoreLauncher;

public partial class MainWindow
{
    static readonly (string label, int w, int h)[] SizePresets =
    {
        ("Свой размер", 0, 0), ("1280 × 720", 1280, 720), ("1600 × 900", 1600, 900), ("1920 × 1080", 1920, 1080), ("2560 × 1440", 2560, 1440),
    };

    void InitSettings()
    {
        _loading = true;
        RamSlider.Maximum = _ramMax;
        RamSlider.Value = Math.Clamp(_s.RamMb, 1024, _ramMax);
        AutoRamCheck.IsChecked = _s.AutoRam;
        WidthBox.Text = _s.Width.ToString(); HeightBox.Text = _s.Height.ToString();
        FullscreenCheck.IsChecked = _s.Fullscreen;
        ServerBox.Text = _s.ServerIp;
        JavaBox.Text = _s.JavaPath; JvmBox.Text = _s.JvmArgs;

        SizePreset.ItemsSource = SizePresets.Select(p => p.label).ToList();
        SizePreset.SelectedIndex = Math.Max(0, Array.FindIndex(SizePresets, p => p.w == _s.Width && p.h == _s.Height));
        JvmPreset.ItemsSource = LauncherSettings.JvmPresets.Keys.ToList();
        JvmPreset.SelectedItem = LauncherSettings.JvmPresets.ContainsKey(_s.JvmPreset) ? _s.JvmPreset : "Стандарт";
        BehaviorBox.ItemsSource = new[] { "Скрыть лаунчер", "Свернуть лаунчер", "Оставить как есть" };
        BehaviorBox.SelectedIndex = (int)_s.OnLaunch;

        ReopenCheck.IsChecked = _s.ReopenOnExit; LogCheck.IsChecked = _s.SaveGameLog;
        SkinsCheck.IsChecked = _s.ApplySkins; AnimCheck.IsChecked = _s.Animations;
        _loading = false;
        UpdateRamUi();

        // выбор акцента
        foreach (var kv in MainWindow.Accents)
        {
            var b = new Button
            {
                Width = 38, Height = 38, Margin = new Thickness(0, 0, 10, 0), Tag = kv.Key, ToolTip = kv.Key, Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(kv.Value)),
                BorderThickness = new Thickness(3),
            };
            b.Click += (_, _) => { ApplyAccent(kv.Key); _s.Save(); MarkAccent(); };
            AccentRow.Children.Add(b);
        }
        MarkAccent();

        // автосохранение
        foreach (var tb in new[] { WidthBox, HeightBox, ServerBox, JavaBox, JvmBox }) tb.LostFocus += (_, _) => { ReadSettings(); _s.Save(); };
        foreach (var cb in new[] { FullscreenCheck, ReopenCheck, LogCheck, SkinsCheck }) cb.Click += (_, _) => { ReadSettings(); _s.Save(); };
        foreach (var co in new[] { JvmPreset, BehaviorBox }) co.SelectionChanged += (_, _) => { if (!_loading) { ReadSettings(); _s.Save(); } };
    }

    void MarkAccent()
    {
        foreach (Button b in AccentRow.Children)
            b.BorderBrush = (string)b.Tag == _s.Accent ? Brushes.White : (Brush)FindResource("Line");
    }

    void ReadSettings()
    {
        if (_loading) return;
        _s.Nickname = NickBox.Text.Trim().Length > 0 ? NickBox.Text.Trim() : _s.Nickname;
        _s.Mode = ModeMs.IsChecked == true ? AccountMode.Microsoft : AccountMode.Offline;
        _s.AutoRam = AutoRamCheck.IsChecked == true;
        _s.RamMb = (int)RamSlider.Value;
        if (int.TryParse(WidthBox.Text, out var w) && w >= 640) _s.Width = w;
        if (int.TryParse(HeightBox.Text, out var h) && h >= 480) _s.Height = h;
        _s.Fullscreen = FullscreenCheck.IsChecked == true;
        _s.ServerIp = ServerBox.Text.Trim();
        _s.JavaPath = JavaBox.Text.Trim(); _s.JvmArgs = JvmBox.Text.Trim();
        _s.JvmPreset = JvmPreset.SelectedItem as string ?? "Стандарт";
        _s.OnLaunch = (LaunchBehavior)Math.Max(0, BehaviorBox.SelectedIndex);
        _s.ReopenOnExit = ReopenCheck.IsChecked == true; _s.SaveGameLog = LogCheck.IsChecked == true;
        _s.ApplySkins = SkinsCheck.IsChecked == true;
    }

    void UpdateRamUi()
    {
        bool auto = AutoRamCheck.IsChecked == true;
        RamSlider.IsEnabled = !auto;
        long totalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024;
        int rec = GameService.RecommendedRamMb();
        RamLabel.Text = auto ? $"авто · {rec / 1024.0:0.#} ГБ" : $"{RamSlider.Value / 1024.0:0.#} ГБ";
        RamHint.Text = $"В системе {totalMb / 1024.0:0.#} ГБ. Хватит 4 ГБ для ванили и 6-8 ГБ для сборок с модами. Профиль может задать свой объём.";
    }

    void AutoRam_Click(object s, RoutedEventArgs e) { UpdateRamUi(); ReadSettings(); _s.Save(); }
    void Ram_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RamLabel is null || RamHint is null || AutoRamCheck is null) return;
        UpdateRamUi();
        if (!_loading) { _s.RamMb = (int)e.NewValue; _s.Save(); }
    }

    void SizePreset_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_loading || SizePreset.SelectedIndex <= 0) return;
        var p = SizePresets[SizePreset.SelectedIndex];
        WidthBox.Text = p.w.ToString(); HeightBox.Text = p.h.ToString();
        ReadSettings(); _s.Save();
    }

    void Anim_Click(object s, RoutedEventArgs e) { _s.Animations = AnimCheck.IsChecked == true; _s.Save(); Animate(_s.Animations); }

    void BrowseJava_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Java (javaw.exe;java.exe)|javaw.exe;java.exe" };
        if (dlg.ShowDialog() == true) { JavaBox.Text = dlg.FileName; ReadSettings(); _s.Save(); }
    }

    void UpdateDataInfo()
    {
        int logs = Directory.Exists(Paths.Logs) ? Directory.GetFiles(Paths.Logs).Length : 0;
        DataInfo.Text = $"Данные лаунчера: {Paths.Root} · журналов: {logs}";
    }

    void OpenRoot_Click(object s, RoutedEventArgs e) => OpenFolder(Paths.Root);
    void OpenLogs_Click(object s, RoutedEventArgs e) => OpenFolder(Paths.Logs);

    void ClearLogs_Click(object s, RoutedEventArgs e)
    {
        if (!Directory.Exists(Paths.Logs)) return;
        foreach (var f in Directory.GetFiles(Paths.Logs, "*.log")) { try { File.Delete(f); } catch { } }
        UpdateDataInfo();
    }

    void Reset_Click(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show("Сбросить все настройки лаунчера и профили?\n\nМиры, моды и скины на диске не удаляются. Лаунчер закроется.",
                "Core Launcher", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { File.Delete(Paths.SettingsFile); } catch { }
        _noSave = true; Close();
    }
}
