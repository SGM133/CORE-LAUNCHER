using System;
using System.Threading.Tasks;
using System.Windows;
using CoreLauncher.Core;

namespace CoreLauncher;

public partial class MainWindow
{
    UpdateInfo? _update;
    bool _updating;

    void InitUpdates()
    {
        UpdateService.Cleanup();
        VersionText.Text = $"Public Demo · v{UpdateService.CurrentText}";
        AboutTitle.Text = $"Core Launcher · Public Demo {UpdateService.CurrentText}";
        _loading = true;
        AutoUpdCheck.IsChecked = _s.AutoCheckUpdates;
        UpdSourceBox.Text = _s.UpdateSource;
        _loading = false;
        AutoUpdCheck.Click += (_, _) => { _s.AutoCheckUpdates = AutoUpdCheck.IsChecked == true; _s.Save(); };
        UpdSourceBox.LostFocus += (_, _) => { _s.UpdateSource = UpdSourceBox.Text.Trim(); _s.Save(); };

        // пока лаунчер открыт, заглядываем за обновлениями раз в 4 часа
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(4) };
        t.Tick += async (_, _) =>
        {
            if (!_s.AutoCheckUpdates || _update != null || _updating || string.IsNullOrWhiteSpace(_s.UpdateSource)) return;
            try { await CheckUpdateAsync(silent: true); } catch { }
        };
        t.Start();
    }

    /// <summary>Тихая проверка при запуске: ошибки не показываем, чтобы не мешать.</summary>
    async Task AutoCheckUpdateAsync()
    {
        if (!_s.AutoCheckUpdates || string.IsNullOrWhiteSpace(_s.UpdateSource)) return;
        await Task.Delay(2500);
        try { await CheckUpdateAsync(silent: true); } catch { }
    }

    async Task CheckUpdateAsync(bool silent)
    {
        if (!silent) UpdStatus.Text = "Проверяем…";
        _update = await UpdateService.CheckAsync(_s.UpdateSource);
        if (_update == null)
        {
            UpdateBtn.Visibility = Visibility.Collapsed;
            if (!silent) UpdStatus.Text = $"У тебя последняя версия ({UpdateService.CurrentText}).";
            return;
        }
        UpdateBtn.Content = $"⬆ Обновить до {_update.Version.Major}.{_update.Version.Minor}.{Math.Max(0, _update.Version.Build)}";
        UpdateBtn.ToolTip = string.IsNullOrWhiteSpace(_update.Notes) ? null : _update.Notes;
        UpdateBtn.Visibility = Visibility.Visible;
        UpdStatus.Text = $"Доступна версия {_update.Version}. Кнопка обновления - в левом нижнем углу.";
        if (silent) StatusText.Text = $"Доступно обновление лаунчера: {_update.Version}";
    }

    async void CheckNow_Click(object s, RoutedEventArgs e)
    {
        _s.UpdateSource = UpdSourceBox.Text.Trim(); _s.Save();
        if (string.IsNullOrWhiteSpace(_s.UpdateSource)) { UpdStatus.Text = "Укажи источник обновлений."; return; }
        try { await CheckUpdateAsync(silent: false); }
        catch (Exception ex) { UpdStatus.Text = "Не получилось проверить: " + ex.Message; }
    }

    async void Update_Click(object s, RoutedEventArgs e)
    {
        if (_update == null || _updating) return;
        if (_busy) { StatusText.Text = "Дождись окончания запуска игры, потом обновляйся."; return; }
        _updating = true; UpdateBtn.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(p => { UpdateBtn.Content = $"Скачиваем… {p:0}%"; Bar.Value = p; });
            var file = await UpdateService.DownloadAsync(_update, progress);
            UpdateBtn.Content = "Перезапуск…";
            _s.Save();
            UpdateService.ApplyAndRestart(file);
            _noSave = true;
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _updating = false; UpdateBtn.IsEnabled = true; Bar.Value = 0;
            UpdateBtn.Content = $"⬆ Обновить до {_update.Version}";
            StatusText.Text = "Обновление не удалось: " + ex.Message;
        }
    }
}

