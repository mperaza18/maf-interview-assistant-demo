# Tech Talk Demo Plan: Microsoft Agent Framework — Progressive Demos

## Structure Overview

```
Demo 1 ──► Demo 2 ──► Demo 3
Single     Multi-Agent  Workflow
Agent      Pipeline +   Orchestration
           Human-in-    (~15 min)
           the-Loop
(~15 min)  (~20 min)
```

The codebase is progressively **stripped down** for Demo 1 and **built back up** through Demo 2 and
Demo 3 to reach the final state in `main`.

---

## Time Budget

| Segment | Time |
|---|---|
| Intro: What is MAF? (slides) | 5 min |
| Demo 1: Single Agent | 15 min |
| Demo 2: Multi-Agent + Human-in-the-Loop | 20 min |
| Demo 3: Workflow Orchestration | 15 min |
| Q&A | 5 min |
| **Total** | **~60 min** |

---

## Branch Strategy

```
main                  ← Demo 3 (final state, current codebase)
demo/2-multi-agent    ← Demo 2 starting point (no WorkflowRunner)
demo/1-single-agent   ← Demo 1 starting point (single agent only)
```

Prepare three Git branches so you can `git checkout demo/1-single-agent` at the start of the
talk and build forward. This avoids live-deleting code in front of the audience.

---

## Demo 1 — "Your First AI Agent" (~15 min)

### Goal

Show the absolute minimum to create a working AI Agent with MAF and get a **structured JSON
response** from it.

### Key Concepts Introduced

- What is `AIAgent`?
- `AzureOpenAIClient` → `.GetChatClient()` → `.AsAIAgent()`
- `JsonAgentRunner.RunJsonAsync<T>` — forcing structured output
- System prompts as agent identity

### Starting State of `Program.cs`

Strip `Program.cs` down to only the `ResumeIngestion` agent:

```csharp
// Demo 1 — Single Agent: Resume Ingestion

var resumeText = await File.ReadAllTextAsync(Path.Combine("assets", "resumes", "jane_doe.txt"));

AIAgent ingestionAgent = AgentFactory.CreateAzureOpenAIAgent("ResumeIngestion", AgentPrompts.ResumeIngestion);

Console.WriteLine("=== Demo 1: Single Agent ===\n");

var ingestPrompt = $"{AgentPrompts.ResumeIngestion}\n\nRESUME:\n{resumeText}";
var (profile, _) = await JsonAgentRunner.RunJsonAsync<ResumeProfile>(ingestionAgent, ingestPrompt);

Console.WriteLine($"Candidate : {profile.CandidateName}");
Console.WriteLine($"Experience: {profile.YearsExperience} years");
Console.WriteLine($"Skills    : {string.Join(", ", profile.CoreSkills.Take(8))}");
Console.WriteLine($"Red Flags : {string.Join(", ", profile.RedFlags)}");
```

### Files in Scope

| File | Demo Role |
|---|---|
| `Agents/AgentFactory.cs` | Walk through `AsAIAgent()` — the MAF entry point |
| `Agents/AgentPrompts.cs` | Show only the `ResumeIngestion` prompt |
| `Models/ResumeProfile.cs` | Strongly-typed output POCO |
| `Agents/JsonAgentRunner.cs` | Explain why it enforces valid JSON |

### Talking Points

1. **"An Agent = an LLM + an identity (system prompt)"** — no tools, no memory yet
2. Walk through `AgentFactory`: `AzureKeyCredential` vs `AzureCliCredential` — zero-config auth
3. Show the raw prompt, then show the typed `ResumeProfile` — contrast with a raw `ChatClient` call
4. *"This is your foundation — one agent, one responsibility"*

---

## Demo 2 — "Multi-Agent Collaboration + Human-in-the-Loop" (~20 min)

### Goal

Show how **specialized agents** hand off structured data to each other, and how to insert a
**human checkpoint** between steps.

### Key Concepts Introduced

- Separation of concerns — one agent per responsibility
- Chaining agents by passing the previous output as the next input
- Human-in-the-loop: pause → review → approve or revise
- Iterative refinement loop (planner feedback)

### What to Add on Top of Demo 1

