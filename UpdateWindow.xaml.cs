using System.Windows;

namespace BPet;

public partial class UpdateWindow : Window
{
    private readonly UpdateService _updates = new();
    private static bool _updateInProgress;
    private bool _busy;
    private bool _installing;
    private readonly CancellationTokenSource _cancel = new();

    public UpdateWindow()
    {
        InitializeComponent();
        Status.Text = $"Phiên bản hiện tại: {_updates.CurrentVersion}. Bấm cập nhật để tự tải, cài và mở lại BPet.";
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updateInProgress) { Status.Text = "BPet đang cập nhật ở cửa sổ khác."; return; }
        _updateInProgress = true;
        _busy = true;
        UpdateButton.IsEnabled = false;
        try
        {
            Status.Text = "Đang kiểm tra phiên bản mới…";
            var available = await _updates.CheckAsync(_cancel.Token);
            if (available is null) { Status.Text = "Bạn đang dùng phiên bản mới nhất."; return; }
            Progress.Visibility = Visibility.Visible;
            Progress.Value = 0;
            Status.Text = $"Đang tải BPet {available.Version}…";
            var reporter = new Progress<int>(value =>
            {
                if (_installing) return;
                Progress.Value = value;
                Status.Text = $"Đang tải bản cập nhật… {value}%";
            });
            await _updates.DownloadAndLaunchAsync(available, reporter, _cancel.Token);
            _installing = true;
            Status.Text = "Đang cài bản mới. BPet sẽ tự mở lại sau khi cài xong.";
            if (Application.Current.MainWindow is MainWindow pet) pet.RequestExit();
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException) { Status.Text = "Đã hủy cập nhật."; }
        catch (Exception error) { Status.Text = $"Không thể cập nhật: {error.Message}"; }
        finally
        {
            _busy = false;
            _updateInProgress = false;
            UpdateButton.IsEnabled = true;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_busy && !_installing) { e.Cancel = true; return; }
        base.OnClosing(e);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
