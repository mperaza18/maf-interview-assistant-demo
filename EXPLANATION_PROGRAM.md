# Program.cs — Detailed Explanation

This document explains the `Program.cs` file on the **`main` branch**, which is the **Demo 3 final state**
of the AI-powered Interview Assistant built with the Microsoft Agent Framework.

The file is the culmination of a three-step progressive demo:

| Branch | Demo | What it shows |
|---|---|---|
| `demo/1-single-agent` | Demo 1 | Single agent, structured JSON output |
| `demo/2-multi-agent` | Demo 2 | Multi-agent pipeline + human-in-the-loop |
| `main` | **Demo 3** | Workflow orchestration via `WorkflowBuilder` |

---

## Overview

`Program.cs` is a top-level-statements console application.  
It accepts a `--mode` flag that lets the audience see **both** the Demo 2 sequential pipeline and the
Demo 3 orchestrated workflow without switching branches:

```
--mode simple    (default)  ← Demo 2 baseline: four sequential await calls
--mode workflow             ← Demo 3 new:      WorkflowBuilder graph + streaming
```

Regardless of mode, the **human-in-the-loop checkpoint** and the **evaluation step** always run,
keeping the Demo 2 showstopper alive inside Demo 3.

---

## Data Flow

### Simple mode (`--mode simple`)

```
Resume Text
    │
    ▼
[Step 1] ResumeIngestion Agent    ──►  ResumeProfile (JSON)
                                            │
                                            ▼
[Step 2] SeniorityClassifier Agent ──►  SeniorityAssessment (JSON)
                                                │
                                                ▼
[Step 3] InterviewPlanner Agent    ──►  InterviewPlan (JSON)
                                             │
                               ┌─────────────┴─────────────┐
                            Approved                    Rejected
                               │                            │
                               │           [Planner revises with feedback]
                               └─────────────┬─────────────┘
                                             ▼
[Step 4] Evaluator Agent           ──►  EvaluationResult (JSON)
```

### Workflow mode (`--mode workflow`)

```
Single ChatMessage (role + resume text)
    │
    ▼
WorkflowBuilder graph  ──streaming──►  per-executor token buffers
  [ResumeIngestion] ─edge─► [SeniorityClassifier] ─edge─► [InterviewPlanner]
                                                                 │
                                          (reformat raw output to InterviewPlan JSON)
                                                                 │
                                               ┌────────────────┴────────────────┐
                                            Approved                         Rejected
                                               │                                 │
                                               │              [Planner revises with feedback]
                                               └────────────────┬────────────────┘
                                                                ▼
[Step 4] Evaluator Agent (outside the workflow) ──►  EvaluationResult (JSON)
```

---

## Step-by-Step Walkthrough

### 1. CLI arguments

Three named flags are parsed with the `GetArg` helper:

```csharp
static string? GetArg(string[] args, string name)
{
    var idx = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    if (idx >= 0 && idx < args.Length - 1) return args[idx + 1];
    return null;
}

var mode       = GetArg(args, "--mode")   ?? "simple"; // simple | workflow
var role       = GetArg(args, "--role")   ?? "Software Engineer";
var resumePath = GetArg(args, "--resume") ?? Path.Combine("assets", "resumes", "jane_doe.txt");
```

`--mode` is new in Demo 3 — it is the only addition to the argument surface compared to Demo 2.

---

### 2. Agent creation — all four agents, upfront

In Demo 2 each agent was declared immediately before its own step.  
Demo 3 moves all four declarations to the top so the `WorkflowBuilder` in the workflow branch can
reference any of them without scoping issues:

```csharp
AIAgent ingestionAgent = AgentFactory.CreateAzureOpenAIAgent("ResumeIngestion",     AgentPrompts.ResumeIngestion);
AIAgent seniorityAgent = AgentFactory.CreateAzureOpenAIAgent("SeniorityClassifier", AgentPrompts.SeniorityClassifier);
AIAgent plannerAgent   = AgentFactory.CreateAzureOpenAIAgent("InterviewPlanner",    AgentPrompts.InterviewPlanner);
AIAgent evaluatorAgent = AgentFactory.CreateAzureOpenAIAgent("Evaluator",           AgentPrompts.Evaluator);
```

`AgentFactory.CreateAzureOpenAIAgent` reads three environment variables:

| Variable | Purpose |
|---|---|
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI resource URL |
| `AZURE_OPENAI_DEPLOYMENT` | Model deployment name (e.g. `gpt-4o-mini`) |
| `AZURE_OPENAI_API_KEY` | API key — optional, falls back to `AzureCliCredential` |

---

### 3. Mode branching — `if (mode == "workflow") … else …`

The three variables produced by whichever branch is chosen:

```csharp
ResumeProfile        profile;
SeniorityAssessment  seniority;
InterviewPlan        plan;
```

are declared before the `if/else` and consumed by the shared sections below it — the compiler
enforces that both branches assign them before they are read.

---

#### 3a. Workflow branch — Demo 3 new addition

