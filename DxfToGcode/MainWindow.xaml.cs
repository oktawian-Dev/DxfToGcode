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
using Microsoft.Win32;
using System.Windows;

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

        if (dialog.ShowDialog() == true)
        {
            DxfPathTextBox.Text = dialog.FileName;
        }
    }
}