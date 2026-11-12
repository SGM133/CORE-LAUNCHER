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
    // для скинов из интернета
    public byte[]? Png { get; init; }
    public bool Slim { get; init; }
}

public partial class MainWindow
{
    Textures? _lookup;
    string _lookupNick = "";

    void InitSkins()
    {
        SlimCheck.IsChecked = _s.SlimModel;
        if (string.IsNullOrEmpty(_s.ActiveSkin) || !File.Exists(_s.ActiveSkin))
        {
            _s.ActiveSkin = SkinService.DefaultSkinPath();
            AutoSlim(_s.ActiveSkin);
            _s.Save();
        }
    }

    /// <summary>Подбирает модель рук (обычные/тонкие) под сам скин.</summary>
    void AutoSlim(string? path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            _s.SlimModel = SkinService.IsSlim(Px.FromFile(path));
            SlimCheck.IsChecked = _s.SlimModel;
        }
        catch { }
    }

    // ---- развернуть / свернуть список ----
    const int CollapsedSkins = 6;   // табличка 3 x 2
    bool _skinsExpanded;

    void SkinsToggle_Click(object s, RoutedEventArgs e) { _skinsExpanded = !_skinsExpanded; RefreshTiles(); }

    // ---- скачать из интернета ----
    LibItem? _onlinePick;
    int _onlineSite, _onlineRun;

    void OnlineOpen_Click(object s, RoutedEventArgs e)
    {
        OnlinePanel.Visibility = Visibility.Visible;
        OnlinePanel.BringIntoView();
        OnlineBox.Focus();
        if (OnlineTiles.Items.Count == 0) OnlineSearch_Click(s, e);
    }

    void OnlineClose_Click(object s, RoutedEventArgs e)
    {
        OnlinePanel.Visibility = Visibility.Collapsed;
        _onlinePick = null; UpdatePreview();
    }

    void OnlineSite_Click(object s, RoutedEventArgs e)
    {
        if (OnlineTiles is null) return;
        _onlineSite = int.Parse((string)((RadioButton)s).Tag);
        OnlineSearch_Click(s, e);
    }

    void OnlineBox_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) OnlineSearch_Click(s, e); }

    async void OnlineSearch_Click(object s, RoutedEventArgs e)
    {
        int run = ++_onlineRun, site = _onlineSite;
        var q = OnlineBox.Text.Trim();
        var siteName = SkinCatalog.Sites[site];
        OnlineStatus.Text = $"Ищем на {siteName}…";
        OnlineTiles.ItemsSource = null; _onlinePick = null; UpdatePreview();
        try
        {
            var hits = await SkinCatalog.SearchAsync(site, q);
            if (run != _onlineRun) return;
            if (hits.Count == 0) { OnlineStatus.Text = $"На {siteName} ничего не нашлось. Попробуй другое слово (лучше по-английски) или другой сайт."; return; }
            OnlineStatus.Text = $"Нашли {hits.Count}, загружаем картинки…";

            // сами файлы скинов маленькие - качаем сразу и рисуем превью
            using var gate = new System.Threading.SemaphoreSlim(6);
            var items = await System.Threading.Tasks.Task.WhenAll(hits.Select(async h =>
            {
                await gate.WaitAsync();
                try
                {
                    var png = await Net.GetBytesAsync(h.SkinUrl);
                    if (png.Length > 512 * 1024) return null;
                    var px = Px.FromBytes(png);
                    if (!SkinService.IsSkinSize(px)) return null;
                    return new LibItem { Name = h.Name, Png = png, Slim = SkinService.IsSlim(px), Thumb = SkinRender.Head(px) };
                }
                catch { return null; }
                finally { gate.Release(); }
            }));
            if (run != _onlineRun) return;
            var ok = items.Where(i => i != null).Select(i => i!).ToList();
            OnlineTiles.ItemsSource = ok;
            OnlineStatus.Text = ok.Count == 0
                ? $"Скины с {siteName} не загрузились. Попробуй позже или другой сайт."
                : $"{siteName}: {ok.Count} шт. Клик по скину - примерить, «Скачать» - сохранить в библиотеку.";
        }
        catch (Exception ex)
        {
            if (run != _onlineRun) return;
            OnlineStatus.Text = $"{siteName} не отвечает: " + (ex is System.Net.Http.HttpRequestException or System.Threading.Tasks.TaskCanceledException
                ? "проверь интернет или выбери другой сайт." : ex.Message);
        }
    }

    void OnlineTile_Click(object s, RoutedEventArgs e)
    {
        _onlinePick = (LibItem)((Button)s).DataContext;
        UpdatePreview();
    }

    void OnlineSave_Click(object s, RoutedEventArgs e)
    {
        var it = (LibItem)((Button)s).DataContext;
        if (it.Png == null) return;
        try
        {
            _s.ActiveSkin = SkinService.SaveBytes(it.Png, string.IsNullOrWhiteSpace(it.Name) ? "Скин" : it.Name, false);
            _s.SlimModel = it.Slim; SlimCheck.IsChecked = it.Slim;
            _s.Save();
            _onlinePick = null; _skinsExpanded = true;   // чтобы новый скин было видно в списке
            RefreshTiles(); UpdatePreview();
            OnlineStatus.Text = $"«{it.Name}» сохранён в библиотеку и выбран.";
        }
        catch (Exception ex) { OnlineStatus.Text = "Не получилось сохранить: " + ex.Message; }
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
        if (string.IsNullOrEmpty(_s.ActiveSkin) || !File.Exists(_s.ActiveSkin)) { _s.ActiveSkin = SkinService.DefaultSkinPath(); AutoSlim(_s.ActiveSkin); }
        if (!string.IsNullOrEmpty(_s.ActiveCape) && !File.Exists(_s.ActiveCape)) _s.ActiveCape = null;

        // больше шести - показываем табличку 3x2 и кнопку "Развернуть"
        bool many = skins.Count > CollapsedSkins;
        SkinsToggleBtn.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
        SkinsToggleBtn.Content = _skinsExpanded ? "▲ Свернуть" : $"▼ Развернуть (ещё {skins.Count - CollapsedSkins})";
        var shown = many && !_skinsExpanded ? skins.Take(CollapsedSkins).ToList() : skins;

        SkinTiles.ItemsSource = shown.Select(p =>
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
            if (_s.SkinSystem == SkinSystem.Css && _onlinePick?.Png != null)
            {
                // примерка скина из интернета (плащ - свой)
                skin = Px.FromBytes(_onlinePick.Png); slim = _onlinePick.Slim;
                if (!string.IsNullOrEmpty(_s.ActiveCape) && File.Exists(_s.ActiveCape)) cape = Px.FromFile(_s.ActiveCape);
            }
            else if (_s.SkinSystem != SkinSystem.Css && _lookup != null)
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
            SkinModel.Content = skin == null ? null : Skin3D.Build(skin, cape, slim);
        }
        catch (Exception ex) { SkinModel.Content = null; Log.Error("Не получилось показать скин", ex); }
    }

    // ---- объёмный просмотр: тянем мышью - крутим, колесо - ближе/дальше ----
    Point _dragFrom;
    bool _dragging;

    void Skin3D_Down(object s, MouseButtonEventArgs e)
    {
        _dragging = true; _dragFrom = e.GetPosition(SkinArea);
        SkinArea.CaptureMouse();
    }

    void Skin3D_Up(object s, MouseButtonEventArgs e) { _dragging = false; SkinArea.ReleaseMouseCapture(); }

    void Skin3D_Move(object s, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(SkinArea);
        SkinYaw.Angle = (SkinYaw.Angle + (p.X - _dragFrom.X) * 0.7) % 360;
        SkinPitch.Angle = Math.Clamp(SkinPitch.Angle + (p.Y - _dragFrom.Y) * 0.5, -70, 70);
        _dragFrom = p;
    }

    void Skin3D_Wheel(object s, MouseWheelEventArgs e)
    {
        var z = Math.Clamp(SkinCam.Position.Z - e.Delta / 120.0 * 3, 22, 90);
        SkinCam.Position = new System.Windows.Media.Media3D.Point3D(0, 0, z);
        e.Handled = true;
    }

    void SkinReset_Click(object s, RoutedEventArgs e)
    {
        SkinYaw.Angle = -25; SkinPitch.Angle = 8;
        SkinCam.Position = new System.Windows.Media.Media3D.Point3D(0, 0, 42);
    }
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
        else { _s.ActiveSkin = path; _onlinePick = null; AutoSlim(path); }
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
                if (cape) _s.ActiveCape = dst; else { _s.ActiveSkin = dst; AutoSlim(dst); _skinsExpanded = true; }
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