```csharp
if (mode.Equals("workflow", StringComparison.OrdinalIgnoreCase))
{
    var input = new ChatMessage(ChatRole.User,
        $"Target role: {role}\n\nRESUME:\n{resumeText}\n\n" +
        "First extract a ResumeProfile JSON, then classify seniority, then produce an InterviewPlan JSON.");

    var (plannerRaw, perExecutor) = await InterviewWorkflowRunner.RunPlanWorkflowAsync(
        ingestionAgent, seniorityAgent, plannerAgent, input);

    (plan, _) = await JsonAgentRunner.RunJsonAsync<InterviewPlan>(
        plannerAgent,
        $"Reformat this EXACT content as a single valid InterviewPlan JSON (no markdown):\n\n{plannerRaw}");

    profile   = new ResumeProfile       { CandidateName = "(captured in workflow output)" };
    seniority = new SeniorityAssessment { Level = plan.Level, Confidence = 0.8,
                                          Rationale = "(captured in workflow output)" };

    foreach (var kvp in perExecutor)
        Console.WriteLine($"\n[{kvp.Key}]\n{kvp.Value}\n");
}
```

Key points:

- **Single `ChatMessage`** — all context (role + resume text) travels in one message; the workflow
  graph routes it through each agent in declaration order.
- **`InterviewWorkflowRunner.RunPlanWorkflowAsync`** — defined in
  `Workflows/InterviewWorkflowRunner.cs`. Internally it builds the graph with three lines:
  ```csharp
  var workflow = new WorkflowBuilder(resumeIngestionAgent)
      .AddEdge(resumeIngestionAgent, seniorityAgent)
      .AddEdge(seniorityAgent, plannerAgent)
      .Build();
  ```
  then streams execution via `InProcessExecution.StreamAsync`, sends a `TurnToken` to trigger the
  agents, and collects `AgentResponseUpdateEvent` tokens per executor into string buffers.
- **Reformat step** — the raw planner output is passed back through `plannerAgent` as a
  normalisation call so the shared human-in-the-loop section always receives a valid
  `InterviewPlan` regardless of how the workflow formatted its response.
- **Placeholder profile / seniority** — ingestion and classification happen _inside_ the workflow.
  Placeholder objects satisfy the shared evaluation prompt that is assembled further down.
- **Per-executor debug output** — each agent's raw streamed response is printed, making the
  streaming nature of the workflow visible to the audience.

---

#### 3b. Simple / sequential branch — Demo 2 baseline

```csharp
else
{
    // Step 1: Resume Ingestion
    var ingestPrompt = $"{AgentPrompts.ResumeIngestion}\n\nRESUME:\n{resumeText}";
    (profile, _) = await JsonAgentRunner.RunJsonAsync<ResumeProfile>(ingestionAgent, ingestPrompt);

    // Step 2: Seniority Classification
    var seniorityPrompt = $"{AgentPrompts.SeniorityClassifier}\n\nRESUME_PROFILE:\n"
                        + System.Text.Json.JsonSerializer.Serialize(profile);
    (seniority, _) = await JsonAgentRunner.RunJsonAsync<SeniorityAssessment>(seniorityAgent, seniorityPrompt);

    // Step 3: Interview Planning
    var planPrompt = new StringBuilder()
        .AppendLine(AgentPrompts.InterviewPlanner)
        .AppendLine("ROLE:").AppendLine(role)
        .AppendLine("RESUME_PROFILE:").AppendLine(System.Text.Json.JsonSerializer.Serialize(profile))
        .AppendLine("SENIORITY:").AppendLine(System.Text.Json.JsonSerializer.Serialize(seniority))
        .ToString();
    (plan, _) = await JsonAgentRunner.RunJsonAsync<InterviewPlan>(plannerAgent, planPrompt);
}
```

The typed output of each step becomes the structured input to the next — the type-safe hand-off
pattern from Demo 2. `SeniorityClassifier` never sees the raw résumé text; it only receives the
already-structured `ResumeProfile`.

---

### 4. Human-in-the-Loop checkpoint — shared by both modes

After the `if/else`, `plan` is always populated. The same approval loop from Demo 2 runs regardless
of which branch produced the plan:

```csharp
Console.Write("Approve this plan? (y/n): ");
var approved = (Console.ReadLine() ?? "").Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);

if (!approved)
{
    Console.Write("Give feedback in one sentence: ");
    var feedback = Console.ReadLine() ?? "";

    var revisePrompt = $"Revise the InterviewPlan JSON below based on this feedback.\n"
                     + $"Feedback: {feedback}\n\nReturn ONLY valid InterviewPlan JSON.\n\n"
                     + System.Text.Json.JsonSerializer.Serialize(plan);

    (plan, _) = await JsonAgentRunner.RunJsonAsync<InterviewPlan>(plannerAgent, revisePrompt);
}
```

The revision prompt passes the planner its **own previous JSON output** plus the new feedback so it
refines what it already produced rather than starting from scratch. `plan` is reassigned in place so
Step 4 always works with the latest approved or revised version.

In production this `Console.ReadLine()` maps directly to a Teams Adaptive Card, a web form, or an
API approval gate.

---

