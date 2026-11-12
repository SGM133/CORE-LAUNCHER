using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using CoreLauncher.Core;

namespace CoreLauncher;

public partial class MainWindow
{
    ModSite _modSite = ModSite.Modrinth;
    string _hitsKey = "";

    void RefreshMods()
    {
        var p = _s.ActiveProfile;
        ModsProfileText.Text = $"Профиль «{p.Name}» · {Loaders.Title(p.Loader)} {p.McVersion}" +
                               (p.Loader == Loader.Vanilla ? "   ⚠ В Vanilla моды не работают - выбери загрузчик в профиле" : "");
        var mods = ModService.List(p);
        ModList.ItemsSource = mods;
        ModsEmpty.Visibility = mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void ModsTab_Click(object s, RoutedEventArgs e)
    {
        if (InstalledPanel is null || ModrinthPanel is null || TabCurse is null) return;
        bool inst = TabInstalled.IsChecked == true;
        InstalledPanel.Visibility = inst ? Visibility.Visible : Visibility.Collapsed;
        ModrinthPanel.Visibility = inst ? Visibility.Collapsed : Visibility.Visible;
        if (inst) { RefreshMods(); return; }
        _modSite = TabCurse.IsChecked == true ? ModSite.CurseForge : ModSite.Modrinth;
        var p = _s.ActiveProfile;
        var key = $"{_modSite}|{p.Id}|{p.McVersion}|{p.Loader}";
        if (key != _hitsKey) Search_Click(s, e);
    }

    void AddMods_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Моды (*.jar;*.litemod)|*.jar;*.litemod", Multiselect = true };
        if (dlg.ShowDialog() == true) { int n = ModService.Add(_s.ActiveProfile, dlg.FileNames); Log.Info($"Добавлено модов из файлов: {n}"); RefreshMods(); }
    }

    void Mods_Drop(object s, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { int n = ModService.Add(_s.ActiveProfile, files); Log.Info($"Добавлено модов перетаскиванием: {n}"); RefreshMods(); }
    }

    void ModToggle_Click(object s, RoutedEventArgs e)
    {
        var cb = (CheckBox)s;
        var mod = (ModItem)cb.DataContext;
        try { ModService.SetEnabled(mod, cb.IsChecked == true); Log.Info($"Мод «{mod.Name}» {(cb.IsChecked == true ? "включён" : "выключен")}"); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Core Launcher"); }
        RefreshMods();
    }

    void ModDelete_Click(object s, RoutedEventArgs e)
    {
        var mod = (ModItem)((Button)s).DataContext;
        if (MessageBox.Show($"Удалить мод «{mod.Name}»?", "Core Launcher", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { ModService.Delete(mod); Log.Info($"Мод «{mod.Name}» удалён"); } catch (Exception ex) { MessageBox.Show(ex.Message, "Core Launcher"); }
        RefreshMods();
    }

    void OpenMods_Click(object s, RoutedEventArgs e) => OpenFolder(Paths.InstanceMods(_s.ActiveProfile.Id));

    // ---- каталоги: Modrinth и CurseForge ----
    void SearchBox_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) Search_Click(s, e); }

    async void Search_Click(object s, RoutedEventArgs e)
    {
        var p = _s.ActiveProfile;
        var site = _modSite;
        _hitsKey = $"{site}|{p.Id}|{p.McVersion}|{p.Loader}";
        SearchStatus.Text = $"Ищем на {site}…";
        HitList.ItemsSource = null;
        try
        {
            var hits = await ModService.SearchAsync(site, SearchBox.Text.Trim(), p);
            if (site != _modSite) return;
            HitList.ItemsSource = hits;
            SearchStatus.Text = hits.Count == 0
                ? $"На {site} ничего не найдено для {Loaders.Title(p.Loader)} {p.McVersion}."
                : $"{site}: моды для {Loaders.Title(p.Loader)} {p.McVersion}" + (string.IsNullOrWhiteSpace(SearchBox.Text) ? " - самые популярные" : "") +
                  ". Клик по названию откроет страницу мода.";
        }
        catch (Exception ex) { SearchStatus.Text = $"Не удалось выполнить поиск на {site}: " + ex.Message; }
    }

    async void Install_Click(object s, RoutedEventArgs e)
    {
        var btn = (Button)s;
        var hit = (ModHit)btn.DataContext;
        btn.IsEnabled = false; btn.Content = "Ставим…";
        try
        {
            var done = await ModService.InstallAsync(hit, _s.ActiveProfile);
            btn.Content = done.Count == 0 ? "Уже стоит" : "Готово ✔";
            SearchStatus.Text = done.Count == 0 ? $"«{hit.Title}» уже установлен." : "Установлено: " + string.Join(", ", done);
        }
        catch (Exception ex)
        {
            btn.Content = "Установить"; btn.IsEnabled = true;
            SearchStatus.Text = $"Не удалось установить «{hit.Title}»: " + ex.Message;
        }
    }

    /// <summary>Клик по моду в каталоге - открыть его страницу на выбранном сайте.</summary>
    void HitPage_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is not ModHit hit) return;
        if (!hit.PageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo(hit.PageUrl) { UseShellExecute = true }); Log.Info("Открыта страница мода: " + hit.PageUrl); }
        catch (Exception ex) { SearchStatus.Text = "Не получилось открыть браузер: " + ex.Message; }
    }
}
