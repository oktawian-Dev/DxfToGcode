using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Linq;
using System.Windows;
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
}