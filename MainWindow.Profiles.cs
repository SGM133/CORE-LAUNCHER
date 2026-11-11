using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CoreLauncher.Core;

namespace CoreLauncher;

public partial class MainWindow
{
    int _ramMax = 16384;

    void InitProfiles()
    {
        long totalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024;
        _ramMax = (int)Math.Clamp(totalMb * 3 / 4 / 512 * 512, 2048, 16384);
        PRam.Maximum = _ramMax;
        SnapshotsCheck.IsChecked = _s.ShowSnapshots;
        RefreshProfiles();
    }

    void RefreshProfiles()
    {
        _loading = true;
        ProfileList.ItemsSource = null; ProfileList.ItemsSource = _s.Profiles; ProfileList.SelectedItem = _s.ActiveProfile;
        ProfileBox.ItemsSource = null; ProfileBox.ItemsSource = _s.Profiles; ProfileBox.SelectedItem = _s.ActiveProfile;
        _loading = false;
        LoadEditor();
        UpdateProfileInfo();
    }

    void UpdateProfileInfo()
    {
        var p = _s.ActiveProfile;
        ProfileInfo.Text = $"{(p.Loader == Loader.Fabric ? "Fabric" : "Vanilla")} · Minecraft {(string.IsNullOrEmpty(p.McVersion) ? "—" : p.McVersion)}" +
                           (p.RamMb > 0 ? $" · {p.RamMb / 1024.0:0.#} ГБ" : "");
    }

    void ProfileBox_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_loading || ProfileBox.SelectedItem is not Profile p) return;
        _s.ActiveProfileId = p.Id; _s.Save();
        _loading = true; ProfileList.SelectedItem = p; _loading = false;
        LoadEditor(); UpdateProfileInfo();
    }

    void ProfileList_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_loading || ProfileList.SelectedItem is not Profile p) return;
        _s.ActiveProfileId = p.Id; _s.Save();
        _loading = true; ProfileBox.SelectedItem = p; _loading = false;
        LoadEditor(); UpdateProfileInfo();
    }

    void LoadEditor()
    {
        var p = _s.ActiveProfile;
        _loading = true;
        PName.Text = p.Name;
        var list = _versions.ToList();
        if (!string.IsNullOrEmpty(p.McVersion) && !list.Contains(p.McVersion)) list.Insert(0, p.McVersion);
        PVersion.ItemsSource = list;
        PVersion.SelectedItem = p.McVersion;
        (p.Loader == Loader.Fabric ? LoaderFabric : LoaderVanilla).IsChecked = true;
        PRamOwn.IsChecked = p.RamMb > 0;
        PRam.Value = Math.Clamp(p.RamMb > 0 ? p.RamMb : GameService.RecommendedRamMb(), 1024, _ramMax);
        PRam.IsEnabled = p.RamMb > 0;
        PRamLabel.Text = p.RamMb > 0 ? $"{PRam.Value / 1024.0:0.#} ГБ" : "из настроек";
        _loading = false;
        UpdateLoaderHint();
    }

    void UpdateLoaderHint()
    {
        if (LoaderHint is null || PVersion is null || LoaderFabric is null) return;
        var v = PVersion.SelectedItem as string ?? "";
        bool warn = LoaderFabric.IsChecked == true && v.StartsWith("26");
        LoaderHint.Text = warn
            ? "⚠ Fabric на сборках 26.x пока не запускается. Выбери 1.21.x или загрузчик Vanilla."
            : "Для модов и скинов нужен Fabric - лаунчер поставит его сам.";
        LoaderHint.Foreground = warn ? (System.Windows.Media.Brush)FindResource("Danger") : (System.Windows.Media.Brush)FindResource("Muted");
    }

    void LoaderOrVersion_Changed(object s, RoutedEventArgs e) => UpdateLoaderHint();

    void PRamOwn_Click(object s, RoutedEventArgs e)
    {
        PRam.IsEnabled = PRamOwn.IsChecked == true;
        PRamLabel.Text = PRamOwn.IsChecked == true ? $"{PRam.Value / 1024.0:0.#} ГБ" : "из настроек";
    }

    void PRam_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PRamLabel is not null && PRamOwn?.IsChecked == true) PRamLabel.Text = $"{e.NewValue / 1024.0:0.#} ГБ";
    }

    async void Snapshots_Click(object s, RoutedEventArgs e)
    {
        _s.ShowSnapshots = SnapshotsCheck.IsChecked == true; _s.Save();
        _versions = await _game.GetVersionsAsync(_s.ShowSnapshots);
        LoadEditor();
    }

    void SaveProfile_Click(object s, RoutedEventArgs e)
    {
        var p = _s.ActiveProfile;
        if (!string.IsNullOrWhiteSpace(PName.Text)) p.Name = PName.Text.Trim();
        if (PVersion.SelectedItem is string v) p.McVersion = v;
        p.Loader = LoaderFabric.IsChecked == true ? Loader.Fabric : Loader.Vanilla;
        p.RamMb = PRamOwn.IsChecked == true ? (int)PRam.Value : 0;
        _s.Save();
        RefreshProfiles();
        StatusText.Text = $"Профиль «{p.Name}» сохранён";
    }

    void NewProfile_Click(object s, RoutedEventArgs e)
    {
        var p = new Profile { Name = $"Профиль {_s.Profiles.Count + 1}", McVersion = _versions.FirstOrDefault() ?? "" };
        _s.Profiles.Add(p); _s.ActiveProfileId = p.Id; _s.Save();
        RefreshProfiles();
    }

    void DeleteProfile_Click(object s, RoutedEventArgs e)
    {
        if (_s.Profiles.Count <= 1) { MessageBox.Show("Нужен хотя бы один профиль.", "Core Launcher"); return; }
        var p = _s.ActiveProfile;
        if (MessageBox.Show($"Удалить профиль «{p.Name}» из лаунчера?\n\nПапка с мирами и модами останется на диске, ты сможешь открыть её вручную.",
                "Core Launcher", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _s.Profiles.Remove(p);
        _s.ActiveProfileId = _s.Profiles[0].Id; _s.Save();
        RefreshProfiles();
    }

    void OpenProfileFolder_Click(object s, RoutedEventArgs e) => OpenFolder(Paths.Instance(_s.ActiveProfile.Id));
}
