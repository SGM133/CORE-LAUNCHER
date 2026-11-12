using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using CoreLauncher.Core;

namespace CoreLauncher;

public partial class MainWindow
{
    int _ramMax = 16384;
    Loader _editLoader = Loader.Fabric;

    void InitProfiles()
    {
        long totalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024;
        _ramMax = (int)Math.Clamp(totalMb * 3 / 4 / 512 * 512, 2048, 16384);
        PRam.Maximum = _ramMax;
        SnapshotsCheck.IsChecked = _s.ShowSnapshots;
        OldVersionsCheck.IsChecked = _s.ShowOldVersions;

        // список загрузчиков во всплывающем окне: выбранный подсвечивается
        foreach (var info in Loaders.All)
        {
            var text = new StackPanel { Orientation = Orientation.Horizontal };
            text.Children.Add(new TextBlock { Text = info.Title, FontSize = 14, Width = 170 });
            text.Children.Add(new TextBlock { Text = info.Hint, FontSize = 12, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center });
            var rb = new RadioButton
            {
                Style = (Style)FindResource("SegButton"), GroupName = "LoaderPick", Tag = info.Id, Content = text,
                HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 1),
            };
            System.Windows.Automation.AutomationProperties.SetName(rb, "Загрузчик " + info.Title);
            rb.Click += (_, _) => { SetEditLoader(info.Id); LoaderPopup.IsOpen = false; UpdateLoaderHint(); };
            LoaderOptions.Children.Add(rb);
        }
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
        ProfileInfo.Text = $"{Loaders.Title(p.Loader)} · Minecraft {(string.IsNullOrEmpty(p.McVersion) ? "—" : p.McVersion)}" +
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
        SetEditLoader(p.Loader);
        NoteBox.Text = ReadNote(p);
        PRamOwn.IsChecked = p.RamMb > 0;
        PRam.Value = Math.Clamp(p.RamMb > 0 ? p.RamMb : GameService.RecommendedRamMb(), 1024, _ramMax);
        PRam.IsEnabled = p.RamMb > 0;
        PRamLabel.Text = p.RamMb > 0 ? $"{PRam.Value / 1024.0:0.#} ГБ" : "из настроек";
        _loading = false;
        UpdateLoaderHint();
    }

    void SetEditLoader(Loader l)
    {
        _editLoader = l;
        LoaderBtn.Content = $"Выбрать загрузчик:  {Loaders.Title(l)}  ▾";
        foreach (RadioButton rb in LoaderOptions.Children) rb.IsChecked = (Loader)rb.Tag == l;
    }

    void LoaderBtn_Click(object s, RoutedEventArgs e) => LoaderPopup.IsOpen = !LoaderPopup.IsOpen;

    void UpdateLoaderHint()
    {
        if (LoaderHint is null || PVersion is null) return;
        var v = PVersion.SelectedItem as string ?? "";
        var warn = Loaders.Check(_editLoader, v);
        LoaderHint.Text = warn != null ? "⚠ " + warn
            : _editLoader == Loader.Vanilla ? "Чистая игра. Для модов выбери загрузчик."
            : $"{Loaders.Title(_editLoader)} лаунчер поставит сам при первом запуске." +
              (Loaders.SupportsSkins(_editLoader) ? " Скины и плащи будут работать." : " Скины лаунчера с этим загрузчиком не работают.");
        LoaderHint.Foreground = warn != null ? (System.Windows.Media.Brush)FindResource("Danger") : (System.Windows.Media.Brush)FindResource("Muted");
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

    // снапшоты и старые беты/альфы - два независимых фильтра
    async void Snapshots_Click(object s, RoutedEventArgs e)
    {
        _s.ShowSnapshots = SnapshotsCheck.IsChecked == true;
        _s.ShowOldVersions = OldVersionsCheck.IsChecked == true;
        _s.Save();
        _versions = await _game.GetVersionsAsync(_s.ShowSnapshots, _s.ShowOldVersions);
        LoadEditor();
    }

    // ---- заметка: один note.txt в папке профиля ----
    static string NotePath(Profile p) => Path.Combine(Paths.Instance(p.Id), "note.txt");

    static string ReadNote(Profile p)
    {
        try { return File.Exists(NotePath(p)) ? File.ReadAllText(NotePath(p)) : ""; } catch { return ""; }
    }

    void SaveNote(Profile p)
    {
        try
        {
            var text = NoteBox.Text;
            if (text == ReadNote(p)) return;
            Directory.CreateDirectory(Paths.Instance(p.Id));
            if (string.IsNullOrWhiteSpace(text)) { if (File.Exists(NotePath(p))) File.Delete(NotePath(p)); }
            else File.WriteAllText(NotePath(p), text);
            Log.Info($"Заметка профиля «{p.Name}» сохранена");
        }
        catch (Exception ex) { ProfileStatus.Text = "Заметка не сохранилась: " + ex.Message; }
    }

    void NoteBox_LostFocus(object s, RoutedEventArgs e) { if (!_loading) SaveNote(_s.ActiveProfile); }

    // ---- экспорт и импорт профиля одним zip ----
    const string ProfileManifest = "core-profile.json";
    static readonly string[] SkipDirs = { "logs", "crash-reports", ".fabric", ".mixin.out" };

    async void ExportProfile_Click(object s, RoutedEventArgs e)
    {
        var p = _s.ActiveProfile;
        SaveNote(p);
        var safe = string.Concat(p.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dlg = new SaveFileDialog { Filter = "Профиль Core Launcher (*.zip)|*.zip", FileName = safe + ".zip" };
        if (dlg.ShowDialog() != true) return;
        ProfileStatus.Text = $"Упаковываем профиль «{p.Name}»…";
        try
        {
            var root = Paths.Instance(p.Id);
            Directory.CreateDirectory(root);
            var manifest = JsonSerializer.Serialize(new { format = 1, name = p.Name, mcVersion = p.McVersion, loader = p.Loader.ToString(), ramMb = p.RamMb });
            int count = await Task.Run(() =>
            {
                if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                using var zip = ZipFile.Open(dlg.FileName, ZipArchiveMode.Create);
                using (var w = new StreamWriter(zip.CreateEntry(ProfileManifest).Open())) w.Write(manifest);
                int n = 0;
                foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/');
                    if (SkipDirs.Any(d => rel.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase))) continue;
                    try { zip.CreateEntryFromFile(f, "instance/" + rel, CompressionLevel.Fastest); n++; } catch (IOException) { }   // занятый файл пропускаем
                }
                return n;
            });
            ProfileStatus.Text = $"Профиль «{p.Name}» сохранён в {Path.GetFileName(dlg.FileName)} (файлов: {count}).";
        }
        catch (Exception ex) { ProfileStatus.Text = "Экспорт не удался: " + ex.Message; }
    }

    async void ImportProfile_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Профиль Core Launcher (*.zip)|*.zip" };
        if (dlg.ShowDialog() != true) return;
        ProfileStatus.Text = "Распаковываем профиль…";
        try
        {
            var p = new Profile();
            await Task.Run(() =>
            {
                using var zip = ZipFile.OpenRead(dlg.FileName);
                var me = zip.GetEntry(ProfileManifest) ?? throw new InvalidOperationException("это не профиль Core Launcher (нет core-profile.json).");
                using (var r = new StreamReader(me.Open()))
                {
                    var m = JsonDocument.Parse(r.ReadToEnd()).RootElement;
                    p.Name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "Импорт" : "Импорт";
                    p.McVersion = m.TryGetProperty("mcVersion", out var v) ? v.GetString() ?? "" : "";
                    p.Loader = m.TryGetProperty("loader", out var l) && Enum.TryParse<Loader>(l.GetString(), out var ld) ? ld : Loader.Vanilla;
                    p.RamMb = m.TryGetProperty("ramMb", out var rm) && rm.TryGetInt32(out var mb) ? Math.Clamp(mb, 0, 65536) : 0;
                }
                var root = Path.GetFullPath(Paths.Instance(p.Id)) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(root);
                foreach (var en in zip.Entries)
                {
                    if (!en.FullName.StartsWith("instance/") || en.FullName.EndsWith("/")) continue;
                    var dst = Path.GetFullPath(Path.Combine(root, en.FullName["instance/".Length..]));
                    if (!dst.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // путь наружу папки профиля - пропускаем
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    en.ExtractToFile(dst, true);
                }
            });
            if (_s.Profiles.Any(x => x.Name == p.Name)) p.Name += " (импорт)";
            _s.Profiles.Add(p); _s.ActiveProfileId = p.Id; _s.Save();
            RefreshProfiles();
            ProfileStatus.Text = $"Профиль «{p.Name}» импортирован.";
        }
        catch (Exception ex) { ProfileStatus.Text = "Импорт не удался: " + ex.Message; }
    }

    void SaveProfile_Click(object s, RoutedEventArgs e)
    {
        var p = _s.ActiveProfile;
        if (!string.IsNullOrWhiteSpace(PName.Text)) p.Name = PName.Text.Trim();
        if (PVersion.SelectedItem is string v) p.McVersion = v;
        p.Loader = _editLoader;
        SaveNote(p);
        p.RamMb = PRamOwn.IsChecked == true ? (int)PRam.Value : 0;
        _s.Save();
        RefreshProfiles();
        ProfileStatus.Text = $"Профиль «{p.Name}» сохранён: Minecraft {p.McVersion}, {Loaders.Title(p.Loader)}";
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
