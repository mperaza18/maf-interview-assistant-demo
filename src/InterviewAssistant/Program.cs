// Demo 3 — Workflow Orchestration (final state: main branch)
// Builds on Demo 2 (multi-agent pipeline + human-in-the-loop).
// Run with --mode simple  (default) to replay the Demo 2 sequential pipeline.
// Run with --mode workflow          to use the MAF WorkflowBuilder graph instead.
using System.Text;
using InterviewAssistant.Agents;
using InterviewAssistant.Models;
using InterviewAssistant.Workflows;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

static string? GetArg(string[] args, string name)
{
    var idx = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    if (idx >= 0 && idx < args.Length - 1) return args[idx + 1];
    return null;
}

var mode       = GetArg(args, "--mode")   ?? "simple"; // simple | workflow
var role       = GetArg(args, "--role")   ?? "Software Engineer";
var resumePath = GetArg(args, "--resume") ?? Path.Combine("assets", "resumes", "jane_doe.txt");

if (!File.Exists(resumePath))
{
    Console.Error.WriteLine($"Resume not found: {resumePath}");
    return;
}

var resumeText = await File.ReadAllTextAsync(resumePath);

// ---- Create agents (Azure OpenAI backend) ----
AIAgent ingestionAgent = AgentFactory.CreateAzureOpenAIAgent("ResumeIngestion",     AgentPrompts.ResumeIngestion);
AIAgent seniorityAgent = AgentFactory.CreateAzureOpenAIAgent("SeniorityClassifier", AgentPrompts.SeniorityClassifier);
AIAgent plannerAgent   = AgentFactory.CreateAzureOpenAIAgent("InterviewPlanner",    AgentPrompts.InterviewPlanner);
AIAgent evaluatorAgent = AgentFactory.CreateAzureOpenAIAgent("Evaluator",           AgentPrompts.Evaluator);

Console.WriteLine("\n=== Microsoft Agent Framework: AI Interview Assistant ===\n");
Console.WriteLine($"Mode  : {mode}");
Console.WriteLine($"Role  : {role}");
Console.WriteLine($"Resume: {resumePath}\n");

ResumeProfile        profile;
SeniorityAssessment  seniority;
InterviewPlan        plan;

// =============================================================================
// Demo 3 — Workflow Orchestration
// Replace the three sequential await calls with a single WorkflowBuilder graph.
// =============================================================================
if (mode.Equals("workflow", StringComparison.OrdinalIgnoreCase))
{
    // A single ChatMessage carries all context; the workflow routes it through
    // each agent in the declared graph order.
    var input = new ChatMessage(ChatRole.User,
        $"Target role: {role}\n\nRESUME:\n{resumeText}\n\n" +
        "First extract a ResumeProfile JSON, then classify seniority, then produce an InterviewPlan JSON.");

    Console.WriteLine("--- Running planning workflow (ingest → classify → plan) ---\n");

    var (plannerRaw, perExecutor) = await InterviewWorkflowRunner.RunPlanWorkflowAsync(
        ingestionAgent, seniorityAgent, plannerAgent, input);

    // Reformat the last executor's raw output into a typed InterviewPlan.
    (plan, _) = await JsonAgentRunner.RunJsonAsync<InterviewPlan>(
        plannerAgent,
        $"Reformat this EXACT content as a single valid InterviewPlan JSON (no markdown):\n\n{plannerRaw}");

    // Workflow captures ingestion + seniority internally; use placeholders here
    // so the shared human-in-the-loop and evaluation steps below still compile.
    profile   = new ResumeProfile        { CandidateName = "(captured in workflow output)" };
    seniority = new SeniorityAssessment  { Level = plan.Level, Confidence = 0.8,
                                           Rationale = "(captured in workflow output)" };

    Console.WriteLine("--- Per-executor streamed output ---");
    foreach (var kvp in perExecutor)
        Console.WriteLine($"\n[{kvp.Key}]\n{kvp.Value}\n");
}
// =============================================================================
// Demo 2 — Sequential Multi-Agent Pipeline (the baseline we built up to here)
// =============================================================================
else
{
    // ---- Step 1: Resume Ingestion ----
    Console.WriteLine("--- Step 1: Resume Ingestion ---\n");
    var ingestPrompt = $"{AgentPrompts.ResumeIngestion}\n\nRESUME:\n{resumeText}";
    (profile, _) = await JsonAgentRunner.RunJsonAsync<ResumeProfile>(ingestionAgent, ingestPrompt);

    Console.WriteLine($"Candidate : {profile.CandidateName}");
    Console.WriteLine($"Experience: {profile.YearsExperience} years");
    Console.WriteLine($"Skills    : {string.Join(", ", profile.CoreSkills.Take(8))}");
    Console.WriteLine($"Red Flags : {string.Join(", ", profile.RedFlags)}");

    // ---- Step 2: Seniority Classification ----
    Console.WriteLine("\n--- Step 2: Seniority Classification ---\n");
    var seniorityPrompt = $"{AgentPrompts.SeniorityClassifier}\n\nRESUME_PROFILE:\n{System.Text.Json.JsonSerializer.Serialize(profile)}";
    (seniority, _) = await JsonAgentRunner.RunJsonAsync<SeniorityAssessment>(seniorityAgent, seniorityPrompt);

    Console.WriteLine($"Level     : {seniority.Level}  (confidence {seniority.Confidence:0.00})");
    Console.WriteLine($"Rationale : {seniority.Rationale}");

    // ---- Step 3: Interview Planning ----
    Console.WriteLine("\n--- Step 3: Interview Planning ---\n");
    var planPrompt = new StringBuilder()
        .AppendLine(AgentPrompts.InterviewPlanner)
        .AppendLine()
        .AppendLine("ROLE:").AppendLine(role)
        .AppendLine()
        .AppendLine("RESUME_PROFILE:").AppendLine(System.Text.Json.JsonSerializer.Serialize(profile))
        .AppendLine()
        .AppendLine("SENIORITY:").AppendLine(System.Text.Json.JsonSerializer.Serialize(seniority))
        .ToString();

    (plan, _) = await JsonAgentRunner.RunJsonAsync<InterviewPlan>(plannerAgent, planPrompt);
}

