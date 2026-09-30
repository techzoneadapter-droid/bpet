using System.Windows;

namespace BPet;

public partial class App : System.Windows.Application
{
    internal static AppServices Services { get; private set; } = null!;

    private void App_Startup(object sender, StartupEventArgs e)
    {
        try
        {
            Services = new AppServices();
            Services.Load();
            var pet = new MainWindow(Services);
            MainWindow = pet;
            pet.Show();
            Services.Tray.Show();
        }
        catch (Exception error)
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BPet", "logs");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "startup-error.log"), error.ToString());
            System.Windows.MessageBox.Show("BPet không khởi động được. Đã lưu chi tiết lỗi tại: %LocalAppData%\\BPet\\logs\\startup-error.log", "BPet", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void App_Exit(object sender, ExitEventArgs e)
    {
        Services?.Save();
        Services?.Dispose();
    }
}
