using System.Text.Json;

namespace RunReport;

public sealed record SavedRun(string JsonPath, string MarkdownPath);

/// <summary>Saves a run as <c>&lt;id&gt;.json</c> (the full record) and <c>&lt;id&gt;.md</c> (the readable summary), and reads records back.</summary>
public static class RunStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static SavedRun Save(RunRecord record, string directory)
    {
        Directory.CreateDirectory(directory);
        var jsonPath = Path.Combine(directory, record.Id + ".json");
        var markdownPath = Path.Combine(directory, record.Id + ".md");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(record, JsonOptions));
        File.WriteAllText(markdownPath, RunMarkdown.Render(record));
        return new SavedRun(jsonPath, markdownPath);
    }

    public static RunRecord Load(string path) =>
        JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"{path} does not contain a run record.");

    /// <summary>Every <c>*.json</c> record in the directory, oldest run first.</summary>
    public static IReadOnlyList<RunRecord> LoadAll(string directory) =>
        Directory.EnumerateFiles(directory, "*.json").Select(Load).OrderBy(r => r.Configuration.StartUtc).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
}
