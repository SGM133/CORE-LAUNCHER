using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using CoreLauncher.Core;

namespace CoreLauncher;

public class LibItem
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public BitmapSource? Thumb { get; init; }
    public bool Active { get; init; }
}

public partial class MainWindow
{
    bool _back;
    Textures? _lookup;
    string _lookupNick = "";

    void InitSkins()
    {
        SlimCheck.IsChecked = _s.SlimModel;
        if (string.IsNullOrEmpty(_s.ActiveSkin) || !File.Exists(_s.ActiveSkin))
        {
            _s.ActiveSkin = SkinService.Skins().FirstOrDefault();
            _s.Save();
        }
    }

    void RefreshSkinsPage()
    {
        _loading = true;
        (_s.SkinSystem switch { SkinSystem.Official => SysOfficial, SkinSystem.ElyBy => SysEly, SkinSystem.TLauncher => SysTl, _ => SysCss }).IsChecked = true;
        _loading = false;

        bool css = _s.SkinSystem == SkinSystem.Css;
        CssPanel.Visibility = css ? Visibility.Visible : Visibility.Collapsed;
        SitePanel.Visibility = css ? Visibility.Collapsed : Visibility.Visible;
        UploadBtn.Visibility = _s.SkinSystem == SkinSystem.Official ? Visibility.Visible : Visibility.Collapsed;
        SysDesc.Text = SkinService.Describe(_s.SkinSystem);
        if (string.IsNullOrWhiteSpace(LookupBox.Text)) LookupBox.Text = _s.Nickname;

        SkinsFabricHint.Text = _s.ActiveProfile.Loader == Loader.Fabric ? ""
            : "⚠ Чтобы скины и плащи были видны в игре, у профиля должен быть загрузчик Fabric (раздел «Профили»).";

        RefreshTiles();
        UpdatePreview();
    }

    void RefreshTiles()
    {
        var skins = SkinService.Skins();
        var capes = SkinService.Capes();
        if (string.IsNullOrEmpty(_s.ActiveSkin) || !File.Exists(_s.ActiveSkin)) _s.ActiveSkin = skins.FirstOrDefault();
        if (!string.IsNullOrEmpty(_s.ActiveCape) && !File.Exists(_s.ActiveCape)) _s.ActiveCape = null;

        SkinTiles.ItemsSource = skins.Select(p =>
        {
            BitmapSource? thumb = null;
            try { thumb = SkinRender.Head(Px.FromFile(p)); } catch { }
            return new LibItem { Path = p, Name = System.IO.Path.GetFileNameWithoutExtension(p), Thumb = thumb, Active = p == _s.ActiveSkin };
        }).ToList();

        CapeTiles.ItemsSource = capes.Select(p =>
        {
            BitmapSource? thumb = null;
            try { thumb = SkinRender.CapeFace(Px.FromFile(p)); } catch { }
            return new LibItem { Path = p, Name = System.IO.Path.GetFileNameWithoutExtension(p), Thumb = thumb, Active = p == _s.ActiveCape };
        }).ToList();
    }

    void UpdatePreview()
    {
        try
        {
            Px? skin = null, cape = null; bool slim = SlimCheck.IsChecked == true;
            if (_s.SkinSystem != SkinSystem.Css && _lookup != null)
            {
                if (_lookup.Skin != null) skin = Px.FromBytes(_lookup.Skin);
                if (_lookup.Cape != null) cape = Px.FromBytes(_lookup.Cape);
                slim = _lookup.Slim;
            }
            else if (_s.SkinSystem == SkinSystem.Css || _lookup == null)
            {
                if (!string.IsNullOrEmpty(_s.ActiveSkin) && File.Exists(_s.ActiveSkin)) skin = Px.FromFile(_s.ActiveSkin);
                if (!string.IsNullOrEmpty(_s.ActiveCape) && File.Exists(_s.ActiveCape)) cape = Px.FromFile(_s.ActiveCape);
            }
            SkinImg.Source = skin == null ? null : SkinRender.Character(skin, cape, _back, slim);
        }
        catch { SkinImg.Source = null; }
    }

    void View_Click(object s, RoutedEventArgs e) { if (SkinImg is null || ViewBack is null) return; _back = ViewBack.IsChecked == true; UpdatePreview(); }
    void Slim_Click(object s, RoutedEventArgs e) { _s.SlimModel = SlimCheck.IsChecked == true; _s.Save(); UpdatePreview(); }

    void System_Click(object s, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.SkinSystem = Enum.Parse<SkinSystem>((string)((RadioButton)s).Tag);
        _s.Save();
        _lookup = null; LookupStatus.Text = ""; SaveLookupBtn.IsEnabled = false;
        RefreshSkinsPage();
    }

