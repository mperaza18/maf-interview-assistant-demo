# Demo 3 — Hierarchical Pattern: Technical Implementation Guide

> **Baseline:** Demo 3 workflow orchestration (main branch)
> **Pattern to add:** Hierarchical Multi-Agent (Orchestrator + Specialist sub-agents as tools)
> **Runtime flag:** `--mode hierarchical`
> **Framework:** Microsoft Agent Framework (MAF) · .NET 8 · C# 12

---

## Architecture Overview

In the **Sequential** and **Workflow** modes the developer hard-codes the execution order.
In the **Hierarchical** mode an **Orchestrator agent** — backed by an LLM — decides at runtime
which specialist sub-agent to invoke, with what input, and in what order.
Each specialist is registered as a callable **tool** (`AIFunction`) that the orchestrator can
invoke zero or more times.

```
User Input (resume + role + notes)
          │
          ▼
┌──────────────────────────────────┐
│        OrchestratorAgent         │  ← single LLM-driven entry point
│   (reasons about tool sequence)  │
└────────────┬─────────────────────┘
             │  tool calls (decided by LLM)
   ┌─────────┼──────────────┬────────────────┐
   ▼         ▼              ▼                ▼
[ingest_   [classify_    [plan_           [evaluate_
 resume]    seniority]    interview]       candidate]
ResumeI-   Seniority-    Interview-       Evaluator
ngestion   Classifier    Planner          (sub-agent)
(sub-agt)  (sub-agt)     (sub-agt)
                                    ▼
                             [human_review]   ← HITL as a registered tool
                             (console func)
```

### Key difference from the sequential/workflow patterns

| | Sequential (Demo 2) | Workflow (Demo 3) | Hierarchical |
|---|---|---|---|
| **Who decides the flow?** | Developer code | Developer graph | LLM (Orchestrator) |
| **Tool calls** | None | None | Sub-agents as `AIFunction` tools |
| **Adaptivity** | None | None | Can skip, retry or branch steps |
| **HITL** | Console loop in Program.cs | Console loop in Program.cs | `human_review` tool the LLM calls |
| **Entry point** | 3 explicit `await` calls | `WorkflowBuilder` graph | Single `orchestrator.RunAsync()` |

---

## Files Changed / Created

| Action | File |
|--------|------|
| **Modify** | `src/InterviewAssistant/Agents/AgentFactory.cs` |
| **Modify** | `src/InterviewAssistant/Agents/AgentPrompts.cs` |
| **Create** | `src/InterviewAssistant/Workflows/HierarchicalOrchestratorRunner.cs` |
| **Modify** | `src/InterviewAssistant/Program.cs` |

No new NuGet packages required — `AIFunction`, `AIFunctionFactory`, and `AIFunctionFactoryOptions`
are already provided by `Microsoft.Extensions.AI`, which is a transitive dependency of
`Microsoft.Agents.AI.OpenAI`.

---

## Step 1 — Update `AgentFactory` to Accept Tools

The current `CreateAzureOpenAIAgent` does not forward tools to `AsAIAgent`.
Add an optional `tools` parameter so the orchestrator can be created with all
specialist functions registered.

**File:** `src/InterviewAssistant/Agents/AgentFactory.cs`

```csharp
// Add IList<AITool> import at the top
using Microsoft.Extensions.AI;

public static class AgentFactory
{
    /// <summary>
    /// Creates an <see cref="AIAgent"/> backed by Azure OpenAI.
    /// Pass <paramref name="tools"/> to register specialist sub-agents
    /// as callable functions (hierarchical pattern).
    /// </summary>
    public static AIAgent CreateAzureOpenAIAgent(
        string name,
        string instructions,
        IList<AITool>? tools = null)   // ← new optional parameter
    {
        var endpoint   = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
                         ?? throw new InvalidOperationException("Missing AZURE_OPENAI_ENDPOINT env var");
        var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT")
                         ?? throw new InvalidOperationException("Missing AZURE_OPENAI_DEPLOYMENT env var");
        var apiKey     = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

        var credential = string.IsNullOrWhiteSpace(apiKey)
            ? (object)new AzureCliCredential()
            : new AzureKeyCredential(apiKey);

        var client = credential switch
        {
            AzureCliCredential c => new AzureOpenAIClient(new Uri(endpoint), c),
            AzureKeyCredential k => new AzureOpenAIClient(new Uri(endpoint), k),
            _                    => throw new InvalidOperationException("Unsupported credential")
        };

        // AsAIAgent overload: (IChatClient, name, instructions, modelId, tools, loggerFactory, serviceProvider)
        // modelId, loggerFactory and serviceProvider default to null.
        return client
            .GetChatClient(deployment)
            .AsAIAgent(name: name, instructions: instructions, tools: tools);
    }
}
```

