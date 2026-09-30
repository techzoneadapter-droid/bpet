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
        _panels = new() { ["General"] = GeneralPanel, ["Character"] = CharacterPanel, ["AI Provider"] = AiPanel, ["AI Personality"] = PersonalityPanel, ["Relationship"] = RelationshipPanel, ["Pet Behavior"] = BehaviorPanel, ["Memory"] = InfoPanel, ["Advanced"] = InfoPanel, ["About"] = InfoPanel };
        LoadSettings();
    }
    private void LoadSettings()
    {
        var s = _services.Settings; var p = s.Personality;
        AlwaysOnTop.IsChecked = s.General.AlwaysOnTop; ClickThrough.IsChecked = s.General.ClickThrough; LanguagePicker.SelectedIndex = s.General.Language.StartsWith("vi") ? 0 : 1; ReloadCharacters();
        Provider.SelectedIndex = s.Ai.Provider switch { AiProviderKind.OpenAI => 1, AiProviderKind.Gemini => 2, AiProviderKind.OpenAiCompatible => 3, _ => 0 }; Model.Text = s.Ai.Provider == AiProviderKind.Gemini ? s.Ai.GeminiModel : s.Ai.OpenAiModel; BaseUrl.Text = s.Ai.CustomBaseUrl; Streaming.IsChecked = s.Ai.Streaming;
        PetName.Text = p.PetName; UserName.Text = p.UserName; Select(Attitude, p.Attitude); Affection.Value = p.Affection; Humor.Value = p.Humor; Formality.Value = p.Formality; Talkativeness.Value = p.Talkativeness; Proactiveness.Value = p.Proactiveness; EmojiUsage.Value = p.EmojiUsage; CustomInstructions.Text = p.CustomInstructions; ReloadProfiles();
        Select(RelationshipPreset, p.RelationshipPreset); PetPronoun.Text = p.PetPronoun; UserPronoun.Text = p.UserPronoun; RelationshipDescription.Text = p.RelationshipDescription;
        AutoMovement.IsChecked = s.PetBehavior.AutoMovement; Simulation.IsChecked = s.PetBehavior.SimulationEnabled; MovementFrequency.Value = s.PetBehavior.MovementFrequency;
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
        if (title is "Memory" or "Advanced" or "About") InfoText.Text = title == "Memory" ? "Memory đang để ở chế độ local-only. Nền tảng giữ cấu hình và nhắc việc trên máy; lớp SQLite conversation/memory sẽ được bổ sung khi bắt đầu phần lịch sử chat." : title == "Advanced" ? "BPet không tự chạy lệnh shell, không tự đọc file, clipboard hay xóa dữ liệu. Những tool sau này đều cần cơ chế xin phép rõ ràng." : "BPet — Your AI companion on the desktop. Phiên bản nền tảng WPF cho Windows 10/11.";
    }
    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        SaveValues(false); ConnectionStatus.Text = "Đang kiểm tra…"; var ok = await _services.CurrentProvider().TestAsync(CancellationToken.None); ConnectionStatus.Text = ok ? "Kết nối thành công." : "Chưa kết nối được. Hãy kiểm tra API key, model và mạng.";
    }
    private void RelationshipPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return; var preset = Choice(RelationshipPreset);
        var values = preset switch { "Trợ lý" => ("em", "anh/chị", "personal assistant"), "Dễ thương" => ("em", "anh", "cute companion"), "Anh - Em" => ("em", "anh", "warm companion"), "Em - Anh" => ("anh", "em", "warm companion"), "Chồng - Vợ" => ("em", "chồng", "married couple"), "Vợ - Chồng" => ("anh", "vợ", "married couple"), "Sếp - Trợ lý" => ("em", "sếp", "personal assistant"), "Tôi - Bạn" => ("tôi", "bạn", "friendly assistant"), "Mình - Bạn" => ("mình", "bạn", "friendly companion"), _ => ("mình", "bạn", "friend") };
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
    private void Preview_Click(object sender, RoutedEventArgs e) { SaveValues(false); PreviewText.Text = $"{_services.Settings.Personality.UserPronoun} cứ làm việc đi nha, có gì cần thì gọi {_services.Settings.Personality.PetPronoun}. {_services.Settings.Personality.PetName} ở đây nè."; }
    private void Save_Click(object sender, RoutedEventArgs e) { SaveValues(true); Close(); }
    private void Reset_Click(object sender, RoutedEventArgs e) { _services.Settings.Personality = new(); LoadSettings(); }
    private void SaveValues(bool persist)
    {
        var s = _services.Settings; var p = s.Personality; s.General.AlwaysOnTop = AlwaysOnTop.IsChecked == true; s.General.ClickThrough = ClickThrough.IsChecked == true; s.General.Language = LanguagePicker.SelectedIndex == 0 ? "vi-VN" : "en-US";
        s.Ai.Provider = Provider.SelectedIndex switch { 1 => AiProviderKind.OpenAI, 2 => AiProviderKind.Gemini, 3 => AiProviderKind.OpenAiCompatible, _ => AiProviderKind.None }; s.Ai.Streaming = Streaming.IsChecked == true; s.Ai.CustomBaseUrl = BaseUrl.Text.Trim(); if (s.Ai.Provider == AiProviderKind.Gemini) s.Ai.GeminiModel = Model.Text.Trim(); else s.Ai.OpenAiModel = Model.Text.Trim();
        var secretName = s.Ai.Provider switch { AiProviderKind.OpenAI => "openai", AiProviderKind.Gemini => "gemini", AiProviderKind.OpenAiCompatible => "custom", _ => "" }; if (!string.IsNullOrWhiteSpace(secretName) && !string.IsNullOrWhiteSpace(ApiKey.Password)) _services.Credentials.Save(secretName, ApiKey.Password);
        p.PetName = PetName.Text.Trim() is { Length: > 0 } name ? name : "BPet"; p.UserName = UserName.Text.Trim() is { Length: > 0 } user ? user : "Bạn"; p.Attitude = Choice(Attitude); p.RelationshipPreset = Choice(RelationshipPreset); p.PetPronoun = PetPronoun.Text.Trim(); p.UserPronoun = UserPronoun.Text.Trim(); p.RelationshipDescription = RelationshipDescription.Text.Trim(); p.Affection = (int)Affection.Value; p.Humor = (int)Humor.Value; p.Formality = (int)Formality.Value; p.Talkativeness = (int)Talkativeness.Value; p.Proactiveness = (int)Proactiveness.Value; p.EmojiUsage = (int)EmojiUsage.Value; p.CustomInstructions = CustomInstructions.Text;
        s.PetBehavior.AutoMovement = AutoMovement.IsChecked == true; s.PetBehavior.SimulationEnabled = Simulation.IsChecked == true; s.PetBehavior.MovementFrequency = (int)MovementFrequency.Value; ((MainWindow)Application.Current.MainWindow).ApplyOptions(); if (persist) _services.Save();
    }
}