    void LibTile_Click(object s, RoutedEventArgs e)
    {
        var path = (string)((Button)s).Tag;
        if (path.StartsWith(Paths.CssCapes, StringComparison.OrdinalIgnoreCase)) _s.ActiveCape = path;
        else _s.ActiveSkin = path;
        _s.Save();
        RefreshTiles(); UpdatePreview();
    }

    void NoCape_Click(object s, RoutedEventArgs e) { _s.ActiveCape = null; _s.Save(); RefreshTiles(); UpdatePreview(); }

    void ImportSkin_Click(object s, RoutedEventArgs e) => Import(false);
    void ImportCape_Click(object s, RoutedEventArgs e) => Import(true);

    void Import(bool cape)
    {
        var dlg = new OpenFileDialog { Filter = "PNG (*.png)|*.png", Multiselect = true };
        if (dlg.ShowDialog() != true) return;
        foreach (var f in dlg.FileNames)
        {
            try
            {
                var dst = SkinService.Import(f, cape);
                if (cape) _s.ActiveCape = dst; else _s.ActiveSkin = dst;
            }
            catch (Exception ex) { MessageBox.Show($"{System.IO.Path.GetFileName(f)}: {ex.Message}", "Core Launcher"); }
        }
        _s.Save(); RefreshTiles(); UpdatePreview();
    }

    void DeleteSkin_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_s.ActiveSkin) || !File.Exists(_s.ActiveSkin)) return;
        if (MessageBox.Show($"Удалить скин «{System.IO.Path.GetFileNameWithoutExtension(_s.ActiveSkin)}» из библиотеки?", "Core Launcher",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        File.Delete(_s.ActiveSkin); _s.ActiveSkin = null; _s.Save(); RefreshTiles(); UpdatePreview();
    }

    // ---- по нику ----
    void LookupBox_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) Lookup_Click(s, e); }

    async void Lookup_Click(object s, RoutedEventArgs e)
    {
        var nick = LookupBox.Text.Trim();
        if (nick.Length == 0) return;
        LookupStatus.Text = "Ищем…"; SaveLookupBtn.IsEnabled = false;
        try
        {
            var t = await SkinService.LookupAsync(_s.SkinSystem, nick);
            if (t.Skin == null && t.Cape == null) throw new InvalidOperationException("У этого ника нет скина.");
            _lookup = t; _lookupNick = nick;
            _loading = true; SlimCheck.IsChecked = t.Slim; _loading = false;
            LookupStatus.Text = $"Найдено: скин {(t.Skin != null ? "есть" : "нет")}, плащ {(t.Cape != null ? "есть" : "нет")}.";
            SaveLookupBtn.IsEnabled = t.Skin != null || t.Cape != null;
            UpdatePreview();
        }
        catch (Exception ex)
        {
            _lookup = null; UpdatePreview();
            LookupStatus.Text = "Не нашли: " + (ex is System.Net.Http.HttpRequestException ? "ник не найден или сайт недоступен." : ex.Message);
        }
    }

    void SaveLookup_Click(object s, RoutedEventArgs e)
    {
        if (_lookup == null) return;
        var tag = SkinService.Title(_s.SkinSystem);
        if (_lookup.Skin != null) _s.ActiveSkin = SkinService.SaveBytes(_lookup.Skin, $"{_lookupNick} ({tag})", false);
        if (_lookup.Cape != null) _s.ActiveCape = SkinService.SaveBytes(_lookup.Cape, $"{_lookupNick} ({tag})", true);
        _s.SlimModel = _lookup.Slim; _s.Save();
        LookupStatus.Text = "Сохранено в библиотеку C.S.S. - выбери систему C.S.S., чтобы использовать.";
        RefreshTiles();
    }

    async void Upload_Click(object s, RoutedEventArgs e)
    {
        if (_msSession == null) { LookupStatus.Text = "Сначала войди через Microsoft на вкладке «Играть»."; return; }
        if (string.IsNullOrEmpty(_s.ActiveSkin) || !File.Exists(_s.ActiveSkin)) { LookupStatus.Text = "В библиотеке C.S.S. не выбран скин."; return; }
        try
        {
            LookupStatus.Text = "Загружаем скин в аккаунт…";
            await SkinService.UploadOfficialAsync(_msSession, _s.ActiveSkin, SlimCheck.IsChecked == true);
            LookupStatus.Text = "Готово! Скин загружен в твой аккаунт Minecraft.";
        }
        catch (Exception ex) { LookupStatus.Text = "Не получилось: " + ex.Message; }
    }
}