> **Why:** `AsAIAgent` has an overload that accepts `IList<AITool>`. When the orchestrator agent
> is created with that list, the MAF framework automatically handles the full tool-call lifecycle:
> it detects when the LLM emits a function-call response, deserialises the arguments, invokes the
> matching `AIFunction`, and feeds the result back into the conversation — no manual loop needed.

---

## Step 2 — Add the Orchestrator System Prompt

The orchestrator's system prompt defines the **goal** and **tool contract** without hard-coding a
fixed sequence — the LLM reasons about order and conditions.

**File:** `src/InterviewAssistant/Agents/AgentPrompts.cs`

```csharp
public const string Orchestrator = @"
You are an interview orchestration agent. Your goal is to fully process a candidate
end-to-end for a given target role using the tools available to you.

Tools available:
- ingest_resume         : extract a structured ResumeProfile JSON from raw resume text.
- classify_seniority    : classify the candidate seniority from a ResumeProfile JSON.
- plan_interview        : produce an InterviewPlan JSON given the role, profile and seniority.
- human_review          : show the InterviewPlan to the hiring manager and get approval or feedback.
- evaluate_candidate    : score the candidate given the profile, plan and interview notes.

Required sequence:
1. Call ingest_resume with the full resume text.
2. Call classify_seniority with the ResumeProfile JSON returned in step 1.
3. Call plan_interview with the target role, ResumeProfile JSON and SeniorityAssessment JSON.
4. Call human_review with the InterviewPlan JSON from step 3.
   - If the response starts with 'APPROVED', proceed to step 5.
   - If the response starts with 'REJECTED:', revise the plan by calling plan_interview again
     incorporating the feedback, then call human_review again with the revised plan.
5. Call evaluate_candidate with the ResumeProfile JSON, the approved InterviewPlan JSON,
   and the interview notes provided by the user.

Output: Return ONLY the raw EvaluationResult JSON produced by evaluate_candidate.
        Do NOT wrap it in markdown or add any commentary.
";
```

> **Why:** The orchestrator prompt specifies intent and rules, not imperative steps.
> The LLM can deviate — for example, re-planning after rejected HITL feedback — without any
> conditional code in the host application.

---

## Step 3 — Create `HierarchicalOrchestratorRunner`

This is the new runner that:
1. Wraps each specialist `AIAgent` as an `AIFunction` tool using `.AsAIFunction()`
2. Creates a HITL console tool using `AIFunctionFactory.Create()`
3. Builds the orchestrator agent with all tools registered
4. Executes a single `RunAsync` call

**File:** `src/InterviewAssistant/Workflows/HierarchicalOrchestratorRunner.cs`

