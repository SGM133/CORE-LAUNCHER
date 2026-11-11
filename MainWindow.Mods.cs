using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using CoreLauncher.Core;

namespace CoreLauncher;

public partial class MainWindow
{
    void RefreshMods()
    {
        var p = _s.ActiveProfile;
        ModsProfileText.Text = $"Профиль «{p.Name}» · {p.Loader} {p.McVersion}" +
                               (p.Loader != Loader.Fabric ? "   ⚠ Моды работают только с Fabric - смени загрузчик в профиле" : "");
        var mods = ModService.List(p);
        ModList.ItemsSource = mods;
        ModsEmpty.Visibility = mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void ModsTab_Click(object s, RoutedEventArgs e)
    {
        if (InstalledPanel is null || ModrinthPanel is null) return;
        bool inst = TabInstalled.IsChecked == true;
        InstalledPanel.Visibility = inst ? Visibility.Visible : Visibility.Collapsed;
        ModrinthPanel.Visibility = inst ? Visibility.Collapsed : Visibility.Visible;
        if (!inst && HitList.ItemsSource == null) Search_Click(s, e);
    }

    void AddMods_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Моды (*.jar)|*.jar", Multiselect = true };
        if (dlg.ShowDialog() == true) { ModService.Add(_s.ActiveProfile, dlg.FileNames); RefreshMods(); }
    }

    void Mods_Drop(object s, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { ModService.Add(_s.ActiveProfile, files); RefreshMods(); }
    }

    void ModToggle_Click(object s, RoutedEventArgs e)
    {
        var cb = (CheckBox)s;
        try { ModService.SetEnabled((ModItem)cb.DataContext, cb.IsChecked == true); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Core Launcher"); }
        RefreshMods();
    }

    void ModDelete_Click(object s, RoutedEventArgs e)
    {
        var mod = (ModItem)((Button)s).DataContext;
        if (MessageBox.Show($"Удалить мод «{mod.Name}»?", "Core Launcher", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { ModService.Delete(mod); } catch (Exception ex) { MessageBox.Show(ex.Message, "Core Launcher"); }
        RefreshMods();
    }

    void OpenMods_Click(object s, RoutedEventArgs e) => OpenFolder(Paths.InstanceMods(_s.ActiveProfile.Id));

    // ---- Modrinth ----
    void SearchBox_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) Search_Click(s, e); }

    async void Search_Click(object s, RoutedEventArgs e)
    {
        var p = _s.ActiveProfile;
        SearchStatus.Text = "Ищем…";
        try
        {
            var hits = await ModService.SearchAsync(SearchBox.Text.Trim(), p);
            HitList.ItemsSource = hits;
            SearchStatus.Text = hits.Count == 0
                ? "Ничего не найдено для этой версии."
                : $"Показаны моды для Fabric {p.McVersion}" + (string.IsNullOrWhiteSpace(SearchBox.Text) ? " - самые популярные" : "");
        }
        catch (Exception ex) { SearchStatus.Text = "Не удалось выполнить поиск: " + ex.Message; }
    }

    async void Install_Click(object s, RoutedEventArgs e)
    {
        var btn = (Button)s;
        var id = (string)btn.Tag;
        btn.IsEnabled = false; btn.Content = "Ставим…";
        try
        {
            var done = await ModService.InstallAsync(id, _s.ActiveProfile);
            btn.Content = done.Count == 0 ? "Уже стоит" : "Готово ✔";
            SearchStatus.Text = done.Count == 0 ? "Этот мод уже установлен." : "Установлено: " + string.Join(", ", done);
        }
        catch (Exception ex)
        {
            btn.Content = "Установить"; btn.IsEnabled = true;
            SearchStatus.Text = "Не удалось установить: " + ex.Message;
        }
    }
}

