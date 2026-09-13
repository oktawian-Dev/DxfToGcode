using System.IO;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DxfToGcode.Services;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using Microsoft.Win32;

namespace DxfToGcode;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void SelectDxfButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Wybierz plik DXF",
            Filter = "Pliki DXF (*.dxf)|*.dxf",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            DxfPathTextBox.Text = dialog.FileName;

            var dxfFile = DxfFile.Load(dialog.FileName);

            var splines = dxfFile.Entities
                .OfType<DxfSpline>()
                .ToList();

            var colors = splines
                .GroupBy(spline => spline.Color.RawValue)
                .Select(group => $"{group.Count()} × kolor DXF {group.Key}");

            DxfInfoTextBlock.Text =
                $"Wczytano: {splines.Count} krzywych SPLINE.\n" +
                $"Kolejność kolorów: {string.Join(", ", colors)}.";
        }
        catch (Exception exception)
        {
            DxfInfoTextBlock.Text = "Nie udało się odczytać pliku DXF.";

            MessageBox.Show(
                exception.Message,
                "Błąd odczytu DXF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void GenerateGCodeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(DxfPathTextBox.Text))
            {
                throw new InvalidOperationException(
                    "Najpierw wybierz istniejący plik DXF.");
            }

            var copies = new List<CopyConfiguration>();

            AddCopy(copies, "G54", G54CheckBox, G54MirrorCheckBox);
            AddCopy(copies, "G55", G55CheckBox, G55MirrorCheckBox);
            AddCopy(copies, "G56", G56CheckBox, G56MirrorCheckBox);
            AddCopy(copies, "G57", G57CheckBox, G57MirrorCheckBox);
            AddCopy(copies, "G58", G58CheckBox, G58MirrorCheckBox);
            AddCopy(copies, "G59", G59CheckBox, G59MirrorCheckBox);

            if (copies.Count == 0)
            {
                throw new InvalidOperationException(
                    "Zaznacz przynajmniej jeden układ G54–G59.");
            }

            var generator = new GCodeGenerator();

            GCodePreviewTextBox.Text = generator.Generate(
                DxfPathTextBox.Text,
                ReadColor(ColdGlueColorTextBox, "kleju zimnego"),
                ReadColor(HotGlueColorTextBox, "kleju ciepłego"),
                ReadColor(FrameColorTextBox, "ramki"),
                copies);

            GCodePreviewTextBox.ScrollToHome();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Nie udało się wygenerować G-code",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static short ReadColor(TextBox textBox, string description)
    {
        if (short.TryParse(
                textBox.Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var color))
        {
            return color;
        }

        throw new InvalidOperationException(
            $"Kolor {description} musi być liczbą.");
    }

    private static void AddCopy(
        List<CopyConfiguration> copies,
        string workOffset,
        CheckBox copyCheckBox,
        CheckBox mirrorCheckBox)
    {
        if (copyCheckBox.IsChecked == true)
        {
            copies.Add(new CopyConfiguration(
                workOffset,
                mirrorCheckBox.IsChecked == true));
        }
    }
}