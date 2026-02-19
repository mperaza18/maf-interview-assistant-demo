using System.Text.Json.Serialization;

namespace InterviewAssistant.Models;

public sealed class InterviewPlan
{
    [JsonPropertyName("role")] public string Role { get; set; } = "Software Engineer";
    [JsonPropertyName("level")] public string Level { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("rounds")] public List<InterviewRound> Rounds { get; set; } = new();
    [JsonPropertyName("rubric")] public List<RubricItem> Rubric { get; set; } = new();
}

public sealed class InterviewRound
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("durationMinutes")] public int DurationMinutes { get; set; }
    [JsonPropertyName("questions")] public List<string> Questions { get; set; } = new();
}

public sealed class RubricItem
{
    [JsonPropertyName("dimension")] public string Dimension { get; set; } = "";
    [JsonPropertyName("signals")] public List<string> Signals { get; set; } = new();
}
