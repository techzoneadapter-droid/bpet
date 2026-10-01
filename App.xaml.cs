using System.Windows;

namespace BPet;

public partial class App : System.Windows.Application
{
    internal static AppServices Services { get; private set; } = null!;
    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _showEvent;
    private static bool _ownsInstance;

    private void App_Startup(object sender, StartupEventArgs e)
    {
        if (!TryOwnInstance())
        {
            try { EventWaitHandle.OpenExisting(@"Local\BPet.Show").Set(); } catch { /* The first window is still starting. */ }
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\BPet.Show");
        ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) =>
        {
            Current?.Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is MainWindow pet)
                {
                    pet.Show();
                    pet.Activate();
                }
            });
        }, null, Timeout.Infinite, false);

        try
        {
            Services = new AppServices();
            Services.Load();
            try { BrowserIntegration.Register(); } catch { /* Browser integration remains optional. */ }
            var pet = new MainWindow(Services);
            MainWindow = pet;
            pet.Show();
            Services.Tray.Show();
        }
        catch (Exception error)
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BPet", "logs");
            Directory.CreateDirectory(folder);
            var logPath = Path.Combine(folder, "startup-error.log");
            File.WriteAllText(logPath, error.ToString());
            var reason = error.GetBaseException().Message;
            System.Windows.MessageBox.Show(
                "BPet không khởi động được: " + reason + Environment.NewLine + Environment.NewLine + "Đã lưu chi tiết lỗi tại:" + Environment.NewLine + logPath,
                "BPet",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static bool TryOwnInstance()
    {
        _instanceMutex = new Mutex(false, @"Local\BPet.SingleInstance");
        try
        {
            _ownsInstance = _instanceMutex.WaitOne(0);
            return _ownsInstance;
        }
        catch (AbandonedMutexException)
        {
            _ownsInstance = true;
            return true;
        }
    }

    private void App_Exit(object sender, ExitEventArgs e)
    {
        try { Services?.Save(); } catch { /* A bad saved coordinate must not block shutdown. */ }
        try { Services?.Dispose(); } catch { }
        if (_ownsInstance)
        {
            try { _instanceMutex?.ReleaseMutex(); } catch { }
        }
    }
}

