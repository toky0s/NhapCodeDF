using Microsoft.UI.Xaml;

using NhapCodeDF.ViewModels;

using Windows.Graphics;

namespace NhapCodeDF;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1000, 680));
        Closed += (_, _) => ViewModel.Shutdown();
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
        => await ViewModel.InitializeAsync();
}
