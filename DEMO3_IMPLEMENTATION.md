# Demo 3 — Workflow Orchestration: Implementation Analysis

> **Repository:** `maf-interview-assistant-demo`
> **Branch:** `main`
> **Framework:** Microsoft Agent Framework (MAF) · .NET 8 · C# 12

---

## 1. Is the Workflow Orchestration Pipeline Implemented?

**Yes — fully implemented.**

Demo 3 extends the Demo 2 sequential pipeline by adding a second execution path selectable at runtime
via the `--mode workflow` flag. The orchestration is handled by
`InterviewWorkflowRunner` (`src/InterviewAssistant/Workflows/InterviewWorkflowRunner.cs`),
which uses the MAF `WorkflowBuilder` API to declare a directed agent graph and run it
as a single streaming execution unit.

Both modes — `simple` (Demo 2 baseline) and `workflow` (Demo 3) — share the same
human-in-the-loop checkpoint and the evaluator step that follow planning.

---

## 2. AI Agent Design Pattern

### Primary Pattern — Sequential Multi-Agent Pipeline

The dominant pattern across both modes is a **Sequential Pipeline** (also called a *Chain of
Agents*): each agent's output becomes the context/input for the next agent in a fixed linear order.
There is no branching, no parallel execution, and no single orchestrator directing sub-agents via
tool calls (which would characterise a hierarchical pattern).

```
[Resume File]
      │
      ▼
┌─────────────────────┐
│  ResumeIngestion    │  → extracts ResumeProfile JSON
└─────────────────────┘
      │
      ▼
┌─────────────────────┐
│  SeniorityClassifier│  → classifies level + confidence
└─────────────────────┘
      │
      ▼
┌─────────────────────┐
│  InterviewPlanner   │  → produces InterviewPlan JSON
└─────────────────────┘
      │
      ▼
  [Human Checkpoint]   ← approves or provides feedback (human-in-the-loop)
      │
      ▼
┌─────────────────────┐
│  Evaluator          │  → scores candidate + issues recommendation
└─────────────────────┘
```

### Secondary Pattern — Human-in-the-Loop (HITL)

After the planning stage, execution **pauses** for human review. The user either approves the plan
or submits free-text feedback that triggers a plan revision by the `InterviewPlanner` agent before
evaluation proceeds. This HITL checkpoint was introduced in Demo 2 and is preserved unchanged in
Demo 3.

### Demo 3 Addition — Workflow Graph Orchestration

In `--mode workflow`, the three planning agents are wired into a MAF `WorkflowBuilder` **directed
acyclic graph (DAG)** instead of being called imperatively. The graph is still linear (no branches),
but the framework now owns the scheduling, streaming, and event dispatch — decoupling the
orchestration logic from `Program.cs`.

---

## 3. Step-by-Step Process

### Mode: `simple` (Demo 2 sequential baseline)

#### Step 0 — Bootstrap

```csharp
// Program.cs — Agent creation
AIAgent ingestionAgent = AgentFactory.CreateAzureOpenAIAgent(
    "ResumeIngestion", AgentPrompts.ResumeIngestion);
AIAgent seniorityAgent = AgentFactory.CreateAzureOpenAIAgent(
    "SeniorityClassifier", AgentPrompts.SeniorityClassifier);
AIAgent plannerAgent   = AgentFactory.CreateAzureOpenAIAgent(
    "InterviewPlanner", AgentPrompts.InterviewPlanner);
AIAgent evaluatorAgent = AgentFactory.CreateAzureOpenAIAgent(
    "Evaluator", AgentPrompts.Evaluator);
```

`AgentFactory.CreateAzureOpenAIAgent` authenticates with Azure OpenAI (API key or Azure CLI
credential), wraps the chat client as an `AIAgent`, and attaches a system-level instruction prompt.

```csharp
// AgentFactory.cs
return client
    .GetChatClient(deployment)
    .AsAIAgent(instructions: instructions, name: name);
```

---

#### Step 1 — Resume Ingestion

The raw resume text is appended to the agent's system prompt and sent to the
`ResumeIngestion` agent, which returns a strongly typed `ResumeProfile` JSON.

```csharp
var ingestPrompt = $"{AgentPrompts.ResumeIngestion}\n\nRESUME:\n{resumeText}";
(profile, _) = await JsonAgentRunner.RunJsonAsync<ResumeProfile>(ingestionAgent, ingestPrompt);

// Output: profile.CandidateName, profile.YearsExperience, profile.CoreSkills, profile.RedFlags
```

`JsonAgentRunner.RunJsonAsync<T>` calls `agent.RunAsync(prompt)`, trims the response, and
deserialises it into the target model — throwing a descriptive exception on schema mismatch.

---

#### Step 2 — Seniority Classification

The `ResumeProfile` JSON produced in Step 1 is serialised and passed to the
`SeniorityClassifier` agent.

```csharp
var seniorityPrompt = $"{AgentPrompts.SeniorityClassifier}\n\nRESUME_PROFILE:\n"
                    + JsonSerializer.Serialize(profile);
(seniority, _) = await JsonAgentRunner.RunJsonAsync<SeniorityAssessment>(seniorityAgent, seniorityPrompt);

// Output: seniority.Level (Junior/Mid/Senior/Staff+), seniority.Confidence, seniority.Rationale
```

---

#### Step 3 — Interview Planning

The `InterviewPlanner` agent receives the target role, the full `ResumeProfile`, and the
`SeniorityAssessment` to compose a structured `InterviewPlan`.

