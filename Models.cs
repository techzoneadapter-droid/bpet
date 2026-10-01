namespace BPet;

public enum PetState { Idle, Walk, Run, Sit, Sleep, Wake, Dragged, Falling, Eating, Happy, Talking, Listening, Thinking, Working, Celebrating }
public enum AiProviderKind { None, OpenAI, Gemini, OpenAiCompatible }

public sealed class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public AiSettings Ai { get; set; } = new();
    public PersonalitySettings Personality { get; set; } = new();
    public PetBehaviorSettings PetBehavior { get; set; } = new();
    public List<PersonalityProfile> Profiles { get; set; } = new();
    public List<Reminder> Reminders { get; set; } = new();
    public List<DailyTask> DailyTasks { get; set; } = new();
}

public sealed class GeneralSettings
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public bool AlwaysOnTop { get; set; } = true;
    public bool ClickThrough { get; set; }
    public bool LaunchWithWindows { get; set; }
    public string CharacterId { get; set; } = "bpet-my";
    public bool PickedMy { get; set; }
    public string Language { get; set; } = "vi-VN";
    public int SizePercent { get; set; } = 48;
    public string StyleId { get; set; } = "thuong";
}

public sealed class AiSettings
{
    public AiProviderKind Provider { get; set; } = AiProviderKind.None;
    public string OpenAiModel { get; set; } = "gpt-4.1-mini";
    public string GeminiModel { get; set; } = "gemini-3.8-flash";
    public string CustomModelId { get; set; } = "";
    public string CustomBaseUrl { get; set; } = "";
    public bool Streaming { get; set; } = true;
}

public sealed class PersonalitySettings
{
    public string PetName { get; set; } = "BPet";
    public string UserName { get; set; } = "Bạn";
    public string Attitude { get; set; } = "Caring";
    public string RelationshipPreset { get; set; } = "Trợ lý";
    public string PetPronoun { get; set; } = "em";
    public string UserPronoun { get; set; } = "bạn";
    public string RelationshipDescription { get; set; } = "personal assistant";
    public int Affection { get; set; } = 60;
    public int Humor { get; set; } = 35;
    public int Formality { get; set; } = 55;
    public int Talkativeness { get; set; } = 45;
    public int Proactiveness { get; set; } = 35;
    public int EmojiUsage { get; set; } = 15;
    public string CustomInstructions { get; set; } = "";
}

public sealed class PetBehaviorSettings
{
    public bool AutoMovement { get; set; } = true;
    public bool SimulationEnabled { get; set; } = true;
    public int MovementFrequency { get; set; } = 30;
    public int Happiness { get; set; } = 70;
    public int Energy { get; set; } = 80;
    public int Hunger { get; set; } = 45;
    public int Affinity { get; set; } = 50;
}

public sealed class PersonalityProfile
{
    public string Name { get; set; } = "";
    public PersonalitySettings Personality { get; set; } = new();
    public AiProviderKind? Provider { get; set; }
}

public sealed class DailyTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "custom";
    public string Note { get; set; } = "";
    public int Hour { get; set; } = 9;
    public int Minute { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime LastRun { get; set; }
    public override string ToString() => $"{Hour:00}:{Minute:00}  {Name}";
}

public sealed class Reminder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Message { get; set; } = "";
    public DateTime DueAt { get; set; }
    public bool Delivered { get; set; }
}

public sealed record ChatMessage(string Role, string Content);

public sealed class CharacterManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public string RecommendedAttitude { get; set; } = "Cute";
    public string Preview { get; set; } = "preview.png";
    public Dictionary<string, string> Animations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record CharacterInfo(string Id, string Name, string RecommendedAttitude, bool IsBuiltIn, string? Folder, CharacterManifest Manifest)
{
    public override string ToString() => Name;
}