```csharp
using System.ComponentModel;
using System.Text.Json;
using InterviewAssistant.Agents;
using InterviewAssistant.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace InterviewAssistant.Workflows;

public static class HierarchicalOrchestratorRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true
    };

    /// <summary>
    /// Runs the full interview pipeline through a single LLM-driven OrchestratorAgent.
    /// Each specialist is registered as an AIFunction tool; the orchestrator decides
    /// when and how to call each one, including re-planning after HITL feedback.
    /// </summary>
    public static async Task<EvaluationResult> RunAsync(
        AIAgent ingestionAgent,
        AIAgent seniorityAgent,
        AIAgent plannerAgent,
        AIAgent evaluatorAgent,
        string  role,
        string  resumeText,
        string  notes,
        CancellationToken cancellationToken = default)
    {
        // ------------------------------------------------------------------
        // 1. Wrap each specialist AIAgent as an AIFunction (AITool subtype).
        //    AsAIFunction() is provided by Microsoft.Agents.AI.AIAgentExtensions.
        //    Each new invocation gets its own AgentSession (stateless per call).
        // ------------------------------------------------------------------
        AIFunction ingestFn = ingestionAgent.AsAIFunction(new AIFunctionFactoryOptions
        {
            Name        = "ingest_resume",
            Description = "Extracts a structured ResumeProfile JSON from raw resume text. " +
                          "Input: the full resume text as a plain string."
        });

        AIFunction seniorityFn = seniorityAgent.AsAIFunction(new AIFunctionFactoryOptions
        {
            Name        = "classify_seniority",
            Description = "Classifies candidate seniority level. " +
                          "Input: a ResumeProfile JSON string."
        });

        AIFunction plannerFn = plannerAgent.AsAIFunction(new AIFunctionFactoryOptions
        {
            Name        = "plan_interview",
            Description = "Produces an InterviewPlan JSON. " +
                          "Input: target role (string), ResumeProfile JSON, SeniorityAssessment JSON, " +
                          "and optional revision feedback (string)."
        });

        AIFunction evaluatorFn = evaluatorAgent.AsAIFunction(new AIFunctionFactoryOptions
        {
            Name        = "evaluate_candidate",
            Description = "Scores the candidate and issues a hire recommendation. " +
                          "Input: ResumeProfile JSON, InterviewPlan JSON, and free-text interview notes."
        });

        // ------------------------------------------------------------------
        // 2. Create the HITL console tool using AIFunctionFactory.Create().
        //    The LLM calls this tool with the InterviewPlan JSON; the function
        //    pauses execution, presents the plan to the human, and returns
        //    "APPROVED" or "REJECTED: <feedback>" back to the LLM.
        // ------------------------------------------------------------------
        AIFunction humanReviewFn = AIFunctionFactory.Create(
            ([Description("The InterviewPlan JSON to present for human approval.")] string planJson) =>
            {
                Console.WriteLine("\n=== [Orchestrator] Draft Interview Plan for Review ===\n");

                try
                {
                    var plan = JsonSerializer.Deserialize<InterviewPlan>(planJson, JsonOptions);
                    if (plan is not null)
                    {
                        Console.WriteLine($"Role: {plan.Role}  |  Level: {plan.Level}\n");
                        Console.WriteLine(plan.Summary);
                        Console.WriteLine();
                        foreach (var round in plan.Rounds)
                        {
                            Console.WriteLine($"- {round.Name} ({round.DurationMinutes} min)");
                            foreach (var q in round.Questions.Take(4))
                                Console.WriteLine($"  • {q}");
                            if (round.Questions.Count > 4) Console.WriteLine("  • ...");
                            Console.WriteLine();
                        }
                    }
                    else
                    {
                        Console.WriteLine(planJson);
                    }
                }
                catch
                {
                    // If JSON parsing fails, print raw content
                    Console.WriteLine(planJson);
                }

                Console.Write("Approve this plan? (y/n): ");
                var input = (Console.ReadLine() ?? "").Trim();

                if (input.StartsWith("y", StringComparison.OrdinalIgnoreCase))
                    return "APPROVED";

                Console.Write("Feedback (one sentence): ");
                var feedback = Console.ReadLine() ?? "no feedback provided";
                return $"REJECTED: {feedback}";
            },
            name: "human_review",
            description: "Presents the InterviewPlan to the human hiring manager for approval. " +
                         "Returns 'APPROVED' or 'REJECTED: <feedback>'.");

        // ------------------------------------------------------------------
        // 3. Build the OrchestratorAgent with all tools registered.
        //    MAF automatically handles the full tool-call loop:
        //    emit function call → invoke tool → feed result back → continue LLM.
        // ------------------------------------------------------------------
        AIAgent orchestrator = AgentFactory.CreateAzureOpenAIAgent(
            name:         "Orchestrator",
            instructions: AgentPrompts.Orchestrator,
            tools:        [ingestFn, seniorityFn, plannerFn, evaluatorFn, humanReviewFn]);

        // ------------------------------------------------------------------
        // 4. Single RunAsync call — the orchestrator drives the entire pipeline.
        //    The LLM decides the tool call sequence based on its system prompt.
        // ------------------------------------------------------------------
        var prompt = $"""
            Target role: {role}

            RESUME:
            {resumeText}

            INTERVIEW_NOTES:
            {(string.IsNullOrWhiteSpace(notes)
                ? "- (no notes provided; evaluate based on resume and plan only)"
                : notes)}

            Process this candidate end-to-end and return the final EvaluationResult JSON.
            """;

        Console.WriteLine("--- OrchestratorAgent taking control of the pipeline ---\n");

        var response = await orchestrator.RunAsync(prompt, cancellationToken: cancellationToken);
        var raw      = response.Text.Trim();

        return JsonSerializer.Deserialize<EvaluationResult>(raw, JsonOptions)
               ?? throw new InvalidOperationException(
                   $"Orchestrator returned non-JSON or schema mismatch.\nRaw:\n{raw}");
    }
}
```

