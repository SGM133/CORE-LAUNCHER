using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace CoreLauncher;

/// <summary>Окно для чтения встроенных документов (соглашение, конфиденциальность, права).</summary>
public partial class DocWindow : Window
{
    public DocWindow(string file)
    {
        InitializeComponent();
        try
        {
            var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/docs/{file}.txt"));
            using var r = new StreamReader(info.Stream);
            Body.Text = r.ReadToEnd();
        }
        catch (Exception ex) { Body.Text = "Не получилось открыть документ: " + ex.Message; }
        Note.Text = "Текст можно выделить и скопировать";
    }

    void Drag(object s, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    void Close_Click(object s, RoutedEventArgs e) => Close();
}
