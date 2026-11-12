using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CoreLauncher.Core;

namespace CoreLauncher;

/// <summary>Заставка при запуске: логотип, костёр и пиксельная полоса загрузки.</summary>
public partial class SplashWindow : Window
{
    const int BlockCount = 24;
    readonly Rectangle[] _blocks = new Rectangle[BlockCount];

    public SplashWindow()
    {
        InitializeComponent();
        for (int i = 0; i < BlockCount; i++)
        {
            var r = new Rectangle { Margin = new Thickness(1, 0, 1, 0), Fill = (Brush)FindResource("Line"), Opacity = 0.35 };
            _blocks[i] = r; Blocks.Children.Add(r);
        }
        var (fire, _) = PixelScene.Campfire(5);
        FireHost.Content = fire;
        Ver.Text = "v" + UpdateService.CurrentText;
    }

    /// <summary>Подпись шага и заполнение полосы (0..1). Блоки загораются по одному.</summary>
    public void SetProgress(double fraction, string step)
    {
        Step.Text = step;
        int lit = (int)Math.Round(Math.Clamp(fraction, 0, 1) * BlockCount);
        for (int i = 0; i < BlockCount; i++)
        {
            bool on = i < lit;
            _blocks[i].Fill = on ? (Brush)FindResource("Accent") : (Brush)FindResource("Line");
            _blocks[i].Opacity = on ? 1 : 0.35;
        }
    }

    /// <summary>Плавно гасит заставку и закрывает её.</summary>
    public void FadeOutAndClose()
    {
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
