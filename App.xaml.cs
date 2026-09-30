using System.Windows;

namespace BPet;

public partial class App : System.Windows.Application
{
    internal static AppServices Services { get; private set; } = null!;

    private void App_Startup(object sender, StartupEventArgs e)
    {
        Services = new AppServices();
        Services.Load();
        var pet = new MainWindow(Services);
        MainWindow = pet;
        pet.Show();
        Services.Tray.Show();
    }

    private void App_Exit(object sender, ExitEventArgs e)
    {
        Services?.Save();
        Services?.Dispose();
    }
}
