using System.Windows;

namespace BPet;

public partial class UpdateWindow : Window
{
    private readonly UpdateService _updates = new();
    private UpdateInfo? _available;
    public UpdateWindow() { InitializeComponent(); Status.Text = $"Phiên bản hiện tại: {_updates.CurrentVersion}"; }
    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_available is null)
            {
                UpdateButton.IsEnabled = false; Status.Text = "Đang kiểm tra phiên bản mới…";
                _available = await _updates.CheckAsync(CancellationToken.None);
                if (_available is null) { Status.Text = "Bạn đang dùng phiên bản mới nhất."; return; }
                Status.Text = $"Có BPet {_available.Version}. Bấm “Cài đặt bản mới” để tải và cập nhật."; UpdateButton.Content = "Cài đặt bản mới"; UpdateButton.IsEnabled = true; return;
            }
            if (System.Windows.MessageBox.Show(this, "BPet sẽ tự đóng, rồi trình cài đặt mở lên. Bạn không cần tải file tay. Tiếp tục?", "Xác nhận cập nhật", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            UpdateButton.IsEnabled = false; Progress.Visibility = Visibility.Visible; Status.Text = "Đang tải bản cập nhật…";
            var reporter = new Progress<int>(value => { Progress.Value = value; Status.Text = $"Đang tải bản cập nhật… {value}%"; });
            await _updates.DownloadAndLaunchAsync(_available, reporter, CancellationToken.None);
            Status.Text = "Đang đóng BPet để cài bản mới…";
            await Task.Delay(700);
            if (Application.Current.MainWindow is MainWindow pet) pet.RequestExit();
            Application.Current.Shutdown();
        }
        catch (Exception error) { Status.Text = $"Không thể cập nhật: {error.Message}"; UpdateButton.IsEnabled = true; }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