**Step 1** — add `seniorityAgent` and chain it from the `ResumeProfile`:

```csharp
// Demo 2 — Step 2: Seniority Classification
AIAgent seniorityAgent = AgentFactory.CreateAzureOpenAIAgent("SeniorityClassifier", AgentPrompts.SeniorityClassifier);

Console.WriteLine("\n--- Step 2: Seniority Classification ---\n");
var seniorityPrompt = $"{AgentPrompts.SeniorityClassifier}\n\nRESUME_PROFILE:\n{System.Text.Json.JsonSerializer.Serialize(profile)}";
var (seniority, _) = await JsonAgentRunner.RunJsonAsync<SeniorityAssessment>(seniorityAgent, seniorityPrompt);

Console.WriteLine($"Level     : {seniority.Level}  (confidence {seniority.Confidence:0.00})");
Console.WriteLine($"Rationale : {seniority.Rationale}");
```

**Step 2** — add `plannerAgent` and the human approval loop (the **demo showstopper**):

```csharp
// Demo 2 — Step 3: Interview Planning + Human-in-the-Loop
AIAgent plannerAgent = AgentFactory.CreateAzureOpenAIAgent("InterviewPlanner", AgentPrompts.InterviewPlanner);

Console.WriteLine("\n--- Step 3: Interview Planning ---\n");
var planPrompt = new StringBuilder()
    .AppendLine("ROLE:").AppendLine(role)
    .AppendLine("RESUME_PROFILE:").AppendLine(System.Text.Json.JsonSerializer.Serialize(profile))
    .AppendLine("SENIORITY:").AppendLine(System.Text.Json.JsonSerializer.Serialize(seniority))
    .ToString();

var (plan, _) = await JsonAgentRunner.RunJsonAsync<InterviewPlan>(plannerAgent, planPrompt);

// --- Human-in-the-Loop Checkpoint ---
Console.WriteLine($"\nDraft plan: {plan.Summary}\n");
Console.Write("Approve this plan? (y/n): ");
var approved = (Console.ReadLine() ?? "").Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);

if (!approved)
{
    Console.Write("Feedback: ");
    var feedback = Console.ReadLine() ?? "";
    var revisePrompt = $"Revise based on: {feedback}\n\nReturn ONLY valid InterviewPlan JSON:\n{System.Text.Json.JsonSerializer.Serialize(plan)}";
    (plan, _) = await JsonAgentRunner.RunJsonAsync<InterviewPlan>(plannerAgent, revisePrompt);
}
```

**Step 3** — add `evaluatorAgent` as the closing stage (already present in the full `Program.cs`).

### New Files Brought In

| File | Demo Role |
|---|---|
| `Models/SeniorityAssessment.cs` | Second typed output — agent hand-off |
| `Models/InterviewPlan.cs` | Third typed output — the artifact under review |
| `Models/EvaluationResult.cs` | Fourth typed output — closing the loop |
| `Agents/AgentPrompts.cs` | Reveal remaining prompts one by one |

### Talking Points

1. **"Each agent only knows its job"** — the seniority agent never sees the planner prompt
2. The JSON output of agent N becomes the structured *input* of agent N+1 — type-safe hand-off
3. **Human-in-the-loop is just a `Console.ReadLine()`** — but in production this is a Teams
   Adaptive Card, a web form, or an API approval gate
4. Highlight the **revision loop**: the planner receives its own previous output + new feedback;
   demonstrate live by entering *"more system design questions"*

---

## Demo 3 — "Workflow Orchestration" (~15 min)

### Goal

Replace the manual, imperative agent chaining from Demo 2 with a **declarative orchestration
graph** using MAF Workflows. Show streaming output per executor.

### Key Concepts Introduced

- `WorkflowBuilder` — declare a graph with edges instead of sequential `await` calls
- `InProcessExecution.StreamAsync` — streaming workflow execution
- `TurnToken` — trigger mechanism; the workflow is lazy until you send it
- `WatchStreamAsync` + `AgentResponseUpdateEvent` — per-agent streaming output
- Why workflows over manual chaining? (retries, observability, branching, checkpoints)

