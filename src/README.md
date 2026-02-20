# Microsoft Agent Framework — AI Interview Assistant (1-hour demo repo)

This repo is designed for a **1-hour, hands-on** tech talk showcasing the two core capability buckets in **Microsoft Agent Framework**:

- **AI Agents** (LLM-driven, tool-using, dynamic steps)
- **Workflows** (explicit orchestration graph with control-flow, checkpoints, human-in-the-loop)

> The code is intentionally *minimal* and console-based so you can live-code it.

---

## 0) Prereqs

- **.NET 8 SDK+**
- **Azure OpenAI** endpoint + a deployed chat model (e.g., `gpt-4o-mini`)
- **Azure CLI** authenticated via `az login` **OR** an API key

The Microsoft Learn quick-start uses these packages and Azure CLI auth.

---

## 1) Setup (packages)

From `src/InterviewAssistant`:

```bash
# Core agent packages (from the Learn quick-start)
dotnet add package Azure.AI.OpenAI --prerelease
dotnet add package Azure.Identity
dotnet add package Microsoft.Agents.AI.OpenAI --prerelease

# Workflows (to demonstrate orchestration graphs)
dotnet add package Microsoft.Agents.AI.Workflows --prerelease

# Optional (nice-to-have)
dotnet add package Microsoft.Extensions.Configuration
dotnet add package Microsoft.Extensions.Configuration.EnvironmentVariables
```

---

## 2) Configure environment

Set these environment variables:

- `AZURE_OPENAI_ENDPOINT` — e.g. `https://your-resource.openai.azure.com/`
- `AZURE_OPENAI_DEPLOYMENT` — e.g. `gpt-4o-mini`

Optional:
- `AZURE_OPENAI_API_KEY` — if you don’t want Azure CLI / RBAC auth

---

## 3) Run

### Simple (agents-only) pipeline

```bash
dotnet run --mode simple --resume ../../assets/resumes/jane_doe.txt
```

### Workflows pipeline (agent graph + approval gate)

```bash
dotnet run --mode workflow --resume ../../assets/resumes/jane_doe.txt
```

---

## 4) What this demo shows

### Agents
- Resume ingestion → **structured JSON output**
- Seniority classification
- Interview plan generation
- (Optional) interactive follow-ups

### Workflows
- Explicit **graph** of steps (executors + edges)
- A **human-in-the-loop approval gate** before the interview plan is finalized

Workflows are defined as an explicit sequence/graph, vs. agents whose steps are dynamic and model-driven.

---

## 5) Live-coding suggestion

Start with **simple mode** (agents only). Once the audience sees the value, switch to **workflow mode** and show how the same “brain” (agents) becomes **production-safe** via orchestration and checkpoints.

---

## Repo layout

- `src/InterviewAssistant/Program.cs` — CLI entrypoint
- `src/InterviewAssistant/Agents/` — agent factory + prompts
- `src/InterviewAssistant/Models/` — POCOs for structured outputs
- `src/InterviewAssistant/Workflows/` — minimal workflow runner + approval executor
- `assets/resumes/` — sample resume text
