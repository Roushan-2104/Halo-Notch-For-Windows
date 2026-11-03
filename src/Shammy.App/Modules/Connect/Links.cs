using Shammy.Modules.Productivity;

namespace Shammy.Modules.Connect;

/// <summary>What the Notes/reminders module offers other modules (save a note, set a reminder, read today's list).</summary>
internal interface INotesLink
{
    IReadOnlyList<ReminderItem> Reminders { get; }
    IReadOnlyList<NoteItem> Notes { get; }

    /// <summary>Saves a note and returns the confirmation (the caller shows it, as a command result or a message).</summary>
    CommandResult SaveNote(string text);

    /// <summary>Adds a reminder and returns the confirmation.</summary>
    CommandResult AddReminder(DateTime due, string text);
}

/// <summary>What the clipboard module offers the palette: its history, and copying an entry back.</summary>
internal interface IClipboardLink
{
    IReadOnlyList<Shammy.Core.Clipboard.ClipEntry> Items { get; }
    void CopyBack(Shammy.Core.Clipboard.ClipEntry entry);
}

internal static class LinkExtensions
{
    /// <summary>A command's confirmation as a short notch message (for context-menu actions that aren't commands).</summary>
    public static void ShowResult(this ModuleContext ctx, CommandResult r) =>
        ctx.ShowMessage(r.Glyph, r.Title, r.IsError, seconds: 2.5);
}

/// <summary>
/// The few places modules reach into each other for the features that connect them (reminders on the calendar,
/// "save as note", palette search). Set by the owning module in Start; null while it isn't running.
/// </summary>
internal static class Links
{
    public static INotesLink? Notes { get; set; }

    public static IClipboardLink? Clipboard { get; set; }

    /// <summary>Opens a card's detail (or "shelf", "alerts") in the open notch. One place, so it can move to ctx.OpenCard later.</summary>
    public static bool OpenCard(ModuleContext ctx, string id) => ctx.Notch.OpenView(id);
}
