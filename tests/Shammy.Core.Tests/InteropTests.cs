using System.Text.RegularExpressions;

namespace Shammy.Core.Tests;

/// <summary>Guards the native declarations in the source tree (the app project isn't referenced from tests).</summary>
public class InteropTests
{
    static readonly Regex Import = new(@"\[DllImport\((?<attr>[^\]]*)\)\]\s*(?<decl>[^;]*\bextern\b[^;]*);", RegexOptions.Singleline);
    static readonly Regex TextParam = new(@"\b(string\??|StringBuilder|char\[\])\s+\w+");

    static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("repo root"), "src");
    }

    /// <summary>
    /// Without CharSet.Unicode, .NET passes text buffers as one byte per character while the W functions
    /// write two: a heap overrun. That exact mistake in ToUnicodeEx corrupted Shammy's heap on every key press.
    /// </summary>
    [Fact]
    public void Every_import_that_passes_text_declares_unicode()
    {
        var offenders = new List<string>();
        int checkedCount = 0;
        foreach (var file in Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            foreach (Match m in Import.Matches(File.ReadAllText(file)))
            {
                if (!TextParam.IsMatch(m.Groups["decl"].Value)) continue;
                checkedCount++;
                if (!m.Groups["attr"].Value.Contains("CharSet = CharSet.Unicode"))
                    offenders.Add($"{Path.GetFileName(file)}: {Regex.Replace(m.Groups["decl"].Value, @"\s+", " ").Trim()}");
            }
        }
        Assert.True(checkedCount > 5, $"only {checkedCount} text-taking imports found; is the scan still working?");
        Assert.Empty(offenders);
    }
}
