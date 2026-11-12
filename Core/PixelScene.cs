using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CoreLauncher.Core;

/// <summary>Пиксельные декорации: костёр и мерцающие звёзды.</summary>
public static class PixelScene
{
    static readonly Dictionary<char, Color> Pal = new()
    {
        ['Y'] = Color.FromRgb(0xFF, 0xE0, 0x8A), ['O'] = Color.FromRgb(0xFF, 0x9F, 0x43),
        ['R'] = Color.FromRgb(0xE8, 0x57, 0x3A), ['L'] = Color.FromRgb(0x8B, 0x5A, 0x2B),
        ['D'] = Color.FromRgb(0x5C, 0x3A, 0x1A), ['G'] = Color.FromRgb(0x6B, 0x72, 0xA0),
        ['g'] = Color.FromRgb(0x4A, 0x52, 0x80),
    };

    static readonly string[] FireA =
    {
        ".......Y........", "......YO........", "......OO..R.....", ".....YOOR.RO....", "....ROOYOOROY...",
        "....ROYYYOROO...", "...ROOYYYYOOR...", "...ROOOYYYOOR...", "....ROOOOOOR....",
        "..LLLDDDDDDLLL..", ".LLLLLLDDLLLLLL.", ".DDLLLLLLLLLLDD.", "..gG..DDDD..Gg..",
    };

    static readonly string[] FireB =
    {
        "........Y.......", "........OY......", "....R...OO......", "....OR.YOOR.....", "...YORROOYOR....",
        "...OOROYYYOR....", "...ROOYYYYOOR...", "...ROOYYYOOOR...", "....ROOOOOOR....",
        "..LLLDDDDDDLLL..", ".LLLLLLDDLLLLLL.", ".DDLLLLLLLLLLDD.", "..gG..DDDD..Gg..",
    };

    // ---- пиксельные значки разделов (11x11), красятся в цвет текста кнопки ----
    public static readonly string[] IcoPlay =
    {
        "...........", "..XX.......", "..XXXX.....", "..XXXXXX...", "..XXXXXXXX.", "..XXXXXXXXX",
        "..XXXXXXXX.", "..XXXXXX...", "..XXXX.....", "..XX.......", "...........",
    };
    public static readonly string[] IcoProfiles =
    {
        "XXXXXXXXXXX", "X.........X", "X.XX.XXXX.X", "X.........X", "XXXXXXXXXXX", "...........",
        "XXXXXXXXXXX", "X.........X", "X.XX.XXXX.X", "X.........X", "XXXXXXXXXXX",
    };
    public static readonly string[] IcoMods =
    {
        "...........", "XXXXXXXXXXX", "X.........X", "X.........X", "XXXXXXXXXXX", "X...XXX...X",
        "X...X.X...X", "X...XXX...X", "X.........X", "X.........X", "XXXXXXXXXXX",
    };
    public static readonly string[] IcoSkins =
    {
        "...........", "XXXXXXXXXXX", "XXXXXXXXXXX", "XXXXXXXXXXX", "X.........X", "X.XX...XX.X",
        "X.XX...XX.X", "X.........X", "X...XXX...X", "X.........X", "XXXXXXXXXXX",
    };
    public static readonly string[] IcoConsole =
    {
        "...........", "XXXXXXXXXXX", "X.........X", "X.X.......X", "X..X......X", "X...X.....X",
        "X..X......X", "X.X..XXXX.X", "X.........X", "XXXXXXXXXXX", "...........",
    };
    public static readonly string[] IcoSettings =
    {
        "....XXX....", ".XX.XXX.XX.", ".XXXXXXXXX.", "..XX...XX..", "XXX.....XXX", "XXX.....XXX",
        "XXX.....XXX", "..XX...XX..", ".XXXXXXXXX.", ".XX.XXX.XX.", "....XXX....",
    };

    /// <summary>Значок из пикселей. Цвет берёт у кнопки, в которой лежит (следит за наведением и выбором).</summary>
    public static FrameworkElement Icon(string[] rows, double px)
    {
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (rows[y][x] == 'X') g.Children.Add(new RectangleGeometry(new Rect(x * px, y * px, px, px)));
        g.Freeze();
        var p = new Path { Data = g, Width = 11 * px, Height = rows.Length * px, SnapsToDevicePixels = true, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetEdgeMode(p, EdgeMode.Aliased);
        p.SetBinding(Shape.FillProperty, new System.Windows.Data.Binding("Foreground")
        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Control), 1) });
        return p;
    }

    static Canvas Frame(string[] rows, int px)
    {
        var c = new Canvas { Width = 16 * px, Height = rows.Length * px };
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < 16 && x < rows[y].Length; x++)
            {
                if (!Pal.TryGetValue(rows[y][x], out var col)) continue;
                var r = new Rectangle { Width = px, Height = px, Fill = new SolidColorBrush(col), SnapsToDevicePixels = true };
                Canvas.SetLeft(r, x * px); Canvas.SetTop(r, y * px);
                c.Children.Add(r);
            }
        return c;
    }

    /// <summary>Анимированный костёр. Возвращает контейнер; анимацию можно выключить через Stop.</summary>
    public static (FrameworkElement view, Action<bool> enable) Campfire(int px)
    {
        var a = Frame(FireA, px); var b = Frame(FireB, px); b.Visibility = Visibility.Collapsed;
        var glow = new Ellipse
        {
            Width = 16 * px, Height = 13 * px,   // свечение целиком внутри рамки, чтобы не обрезалось квадратом
            Fill = new RadialGradientBrush(Color.FromArgb(0x60, 0xFF, 0xA8, 0x40), Color.FromArgb(0, 0xFF, 0xA8, 0x40)),
        };
        var grid = new Grid { Width = 16 * px, Height = 13 * px, SnapsToDevicePixels = true };
        glow.HorizontalAlignment = HorizontalAlignment.Center; glow.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(glow); grid.Children.Add(a); grid.Children.Add(b);

        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        t.Tick += (_, _) =>
        {
            bool first = a.Visibility == Visibility.Visible;
            a.Visibility = first ? Visibility.Collapsed : Visibility.Visible;
            b.Visibility = first ? Visibility.Visible : Visibility.Collapsed;
            glow.Opacity = first ? 0.75 : 1.0;
        };
        t.Start();
        return (grid, on => { if (on) t.Start(); else t.Stop(); });
    }

    /// <summary>Рассыпает мерцающие пиксельные звёзды по холсту.</summary>
    public static void Stars(Canvas canvas, int count, double w, double h, bool animate)
    {
        canvas.Children.Clear();
        var rnd = new Random(7);
        for (int i = 0; i < count; i++)
        {
            int s = rnd.Next(0, 4) == 0 ? 4 : 3;
            // тёплые искры вместо звёзд
            var color = rnd.Next(0, 3) == 0 ? Color.FromRgb(0xFF, 0xE0, 0x8A) : Color.FromRgb(0xFF, 0x9F, 0x43);
            var r = new Rectangle { Width = s, Height = s, Fill = new SolidColorBrush(color), Opacity = 0.25 + rnd.NextDouble() * 0.5 };
            Canvas.SetLeft(r, rnd.NextDouble() * w); Canvas.SetTop(r, rnd.NextDouble() * h);
            canvas.Children.Add(r);
            if (animate)
            {
                var an = new DoubleAnimation(0.05, 0.6, TimeSpan.FromSeconds(1.2 + rnd.NextDouble() * 2.5))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(rnd.NextDouble() * 2) };
                r.BeginAnimation(UIElement.OpacityProperty, an);
            }
        }
    }
}
