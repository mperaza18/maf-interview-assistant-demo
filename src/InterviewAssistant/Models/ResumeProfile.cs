using System.Text.Json.Serialization;

namespace InterviewAssistant.Models;

public sealed class ResumeProfile
{
    [JsonPropertyName("candidateName")] public string CandidateName { get; set; } = "";
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("currentTitle")] public string? CurrentTitle { get; set; }
    [JsonPropertyName("yearsExperience")] public double? YearsExperience { get; set; }
    [JsonPropertyName("coreSkills")] public List<string> CoreSkills { get; set; } = new();
    [JsonPropertyName("roles")] public List<string> Roles { get; set; } = new();
    [JsonPropertyName("notableProjects")] public List<string> NotableProjects { get; set; } = new();
    [JsonPropertyName("redFlags")] public List<string> RedFlags { get; set; } = new();
}
