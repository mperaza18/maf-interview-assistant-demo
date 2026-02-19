using System.Text.Json.Serialization;

namespace InterviewAssistant.Models;

public sealed class SeniorityAssessment
{
    [JsonPropertyName("level")] public string Level { get; set; } = ""; // e.g. Junior/Mid/Senior
    [JsonPropertyName("confidence")] public double Confidence { get; set; }
    [JsonPropertyName("rationale")] public string Rationale { get; set; } = "";
}