```csharp
var planPrompt = new StringBuilder()
    .AppendLine(AgentPrompts.InterviewPlanner)
    .AppendLine("ROLE:").AppendLine(role)
    .AppendLine("RESUME_PROFILE:").AppendLine(JsonSerializer.Serialize(profile))
    .AppendLine("SENIORITY:").AppendLine(JsonSerializer.Serialize(seniority))
    .ToString();

(plan, _) = await JsonAgentRunner.RunJsonAsync<InterviewPlan>(plannerAgent, planPrompt);

// Output: plan.Role, plan.Level, plan.Summary, plan.Rounds[], plan.Rubric[]
```

---

### Mode: `workflow` (Demo 3 — WorkflowBuilder graph)

#### Step 3-W — Build & Execute the Workflow Graph

The three planning agents are registered as **nodes** in a `WorkflowBuilder` graph.
Edges declare the execution order; `Build()` compiles the graph.

```csharp
// InterviewWorkflowRunner.cs
var workflow = new WorkflowBuilder(resumeIngestionAgent)
    .AddEdge(resumeIngestionAgent, seniorityAgent)
    .AddEdge(seniorityAgent, plannerAgent)
    .Build();
```

A single `ChatMessage` carries all context (role + resume text + instruction).
`InProcessExecution.StreamAsync` starts an in-process streaming run, and a
`TurnToken` triggers agent execution.

```csharp
var input = new ChatMessage(ChatRole.User,
    $"Target role: {role}\n\nRESUME:\n{resumeText}\n\n" +
    "First extract a ResumeProfile JSON, then classify seniority, then produce an InterviewPlan JSON.");

await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, input);
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
```

Streamed output is captured per executor using `AgentResponseUpdateEvent`:

```csharp
await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    if (evt is AgentResponseUpdateEvent update)
    {
        if (!buffers.TryGetValue(update.ExecutorId, out var sb))
            buffers[update.ExecutorId] = sb = new StringBuilder();
        sb.Append(update.Data);
    }
}
```

The last executor's buffer holds the planner's raw output.  A second `RunJsonAsync` call
reformats it into a typed `InterviewPlan`:

```csharp
(plan, _) = await JsonAgentRunner.RunJsonAsync<InterviewPlan>(
    plannerAgent,
    $"Reformat this EXACT content as a single valid InterviewPlan JSON (no markdown):\n\n{plannerRaw}");
```

---

### Shared — Human-in-the-Loop Checkpoint (both modes)

After planning, execution halts to show the draft plan and prompt for approval.
If the user rejects it, free-text feedback is collected and the planner agent revises the plan
before evaluation begins.

```csharp
Console.Write("Approve this plan? (y/n): ");
var approved = (Console.ReadLine() ?? "").Trim()
               .StartsWith("y", StringComparison.OrdinalIgnoreCase);

if (!approved)
{
    Console.Write("Give feedback in one sentence: ");
    var feedback = Console.ReadLine() ?? "";

    var revisePrompt = $"""
        Revise the InterviewPlan JSON below based on this feedback.
        Feedback: {feedback}

        Return ONLY valid InterviewPlan JSON.

        {JsonSerializer.Serialize(plan)}
        """;

    (plan, _) = await JsonAgentRunner.RunJsonAsync<InterviewPlan>(plannerAgent, revisePrompt);
}
```

---

### Step 4 — Evaluation (both modes)

Free-text interviewer notes (or a default placeholder) are appended to the agent context.
The `Evaluator` agent returns a scored `EvaluationResult`.

```csharp
var evalPrompt = new StringBuilder()
    .AppendLine(AgentPrompts.Evaluator)
    .AppendLine("RESUME_PROFILE:").AppendLine(JsonSerializer.Serialize(profile))
    .AppendLine("INTERVIEW_PLAN:").AppendLine(JsonSerializer.Serialize(plan))
    .AppendLine("INTERVIEW_NOTES:").AppendLine(notes)
    .ToString();

var (evaluation, _) = await JsonAgentRunner.RunJsonAsync<EvaluationResult>(evaluatorAgent, evalPrompt);

// Output: evaluation.OverallScore (1-10), evaluation.Recommendation,
//         evaluation.Strengths[], evaluation.Risks[], evaluation.FollowUps[]
```

---

## 4. File Map

| File | Role |
|------|------|
| `Program.cs` | Entry point; mode switch; shared HITL + evaluation |
| `Agents/AgentFactory.cs` | Creates `AIAgent` instances backed by Azure OpenAI |
| `Agents/AgentPrompts.cs` | System prompts for all four agents |
| `Agents/JsonAgentRunner.cs` | Calls an agent and deserialises its JSON response |
| `Workflows/InterviewWorkflowRunner.cs` | `WorkflowBuilder` graph + streaming execution (Demo 3) |
| `Models/ResumeProfile.cs` | Output model for Step 1 |
| `Models/SeniorityAssessment.cs` | Output model for Step 2 |
| `Models/InterviewPlan.cs` | Output model for Step 3 |
| `Models/EvaluationResult.cs` | Output model for Step 4 |

---

## 5. Summary

| Dimension | Value |
|-----------|-------|
| **Orchestration pattern** | Sequential Multi-Agent Pipeline |
| **Demo 3 addition** | MAF `WorkflowBuilder` graph replaces imperative sequential calls |
| **Human involvement** | Human-in-the-Loop approval / feedback checkpoint between planning and evaluation |
| **Agent communication** | Output of each agent is serialised as JSON and injected into the next agent's prompt |
| **Streaming** | Demo 3 workflow mode captures per-executor streaming tokens via `AgentResponseUpdateEvent` |
| **Backend** | Azure OpenAI Chat Completion (GPT-4 class model) via `Microsoft.Agents.AI` |