### What happens inside `RunAsync` at runtime

```
orchestrator.RunAsync(prompt)
  │
  ├─► LLM reasons → emits tool_call: ingest_resume(resumeText)
  │       MAF invokes ingestionAgent.RunAsync(resumeText)
  │       Result (ResumeProfile JSON) fed back into conversation
  │
  ├─► LLM reasons → emits tool_call: classify_seniority(profileJson)
  │       MAF invokes seniorityAgent.RunAsync(profileJson)
  │       Result (SeniorityAssessment JSON) fed back
  │
  ├─► LLM reasons → emits tool_call: plan_interview(role, profileJson, seniorityJson)
  │       MAF invokes plannerAgent.RunAsync(combined prompt)
  │       Result (InterviewPlan JSON) fed back
  │
  ├─► LLM reasons → emits tool_call: human_review(planJson)
  │       MAF invokes humanReviewFn → pauses → console I/O → returns "APPROVED" or "REJECTED:..."
  │       Result fed back
  │
  │   [If REJECTED: LLM calls plan_interview again with feedback, then human_review again]
  │
  ├─► LLM reasons → emits tool_call: evaluate_candidate(profileJson, planJson, notes)
  │       MAF invokes evaluatorAgent.RunAsync(combined prompt)
  │       Result (EvaluationResult JSON) fed back
  │
  └─► LLM emits final text response: EvaluationResult JSON
        RunAsync returns AgentResult
```

> **MAF automatic tool-call loop:** When `AIAgent` is created with `tools`, the framework
> decorates the underlying `IChatClient` with a `FunctionInvocationDelegatingHandler`. This
> handler intercepts every LLM response, detects `tool_call` entries, invokes the matching
> `AIFunction`, and appends a `tool` role message before continuing the LLM turn — all
> transparently, without any loop in application code.

---

## Step 4 — Update `Program.cs`

Add a third `--mode hierarchical` branch. The HITL is no longer a manual console loop — it is
handled inside the orchestrator's tool invocation. The evaluator step is also absorbed by the
orchestrator, so the shared section only prints results.

**File:** `src/InterviewAssistant/Program.cs`

```csharp
// Add to existing using block at the top
using InterviewAssistant.Workflows;

// Add to mode comment on line 3:
// Run with --mode hierarchical    to use a single LLM-driven OrchestratorAgent.
```

```csharp
// ---- Collect interview notes up front for hierarchical mode ----
// (For simple/workflow modes this is collected later in the shared section)
string? earlyNotes = null;
if (mode.Equals("hierarchical", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Type interview notes now (empty line to finish), or press Enter to skip:");
    var sb = new StringBuilder();
    while (true)
    {
        var line = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(line)) break;
        sb.AppendLine($"- {line}");
    }
    earlyNotes = sb.Length == 0 ? "" : sb.ToString();
}
```

