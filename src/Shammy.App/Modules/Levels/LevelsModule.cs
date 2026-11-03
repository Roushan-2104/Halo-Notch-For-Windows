using Shammy.Core.Levels;
using Shammy.Modules.Palette;

namespace Shammy.Modules.Levels;

/// <summary>
/// "volume 50", "brightness to 70%", "volume up", "mute": sets the level straight away, typed or spoken. Uses the
/// palette's controls (Core Audio for the default output, the power-plan brightness the Windows slider changes).
/// </summary>
internal sealed class LevelsModule : IShammyModule, ITextCommand, ICommandPreview
{
    const string VolumeGlyph = "", MuteGlyph = "", BrightnessGlyph = "";

    readonly AudioOutputs _audio = new();

    public string Name => "levels";

    public void Start(ModuleContext ctx)
    {
        ctx.AddCommand(this);
        // Dry run: what a phrase would do, without touching anything.
        ctx.AddPipeCommand("levels-parse", arg => LevelPhrase.Parse(arg) is { } c ? $"{c.Target} {c.Action} {c.Percent} clamped={c.Clamped}" : "none");
    }

    public bool CanHandle(string text) => LevelPhrase.Parse(text) is not null;

    public CommandPreview? Preview(string text)
    {
        if (LevelPhrase.Parse(text) is not { } c) return null;
        var detail = c.Clamped ? "100% is the most there is" : c.Target == LevelTarget.Volume ? "Sound" : "Built-in display";
        return new CommandPreview(Glyph(c), LevelPhrase.Describe(c), detail);
    }

    public Task<CommandResult> RunAsync(string text)
    {
        if (LevelPhrase.Parse(text) is not { } c) return Task.FromResult(new CommandResult("", "Not a volume or brightness command", IsError: true));
        return Task.FromResult(c.Target == LevelTarget.Volume ? Volume(c) : Brightness(c));
    }

    CommandResult Volume(LevelCommand c)
    {
        if (_audio.Volume() is not { } v) return new CommandResult(MuteGlyph, "No sound output", "Couldn't find a speaker or headphones", IsError: true);
        switch (c.Action)
        {
            case LevelAction.Mute:
                _audio.SetVolume(v.Level, mute: true);
                return new CommandResult(MuteGlyph, "Muted");
            case LevelAction.Unmute:
                _audio.SetVolume(v.Level, mute: false);
                return new CommandResult(VolumeGlyph, "Unmuted", $"Volume {Percent(v.Level)}%");
        }
        int level = LevelPhrase.Apply(c, Percent(v.Level));
        // Setting 0 means silence, not "muted at 0"; anything above unmutes (SetVolume does that).
        _audio.SetVolume(level / 100f);
        return new CommandResult(level == 0 ? MuteGlyph : VolumeGlyph, $"Volume {level}%", c.Clamped ? "Capped at 100%" : null);
    }

    static CommandResult Brightness(LevelCommand c)
    {
        if (BrightnessControl.Get() is not { } current)
            return new CommandResult(BrightnessGlyph, "Can't change brightness", "This display doesn't follow Windows brightness", IsError: true);
        int level = LevelPhrase.Apply(c, current);
        return BrightnessControl.Set(level)
            ? new CommandResult(BrightnessGlyph, $"Brightness {level}%", c.Clamped ? "Capped at 100%" : null)
            : new CommandResult(BrightnessGlyph, "Couldn't change brightness", null, IsError: true);
    }

    static int Percent(float level) => (int)Math.Round(level * 100);

    static string Glyph(LevelCommand c) =>
        c.Target == LevelTarget.Brightness ? BrightnessGlyph : c.Action == LevelAction.Mute || (c.Action == LevelAction.Set && c.Percent == 0) ? MuteGlyph : VolumeGlyph;

    public void Dispose() => _audio.Dispose();
}
