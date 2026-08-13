using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using DepthVideo.App.Localization;
using DepthVideo.App.ViewModels;
using Microsoft.Win32;

namespace DepthVideo.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    private async void SelectVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Text("SelectMediaDialog"),
            Filter = LocalizationService.Text("MediaFilesFilter"),
        };
        if (dialog.ShowDialog(this) == true) await _viewModel.LoadFileAsync(dialog.FileName);
    }

    private void SelectOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Text("SelectOutputDialog"),
            Filter = LocalizationService.Text(_viewModel.IsImage ? "ImageOutputFilter" : "Mp4Filter"),
            DefaultExt = _viewModel.IsImage ? ".png" : ".mp4",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(_viewModel.OutputPath)
                ? LocalizationService.Text(_viewModel.IsImage ? "DefaultImageOutputName" : "DefaultOutputName")
                : System.IO.Path.GetFileName(_viewModel.OutputPath),
        };
        if (dialog.ShowDialog(this) == true) _viewModel.OutputPath = dialog.FileName;
    }

    private async void Start_Click(object sender, RoutedEventArgs e) => await _viewModel.StartAsync();
    private void Stop_Click(object sender, RoutedEventArgs e) => _viewModel.Cancel();
    private void OpenFolder_Click(object sender, RoutedEventArgs e) => _viewModel.OpenOutputFolder();

    private void AuthorLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            await _viewModel.LoadFileAsync(files[0]);
        }
    }
}