// =============================================================================
// Shared — Human-in-the-Loop Checkpoint (Demo 2 showstopper, kept in Demo 3)
// =============================================================================
Console.WriteLine("\n=== Draft Interview Plan ===\n");
Console.WriteLine($"Role: {plan.Role} | Level: {plan.Level}\n");
Console.WriteLine(plan.Summary);
Console.WriteLine();

foreach (var round in plan.Rounds)
{
    Console.WriteLine($"- {round.Name} ({round.DurationMinutes} min)");
    foreach (var q in round.Questions.Take(4)) Console.WriteLine($"  • {q}");
    if (round.Questions.Count > 4) Console.WriteLine("  • ...");
    Console.WriteLine();
}

Console.Write("Approve this plan? (y/n): ");
var approved = (Console.ReadLine() ?? "").Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);

if (!approved)
{
    Console.Write("Give feedback in one sentence (e.g., 'more system design, fewer trivia'): ");
    var feedback = Console.ReadLine() ?? "";

    var revisePrompt = $@"Revise the InterviewPlan JSON below based on this feedback.
Feedback: {feedback}

Return ONLY valid InterviewPlan JSON.

{System.Text.Json.JsonSerializer.Serialize(plan)}";

    (plan, _) = await JsonAgentRunner.RunJsonAsync<InterviewPlan>(plannerAgent, revisePrompt);
    Console.WriteLine("\n=== Revised Plan ===\n");
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(plan, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}

// =============================================================================
// Shared — Step 4: Evaluation (simulate interview notes)
// =============================================================================
Console.WriteLine("\n=== Step 4: Evaluation (simulate interview notes) ===\n");
Console.WriteLine("Type a few bullet notes about the candidate's performance, then enter an empty line:");

var notesSb = new StringBuilder();
while (true)
{
    var line = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(line)) break;
    notesSb.AppendLine($"- {line}");
}

var notes = notesSb.Length == 0
    ? "- (no notes provided; evaluate based on resume + plan only)"
    : notesSb.ToString();

var evalPrompt = new StringBuilder()
    .AppendLine(AgentPrompts.Evaluator)
    .AppendLine()
    .AppendLine("RESUME_PROFILE:").AppendLine(System.Text.Json.JsonSerializer.Serialize(profile))
    .AppendLine()
    .AppendLine("INTERVIEW_PLAN:").AppendLine(System.Text.Json.JsonSerializer.Serialize(plan))
    .AppendLine()
    .AppendLine("INTERVIEW_NOTES:").AppendLine(notes)
    .ToString();

var (evaluation, _) = await JsonAgentRunner.RunJsonAsync<EvaluationResult>(evaluatorAgent, evalPrompt);

Console.WriteLine("\n=== Result ===\n");
Console.WriteLine($"Score          : {evaluation.OverallScore}/10");
Console.WriteLine($"Recommendation : {evaluation.Recommendation}\n");
Console.WriteLine(evaluation.Summary);

Console.WriteLine("\nStrengths:");
foreach (var s in evaluation.Strengths) Console.WriteLine($"  • {s}");

Console.WriteLine("\nRisks:");
foreach (var r in evaluation.Risks) Console.WriteLine($"  • {r}");

Console.WriteLine("\nFollow-ups:");
foreach (var f in evaluation.FollowUps) Console.WriteLine($"  • {f}");
