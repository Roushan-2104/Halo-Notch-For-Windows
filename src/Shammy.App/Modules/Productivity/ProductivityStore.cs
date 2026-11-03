using System.Text.Json.Serialization;

namespace Shammy.Modules.Productivity;

/// <summary>What survives a restart: reminders (upcoming and fired) and quick notes.</summary>
internal sealed class ProductivityStore
{
    public List<ReminderItem> Reminders { get; set; } = new();
    public List<NoteItem> Notes { get; set; } = new();
}

internal sealed class ReminderItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime Due { get; set; }
    public DateTime Created { get; set; }

    /// <summary>Set once the reminder has popped up; it stays listed until marked done or deleted.</summary>
    public DateTime? FiredAt { get; set; }

    /// <summary>Ticked off (on the Notes card or the reminder's Done button). A done reminder never fires.</summary>
    public DateTime? DoneAt { get; set; }

    [JsonIgnore] public bool Fired => FiredAt is not null;
    [JsonIgnore] public bool Done => DoneAt is not null;
    [JsonIgnore] public string Title => Text.Length > 0 ? Text : "Reminder";
}

internal sealed class NoteItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime Created { get; set; }

    /// <summary>Ticked off on the Notes card; it stays listed (struck through) until deleted.</summary>
    public DateTime? DoneAt { get; set; }

    [JsonIgnore] public bool Done => DoneAt is not null;
}
