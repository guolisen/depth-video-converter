using System.Windows;
using DepthVideo.App.Localization;

namespace DepthVideo.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LocalizationService.Initialize();
        new MainWindow().Show();
    }
}