```csharp
// =============================================================================
// Demo 3 — Hierarchical Orchestration  (new branch)
// =============================================================================
else if (mode.Equals("hierarchical", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("--- OrchestratorAgent running (ingest → classify → plan → review → evaluate) ---\n");

    var evaluation = await HierarchicalOrchestratorRunner.RunAsync(
        ingestionAgent, seniorityAgent, plannerAgent, evaluatorAgent,
        role, resumeText, earlyNotes ?? "");

    Console.WriteLine("\n=== Result (via Orchestrator) ===\n");
    Console.WriteLine($"Score          : {evaluation.OverallScore}/10");
    Console.WriteLine($"Recommendation : {evaluation.Recommendation}\n");
    Console.WriteLine(evaluation.Summary);

    Console.WriteLine("\nStrengths:");
    foreach (var s in evaluation.Strengths) Console.WriteLine($"  • {s}");
    Console.WriteLine("\nRisks:");
    foreach (var r in evaluation.Risks) Console.WriteLine($"  • {r}");
    Console.WriteLine("\nFollow-ups:");
    foreach (var f in evaluation.FollowUps) Console.WriteLine($"  • {f}");

    return; // Hierarchical mode is self-contained; skip the shared sections below.
}
```

> **Why `return` early:** The shared sections (HITL console loop + evaluation step) already
> exist in `Program.cs` for `simple`/`workflow` modes. The hierarchical orchestrator absorbs
> both of those into its tool calls, so the shared code is skipped.

---

## Step 5 — Verify the Build

```powershell
dotnet build src\InterviewAssistant\InterviewAssistant.csproj
```

Expected: `Build succeeded` with 0 errors.

---

## Step 6 — Run and Test

```powershell
# Hierarchical mode — full end-to-end via OrchestratorAgent
dotnet run --project src\InterviewAssistant -- `
  --mode hierarchical `
  --role "Senior Software Engineer" `
  --resume assets\resumes\jane_doe.txt

# Compare against the sequential baseline
dotnet run --project src\InterviewAssistant -- --mode simple

# Compare against the workflow graph
dotnet run --project src\InterviewAssistant -- --mode workflow
```

When running in `hierarchical` mode, the console output will show the orchestrator calling
each tool in sequence:

```
=== Microsoft Agent Framework: AI Interview Assistant ===

Mode  : hierarchical
Role  : Senior Software Engineer
Resume: assets\resumes\jane_doe.txt

Type interview notes now (empty line to finish), or press Enter to skip:
> Strong system design answers
> Weak on distributed consensus
>

--- OrchestratorAgent running (ingest → classify → plan → review → evaluate) ---

[OrchestratorAgent calling: ingest_resume]
[OrchestratorAgent calling: classify_seniority]
[OrchestratorAgent calling: plan_interview]
[OrchestratorAgent calling: human_review]

=== [Orchestrator] Draft Interview Plan for Review ===
Role: Senior Software Engineer  |  Level: Senior
...
Approve this plan? (y/n): y

[OrchestratorAgent calling: evaluate_candidate]

=== Result (via Orchestrator) ===

Score          : 8/10
Recommendation : Lean Hire
...
```

---

## Appendix — Pattern Comparison Summary

```
--mode simple        →  Imperative sequential pipeline (Demo 2)
                        Developer owns every await + prompt assembly

--mode workflow      →  Declarative WorkflowBuilder graph (Demo 3)
                        Framework owns scheduling; developer declares edges

--mode hierarchical  →  LLM-driven OrchestratorAgent (this guide)
                        LLM owns routing; developer declares tools + intent
```

The hierarchical pattern trades **predictability** for **adaptability**.
Use it when the pipeline needs conditional branching, self-correction, or dynamic
composition — scenarios that would require hard-coded `if` statements in the other two modes.
