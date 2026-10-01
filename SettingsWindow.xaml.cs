using System.Windows;
using System.Windows.Controls;

namespace BPet;

public partial class SettingsWindow : Window
{
    private readonly AppServices _services;
    private readonly Dictionary<string, FrameworkElement> _panels;
    public SettingsWindow(AppServices services)
    {
        InitializeComponent(); _services = services;
        _panels = new() { ["General"] = GeneralPanel, ["Character"] = CharacterPanel, ["AI Provider"] = AiPanel, ["AI Personality"] = PersonalityPanel, ["Relationship"] = RelationshipPanel, ["Pet Behavior"] = BehaviorPanel, ["Việc hàng ngày"] = TasksPanel, ["Báo tin"] = NewsPanel, ["Memory"] = InfoPanel, ["Advanced"] = InfoPanel, ["About"] = InfoPanel };
        LoadSettings();
    }
    private void LoadSettings()
    {
        var s = _services.Settings; var p = s.Personality;
        AlwaysOnTop.IsChecked = s.General.AlwaysOnTop; ClickThrough.IsChecked = s.General.ClickThrough; LaunchWithWindows.IsChecked = s.General.LaunchWithWindows; LanguagePicker.SelectedIndex = s.General.Language.StartsWith("vi") ? 0 : 1; ReloadCharacters();
        Provider.SelectedIndex = s.Ai.Provider switch { AiProviderKind.OpenAI => 1, AiProviderKind.Gemini => 2, AiProviderKind.OpenAiCompatible => 3, _ => 0 };
        if (s.Ai.Provider == AiProviderKind.Gemini) s.Ai.GeminiModel = GeminiProvider.GeminiModelName(s.Ai.GeminiModel);
        Model.Text = s.Ai.Provider == AiProviderKind.Gemini ? s.Ai.GeminiModel : s.Ai.OpenAiModel;
        BaseUrl.Text = s.Ai.CustomBaseUrl; Streaming.IsChecked = s.Ai.Streaming;
        PetName.Text = p.PetName; UserName.Text = p.UserName; Select(Attitude, p.Attitude); Affection.Value = p.Affection; Humor.Value = p.Humor; Formality.Value = p.Formality; Talkativeness.Value = p.Talkativeness; Proactiveness.Value = p.Proactiveness; EmojiUsage.Value = p.EmojiUsage; CustomInstructions.Text = p.CustomInstructions; ReloadProfiles();
        Select(RelationshipPreset, p.RelationshipPreset); PetPronoun.Text = p.PetPronoun; UserPronoun.Text = p.UserPronoun; RelationshipDescription.Text = p.RelationshipDescription;
        AutoMovement.IsChecked = s.PetBehavior.AutoMovement; Simulation.IsChecked = s.PetBehavior.SimulationEnabled; MovementFrequency.Value = s.PetBehavior.MovementFrequency;
        var size = s.General.SizePercent is < 35 or > 140 ? 48 : s.General.SizePercent;
        PetSize.Value = size; SizeLabel.Text = size + "%";
        PetStyle.SelectedIndex = s.General.StyleId switch { "kiem-hiep" => 1, "giang-ho" => 2, _ => 0 };
        NewsEnabled.IsChecked = s.News.Enabled; NewsGold.IsChecked = s.News.Gold; NewsAi.IsChecked = s.News.Ai; NewsMkt.IsChecked = s.News.Marketing;
        NewsInterval.Value = Math.Clamp(s.News.IntervalMinutes, 10, 180); NewsIntervalLabel.Text = (int)NewsInterval.Value + " phút";
        ReloadTasks();
    }
    private static void Select(System.Windows.Controls.ComboBox combo, string text) { foreach (System.Windows.Controls.ComboBoxItem item in combo.Items) if ((string)item.Content == text) { combo.SelectedItem = item; break; } }
    private static string Choice(System.Windows.Controls.ComboBox combo) => (combo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "";
    private void ReloadCharacters()
    {
        Character.ItemsSource = _services.Characters.All;
        Character.SelectedItem = _services.Characters.Find(_services.Settings.General.CharacterId) ?? _services.Characters.All.FirstOrDefault();
    }
    private void ReloadProfiles() => ProfilePicker.ItemsSource = _services.Settings.Profiles;
    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Nav.SelectedItem is not ListBoxItem item) return; var title = item.Content.ToString()!; PageTitle.Text = title;
        foreach (var panel in _panels.Values.Distinct()) panel.Visibility = Visibility.Collapsed;
        _panels[title].Visibility = Visibility.Visible;
        if (title is "Memory" or "Advanced" or "About") InfoText.Text = title == "Memory" ? "Hội thoại được lưu trên máy, tối đa 200 tin. Tiện ích trình duyệt lưu phiên tối đa 30 ngày khi bạn cài và bật tiện ích. Phiên trình duyệt không gửi đến AI." : title == "Advanced" ? "Chat hỗ trợ lệnh mở ứng dụng, web, khóa máy, tắt/khởi động lại máy và nhắc việc. Tắt máy và mở nhiều tab cần xác nhận. BPet không chạy lệnh shell tùy ý do AI tạo ra." : "BPet — Your AI companion on the desktop. Phiên bản nền tảng WPF cho Windows 10/11.";
    }
    private void PetSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || SizeLabel is null) return;
        var size = (int)Math.Clamp(e.NewValue, 35, 140);
        SizeLabel.Text = size + "%";
        _services.Settings.General.SizePercent = size;
        if (Application.Current.MainWindow is MainWindow pet) pet.ApplyOptions();
    }
    private void PetSize_Save(object sender, System.Windows.Input.MouseButtonEventArgs e) => _services.Save();
    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (Provider.SelectedIndex <= 0) { ConnectionStatus.Text = "Hãy chọn OpenAI hoặc Google Gemini trước."; return; }
        try
        {
            SaveValues(false);
            ConnectionStatus.Text = "Đang kiểm tra…";
            var reply = await _services.CurrentProvider().CompleteAsync("Reply with the single word OK.", new[] { new ChatMessage("user", "ping") }, CancellationToken.None);
            ConnectionStatus.Text = string.IsNullOrWhiteSpace(reply) ? "Đã kết nối nhưng phản hồi trống." : "Kết nối thành công.";
        }
        catch (Exception error) { ConnectionStatus.Text = "Chưa kết nối được: " + error.Message; }
    }
    private void Provider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Model is null) return;
        if (Provider.SelectedIndex == 2) Model.Text = GeminiProvider.GeminiModelName(Model.Text);
        else if (Provider.SelectedIndex is 1 or 3 && Model.Text.Trim().StartsWith("gemini-", StringComparison.OrdinalIgnoreCase)) Model.Text = "gpt-4.1-mini";
    }
    private void RelationshipPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (Choice(RelationshipPreset) is "" or "Custom") return;
        var preset = Choice(RelationshipPreset);
        var values = preset switch { "Trợ lý" => ("em", "anh/chị", "trợ lý riêng, lễ phép vừa đủ"), "Bạn bè" => ("mình", "bạn", "bạn bè ngang hàng"), "Dễ thương" => ("em", "anh", "dễ thương, gần gũi"), "Anh - Em" => ("em", "anh", "em nói với anh"), "Em - Anh" => ("anh", "em", "anh nói với em"), "Chồng - Vợ" => ("em", "anh", "vợ nói với chồng"), "Vợ - Chồng" => ("anh", "em", "chồng nói với vợ"), "Sếp - Trợ lý" => ("em", "sếp", "trợ lý nói với sếp"), "Tôi - Bạn" => ("tôi", "bạn", "lịch sự, khoảng cách vừa"), "Mình - Bạn" => ("mình", "bạn", "thân nhưng không suồng sã"), _ => ("mình", "bạn", "tự nhiên") };
        PetPronoun.Text = values.Item1; UserPronoun.Text = values.Item2; RelationshipDescription.Text = values.Item3;
    }
    private void Character_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Character.SelectedItem is not CharacterInfo character) return;
        _services.Settings.General.CharacterId = character.Id;
        ((MainWindow)Application.Current.MainWindow).ApplyCharacter();
    }
    private void ProfilePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ProfilePicker.SelectedItem is not PersonalityProfile profile) return;
        _services.ActivatePersonalityProfile(profile); ProfileName.Text = profile.Name; LoadSettings();
    }
    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        SaveValues(false);
        try { _services.SavePersonalityProfile(ProfileName.Text); ReloadProfiles(); System.Windows.MessageBox.Show(this, "Đã lưu profile tính cách.", "BPet"); }
        catch (Exception error) { System.Windows.MessageBox.Show(this, error.Message, "BPet", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void ImportZip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Character pack (*.zip)|*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        TryInstall(() => _services.Characters.ImportZip(dialog.FileName));
    }
    private void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Chọn thư mục character có manifest.json" };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        TryInstall(() => _services.Characters.ImportFolder(dialog.SelectedPath));
    }
    private void TryInstall(Func<CharacterInfo> install)
    {
        try { var character = install(); _services.Settings.General.CharacterId = character.Id; ReloadCharacters(); ((MainWindow)Application.Current.MainWindow).ApplyCharacter(); _services.Save(); System.Windows.MessageBox.Show(this, $"Đã cài {character.Name}.", "BPet"); }
        catch (Exception error) { System.Windows.MessageBox.Show(this, error.Message, "Không thể cài character", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        SaveValues(false);
        var p = _services.Settings.Personality;
        var self = string.IsNullOrWhiteSpace(p.PetPronoun) ? "mình" : p.PetPronoun.Trim();
        var user = string.IsNullOrWhiteSpace(p.UserPronoun) ? "bạn" : p.UserPronoun.Trim();
        PreviewText.Text = $"{user} ơi, {self} ở đây. Cứ gọi {self} khi cần.";
    }
    private void NewsInterval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || NewsIntervalLabel is null) return;
        NewsIntervalLabel.Text = (int)e.NewValue + " phút";
    }
    private void NewsNow_Click(object sender, RoutedEventArgs e)
    {
        SaveValues(true);
        _services.News.ReportSoon();
    }
    private void PetStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _services.Settings.General.StyleId = PetStyle.SelectedIndex switch { 1 => "kiem-hiep", 2 => "giang-ho", _ => "thuong" };
        _services.Save();
    }
    private void ReloadTasks() => TaskList.ItemsSource = _services.Settings.DailyTasks.ToList();
    private void AddTask_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TaskHour.Text, out var hour) || hour is < 0 or > 23 || !int.TryParse(TaskMinute.Text, out var minute) || minute is < 0 or > 59)
        {
            System.Windows.MessageBox.Show(this, "Giờ phải từ 0 đến 23, phút từ 0 đến 59.", "BPet");
            return;
        }
        var name = TaskName.Text.Trim();
        if (name.Length == 0) name = (TaskKind.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Việc hàng ngày";
        var kind = (TaskKind.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "custom";
        _services.Settings.DailyTasks.Add(new DailyTask { Name = name, Kind = kind, Note = TaskNote.Text.Trim(), Hour = hour, Minute = minute });
        _services.Save();
        TaskName.Clear();
        TaskNote.Clear();
        ReloadTasks();
    }
    private void RemoveTask_Click(object sender, RoutedEventArgs e)
    {
        if (TaskList.SelectedItem is not DailyTask task) return;
        _services.Settings.DailyTasks.Remove(task);
        _services.Save();
        ReloadTasks();
    }
    private void Save_Click(object sender, RoutedEventArgs e) { SaveValues(true); Close(); }
    private void Reset_Click(object sender, RoutedEventArgs e) { _services.Settings.Personality = new(); LoadSettings(); }
    private void SaveValues(bool persist)
    {
        var s = _services.Settings; var p = s.Personality; s.General.AlwaysOnTop = AlwaysOnTop.IsChecked == true; s.General.ClickThrough = ClickThrough.IsChecked == true; s.General.LaunchWithWindows = LaunchWithWindows.IsChecked == true; s.General.Language = LanguagePicker.SelectedIndex == 0 ? "vi-VN" : "en-US";
        s.Ai.Provider = Provider.SelectedIndex switch { 1 => AiProviderKind.OpenAI, 2 => AiProviderKind.Gemini, 3 => AiProviderKind.OpenAiCompatible, _ => AiProviderKind.None }; s.Ai.Streaming = Streaming.IsChecked == true; s.Ai.CustomBaseUrl = BaseUrl.Text.Trim(); s.Ai.CustomModelId = ""; if (s.Ai.Provider == AiProviderKind.Gemini) { s.Ai.GeminiModel = GeminiProvider.GeminiModelName(Model.Text); Model.Text = s.Ai.GeminiModel; } else s.Ai.OpenAiModel = Model.Text.Trim();
        var secretName = s.Ai.Provider switch { AiProviderKind.OpenAI => "openai", AiProviderKind.Gemini => "gemini", AiProviderKind.OpenAiCompatible => "custom", _ => "" }; if (!string.IsNullOrWhiteSpace(secretName) && !string.IsNullOrWhiteSpace(ApiKey.Password)) _services.Credentials.Save(secretName, ApiKey.Password);
        p.PetName = PetName.Text.Trim() is { Length: > 0 } name ? name : "BPet"; p.UserName = UserName.Text.Trim() is { Length: > 0 } user ? user : "Bạn"; p.Attitude = Choice(Attitude); p.RelationshipPreset = Choice(RelationshipPreset); p.PetPronoun = PetPronoun.Text.Trim(); p.UserPronoun = UserPronoun.Text.Trim(); p.RelationshipDescription = RelationshipDescription.Text.Trim(); p.Affection = (int)Affection.Value; p.Humor = (int)Humor.Value; p.Formality = (int)Formality.Value; p.Talkativeness = (int)Talkativeness.Value; p.Proactiveness = (int)Proactiveness.Value; p.EmojiUsage = (int)EmojiUsage.Value; p.CustomInstructions = CustomInstructions.Text;
        s.PetBehavior.AutoMovement = AutoMovement.IsChecked == true; s.PetBehavior.SimulationEnabled = Simulation.IsChecked == true; s.PetBehavior.MovementFrequency = (int)MovementFrequency.Value; s.General.SizePercent = (int)Math.Clamp(PetSize.Value, 35, 140); s.General.StyleId = PetStyle.SelectedIndex switch { 1 => "kiem-hiep", 2 => "giang-ho", _ => "thuong" }; s.News.Enabled = NewsEnabled.IsChecked == true; s.News.Gold = NewsGold.IsChecked == true; s.News.Ai = NewsAi.IsChecked == true; s.News.Marketing = NewsMkt.IsChecked == true; s.News.IntervalMinutes = (int)NewsInterval.Value; if (Application.Current.MainWindow is MainWindow pet) pet.ApplyOptions(); if (persist) { WindowsStartup.Apply(s.General.LaunchWithWindows); _services.Save(); _services.Tray.SyncStartupItem(); }
    }
}

