# Microsoft Agent Framework — AI Interview Assistant

A **hands-on** tech talk showcasing the **Microsoft Agent Framework (MAF)** — LLM-driven agents
that produce structured output from unstructured text, composed into a multi-agent pipeline and
orchestrated as a declarative workflow graph.

> **Branch `main`** — Demo 3 final state: `WorkflowBuilder` orchestration + streaming, built
> progressively on top of the Demo 2 multi-agent pipeline.

---

## Demo Progression

| Branch | Demo | What it introduces |
|---|---|---|
| `demo/1-single-agent` | Demo 1 (~15 min) | Single agent · `JsonAgentRunner` · structured JSON output |
| `demo/2-multi-agent` | Demo 2 (~20 min) | Four-agent pipeline · type-safe hand-offs · human-in-the-loop |
| **`main`** | **Demo 3 (~15 min)** | `WorkflowBuilder` graph · `InProcessExecution.StreamAsync` · `TurnToken` |

---

## 0) Prereqs

- **.NET 8 SDK+**
- **Azure OpenAI** endpoint + a deployed chat model (e.g., `gpt-4o-mini`)
- **Azure CLI** authenticated via `az login` **OR** an API key

---

## 1) Setup (packages)

From `src/InterviewAssistant`:

```bash
# Demo 1 & 2 — core agent packages
dotnet add package Azure.AI.OpenAI --prerelease
dotnet add package Azure.Identity
dotnet add package Microsoft.Agents.AI.OpenAI --prerelease

# Demo 3 — workflow orchestration (new)
dotnet add package Microsoft.Agents.AI.Workflows --prerelease
```

---

## 2) Configure environment

Set these environment variables before running:

```bash
# Required
$env:AZURE_OPENAI_ENDPOINT   = "https://your-resource.openai.azure.com/"
$env:AZURE_OPENAI_DEPLOYMENT = "gpt-4o-mini"

# Optional — omit to use Azure CLI / RBAC auth (az login)
$env:AZURE_OPENAI_API_KEY    = "your-api-key"
```

---

## 3) Run

From `src/InterviewAssistant`:

### Demo 2 mode — sequential pipeline (default)

```bash
dotnet run -- --resume ../../assets/resumes/jane_doe.txt --role "Software Engineer"
```

```
=== Microsoft Agent Framework: AI Interview Assistant ===

Mode  : simple
Role  : Software Engineer
Resume: ../../assets/resumes/jane_doe.txt

--- Step 1: Resume Ingestion ---

Candidate : Jane Doe
Experience: 6 years
Skills    : C#, .NET, Azure, Azure Service Bus, Cosmos DB, ...
Red Flags :

--- Step 2: Seniority Classification ---

Level     : Senior  (confidence 0.85)
Rationale : 6 years of relevant experience with strong Azure background.

--- Step 3: Interview Planning ---

=== Draft Interview Plan ===

Role: Software Engineer | Level: Senior
...

Approve this plan? (y/n): n
Give feedback in one sentence: more system design questions

=== Revised Plan ===
{ ... updated InterviewPlan JSON ... }

=== Step 4: Evaluation (simulate interview notes) ===

Type a few bullet notes about the candidate's performance, then enter an empty line:
> Strong system design answers
> Struggled with async patterns
>

=== Result ===

Score          : 7/10
Recommendation : Lean Hire
...
```

### Demo 3 mode — WorkflowBuilder orchestration

```bash
dotnet run -- --mode workflow --resume ../../assets/resumes/jane_doe.txt --role "Software Engineer"
```

```
=== Microsoft Agent Framework: AI Interview Assistant ===

Mode  : workflow
Role  : Software Engineer
Resume: ../../assets/resumes/jane_doe.txt

--- Running planning workflow (ingest → classify → plan) ---

--- Per-executor streamed output ---

[ResumeIngestion]
{ ... ResumeProfile JSON streamed token by token ... }

[SeniorityClassifier]
{ ... SeniorityAssessment JSON streamed token by token ... }

[InterviewPlanner]
{ ... InterviewPlan JSON streamed token by token ... }

=== Draft Interview Plan ===

Role: Software Engineer | Level: Senior
...

Approve this plan? (y/n): y

=== Step 4: Evaluation (simulate interview notes) ===
...
```

All flags are optional — `--mode` defaults to `simple`, `--role` to `Software Engineer`, and
`--resume` to `assets/resumes/jane_doe.txt`.

---

## 4) What this demo shows

### Demo 1 baseline (always present)
- **Single agent + structured output** — `AgentFactory.CreateAzureOpenAIAgent` wires an `AIAgent`
  to Azure OpenAI; `JsonAgentRunner.RunJsonAsync<T>` enforces a typed JSON response
- **Auth** — supports both **Azure CLI / RBAC** (`az login`) and an explicit **API key**

### Demo 2 additions
- **Multi-agent pipeline** — four specialised agents, each with a single responsibility, chained
  sequentially
- **Type-safe hand-offs** — typed JSON output of each agent becomes the structured input of the
  next: `ResumeProfile` → `SeniorityAssessment` → `InterviewPlan` → `EvaluationResult`
- **Human-in-the-loop** — `Console.ReadLine()` approval gate between planner and evaluator;
  the same pattern maps to a Teams Adaptive Card or API approval endpoint in production
- **Iterative revision** — when rejected, the planner receives its own previous JSON plus feedback
  and refines in place

### Demo 3 additions (new on `main`)
- **`WorkflowBuilder` graph** — three lines replace ~20 lines of sequential `await` calls:
  ```csharp
  var workflow = new WorkflowBuilder(ingestionAgent)
      .AddEdge(ingestionAgent, seniorityAgent)
      .AddEdge(seniorityAgent, plannerAgent)
      .Build();
  ```
- **`InProcessExecution.StreamAsync`** — executes the graph in-process with per-executor streaming
- **`TurnToken`** — lazy trigger; nothing runs until the token is sent into the stream
- **Per-executor streaming output** — each agent's tokens arrive independently and are printed as
  they stream, making the workflow execution visible in the terminal
- **`--mode` flag** — run `simple` (Demo 2) or `workflow` (Demo 3) from the same binary without
  switching branches

---

## Repo layout

```
src/InterviewAssistant/
  Program.cs                          — CLI entry point — Demo 3 final state (main)
  Program_demo2.cs                    — Reference snapshot for demo/2-multi-agent branch
  Agents/
    AgentFactory.cs                   — Creates an AIAgent backed by Azure OpenAI
    AgentPrompts.cs                   — System prompts for all four agents
    JsonAgentRunner.cs                — Runs an agent and deserialises JSON output
  Models/
    ResumeProfile.cs                  — POCO: resume structured data          (Demo 1)
    SeniorityAssessment.cs            — POCO: level + confidence + rationale  (Demo 2)
    InterviewPlan.cs                  — POCO: rounds, questions, rubric       (Demo 2)
    EvaluationResult.cs               — POCO: score, recommendation, signals  (Demo 2)
  Workflows/
    InterviewWorkflowRunner.cs        — WorkflowBuilder graph runner          (Demo 3)
assets/
  resumes/
    jane_doe.txt                      — Sample resume used across all demos
```

---

## Related Documentation

- [EXPLANATION_PROGRAM.md](EXPLANATION_PROGRAM.md) — Line-by-line walkthrough of `Program.cs`
- [src/InterviewAssistant/DEMO_PLAN_PROGRESSIVE.md](src/InterviewAssistant/DEMO_PLAN_PROGRESSIVE.md) — Full three-demo structure and talking points
- [Agents.md](Agents.md) — Deep dive into agent architecture and system prompts