### 5. Step 4 — Evaluation — shared by both modes

```csharp
var evalPrompt = new StringBuilder()
    .AppendLine(AgentPrompts.Evaluator)
    .AppendLine("RESUME_PROFILE:").AppendLine(System.Text.Json.JsonSerializer.Serialize(profile))
    .AppendLine("INTERVIEW_PLAN:").AppendLine(System.Text.Json.JsonSerializer.Serialize(plan))
    .AppendLine("INTERVIEW_NOTES:").AppendLine(notes)
    .ToString();

var (evaluation, _) = await JsonAgentRunner.RunJsonAsync<EvaluationResult>(evaluatorAgent, evalPrompt);
```

The evaluator is the only agent that sees the complete picture: résumé, plan, and live interview
notes. This illustrates how typed hand-offs **accumulate context** across a pipeline without any
single agent being aware of the others.

`notes` is collected via a `while (true)` / empty-line sentinel — the simplest possible multi-line
console input pattern with no extra libraries required.

---

## What Changed From Demo 2 to Demo 3

| Area | Demo 2 (`else` branch) | Demo 3 (`workflow` branch) |
|---|---|---|
| `--mode` arg | Not present | New: `simple` \| `workflow` |
| Agent declarations | Each agent declared before its own step | All four declared upfront |
| Steps 1–3 | Three sequential `await` calls | Single `InterviewWorkflowRunner.RunPlanWorkflowAsync` call |
| MAF types used | `AIAgent`, `JsonAgentRunner` | + `WorkflowBuilder`, `InProcessExecution`, `TurnToken`, `AgentResponseUpdateEvent` |
| Streaming | None | Per-executor token streaming printed to console |
| `using` added | — | `InterviewAssistant.Workflows`, `Microsoft.Extensions.AI` |
| Human-in-the-loop | ✅ | ✅ (shared, unchanged) |
| Evaluation step | ✅ | ✅ (shared, unchanged) |

---

## Key Types

| Type | Namespace | Role |
|---|---|---|
| `AIAgent` | `Microsoft.Agents.AI` | Core LLM-backed agent abstraction |
| `ChatMessage` | `Microsoft.Extensions.AI` | Input message for workflow entry point |
| `WorkflowBuilder` | `Microsoft.Agents.AI.Workflows` | Declares the directed agent graph |
| `InProcessExecution` | `Microsoft.Agents.AI.Workflows` | Executes the workflow in-process with streaming |
| `TurnToken` | `Microsoft.Agents.AI.Workflows` | Trigger sent to start lazy workflow execution |
| `AgentResponseUpdateEvent` | `Microsoft.Agents.AI.Workflows` | Streamed token event, one per executor chunk |
| `JsonAgentRunner` | `InterviewAssistant.Agents` | Runs an agent and deserialises its JSON response |
| `ResumeProfile` | `InterviewAssistant.Models` | Step 1 output POCO |
| `SeniorityAssessment` | `InterviewAssistant.Models` | Step 2 output POCO |
| `InterviewPlan` | `InterviewAssistant.Models` | Step 3 output POCO |
| `EvaluationResult` | `InterviewAssistant.Models` | Step 4 output POCO |

---

## Usage

```bash
cd src/InterviewAssistant

# Demo 2 mode — sequential pipeline (default)
dotnet run

# Demo 2 mode — custom role and resume
dotnet run -- --role "Staff Engineer" --resume ../../assets/resumes/jane_doe.txt

# Demo 3 mode — WorkflowBuilder graph
dotnet run -- --mode workflow

# Demo 3 mode — custom role and resume
dotnet run -- --mode workflow --role "Principal Engineer" --resume ../../assets/resumes/jane_doe.txt
```

---

## Repo Layout

```
src/InterviewAssistant/
  Program.cs                          ← CLI entry point (this file) — Demo 3 final state
  Program_demo2.cs                    ← Reference snapshot for demo/2-multi-agent branch
  Agents/
    AgentFactory.cs                   ← Creates AIAgent backed by Azure OpenAI
    AgentPrompts.cs                   ← System prompts for all four agents
    JsonAgentRunner.cs                ← Runs an agent and deserialises the JSON response
  Models/
    ResumeProfile.cs                  ← Step 1 output POCO
    SeniorityAssessment.cs            ← Step 2 output POCO
    InterviewPlan.cs                  ← Step 3 output POCO
    EvaluationResult.cs               ← Step 4 output POCO
  Workflows/
    InterviewWorkflowRunner.cs        ← WorkflowBuilder graph — introduced in Demo 3
assets/
  resumes/
    jane_doe.txt                      ← Sample résumé used across all demos
```

---

## Related Documentation

- [DEMO_PLAN_PROGRESSIVE.md](src/InterviewAssistant/DEMO_PLAN_PROGRESSIVE.md) — Full three-demo structure and talking points
- [Agents.md](Agents.md) — Deep dive into agent architecture and system prompts
- [README.md](README.md) — Setup and quick-start guide

---

*Based on the .NET 8 / C# 12 implementation using Microsoft Agent Framework — branch `main`.*