### What to Add — `InterviewWorkflowRunner.cs`

Walk the audience through this file **live** — it is concise enough to read in full:

```csharp
// Demo 3 — The entire pipeline is declared as an explicit graph
var workflow = new WorkflowBuilder(resumeIngestionAgent)
    .AddEdge(resumeIngestionAgent, seniorityAgent)
    .AddEdge(seniorityAgent, plannerAgent)
    .Build();
```

Then show the streaming consumption loop and contrast it with Demo 2's step-by-step `await` calls.

### `Program.cs` Change — Swap the Else Branch for `--mode workflow`

```csharp
// Demo 3 — Replace manual steps 1-3 with a single workflow call
var input = new ChatMessage(ChatRole.User,
    $"Target role: {role}\n\nRESUME:\n{resumeText}\n\n" +
    "Extract ResumeProfile JSON, classify seniority, then produce an InterviewPlan JSON.");

var (plannerRaw, perExecutor) = await InterviewWorkflowRunner.RunPlanWorkflowAsync(
    ingestionAgent, seniorityAgent, plannerAgent, input);

Console.WriteLine("\n--- Per-executor streamed output ---");
foreach (var (executorId, output) in perExecutor)
    Console.WriteLine($"\n[{executorId}]\n{output}");
```

Run it:

```bash
dotnet run --mode workflow --resume ../../assets/resumes/jane_doe.txt
```

### Talking Points

1. **"Three lines to define the graph"** — `WorkflowBuilder` replaces ~20 lines of manual chaining
2. `StreamAsync` vs awaiting each agent — show tokens streaming in per executor in the terminal
3. `TurnToken` — the workflow is **lazy by design**; nothing runs until you send the trigger
4. *"Human-in-the-loop gates, branching edges, retry policies — all composable on top of this
   graph"*
5. Close the loop: the Evaluator step still runs outside the workflow — emphasise that **not
   everything needs orchestration**, only the steps that benefit from it

---

## Data Flow per Demo

### Demo 1
```
Resume Text ──► [ResumeIngestion Agent] ──► ResumeProfile (JSON)
```

### Demo 2
```
Resume Text ──► [ResumeIngestion] ──► ResumeProfile
                                           │
                                           ▼
                                [SeniorityClassifier] ──► SeniorityAssessment
                                                               │
                                                               ▼
                                                    [InterviewPlanner] ──► InterviewPlan
                                                                                │
                                                                      ┌─────────┴─────────┐
                                                                   Approved           Rejected
                                                                      │                   │
                                                                      │          [Planner revises]
                                                                      └─────────┬─────────┘
                                                                                ▼
                                                                     [Evaluator] ──► EvaluationResult
```

### Demo 3
```
Single ChatMessage input
         │
         ▼
WorkflowBuilder graph (streaming)
  [ResumeIngestion] ──edge──► [SeniorityClassifier] ──edge──► [InterviewPlanner]
         │                            │                               │
    (streamed)                   (streamed)                      (streamed)
                                                                       │
                                                          (reformat to InterviewPlan JSON)
                                                                       │
                                                            [Human Approval Gate]
                                                                       │
                                                             [Evaluator] (outside workflow)
```

---

## Related Files

| File | Description |
|---|---|
| `src/InterviewAssistant/Program.cs` | Main entry point — evolves across all three demos |
| `src/InterviewAssistant/Agents/AgentFactory.cs` | Agent construction — shown in Demo 1 |
| `src/InterviewAssistant/Agents/AgentPrompts.cs` | System prompts — revealed incrementally |
| `src/InterviewAssistant/Agents/JsonAgentRunner.cs` | Structured output helper — shown in Demo 1 |
| `src/InterviewAssistant/Models/` | POCOs — one per agent output |
| `src/InterviewAssistant/Workflows/InterviewWorkflowRunner.cs` | Workflow graph — introduced in Demo 3 |

## Related Documentation

- [EXPLANATION_PROGRAM.md](../EXPLANATION_PROGRAM.md) — Full walkthrough of `Program.cs`
- [Agents.md](../Agents.md) — Deep dive into agent architecture and prompts
- [README.md](../README.md) — Setup and quick-start guide
