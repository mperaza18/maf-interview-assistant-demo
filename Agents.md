# InterviewAssistant – Agent Context

## Project Overview
AI-powered interview assistant that automates resume ingestion, seniority classification,
interview planning, and candidate evaluation using a multi-agent architecture
built on the Microsoft Agent Framework (Microsoft.Agents.AI).

---

## Tech Stack
- **Language:** C# (.NET 8+)
- **AI Framework:** Microsoft.Agents.AI / Microsoft.Extensions.AI
- **LLM Backend:** Azure OpenAI (GPT-4o or equivalent)
- **Architecture:** Multi-agent pipeline with optional workflow orchestration

---

## Project Structure

```
src/InterviewAssistant/
├── Program.cs                      # Entry point, CLI arg parsing, pipeline orchestration
├── Agents/
│   ├── AgentFactory.cs             # Creates AIAgent instances backed by Azure OpenAI
│   ├── AgentPrompts.cs             # System prompts for each agent role
│   └── JsonAgentRunner.cs          # Runs an agent and deserializes JSON output
├── Models/
│   ├── ResumeProfile.cs            # Structured resume data
│   ├── SeniorityAssessment.cs      # Seniority level + confidence + rationale
│   ├── InterviewPlan.cs            # Rounds, questions, role, level, summary
│   └── EvaluationResult.cs         # Score, recommendation, strengths, risks, follow-ups
├── Workflows/
│   └── InterviewWorkflowRunner.cs  # Sequential workflow: ingest → classify → plan
└── assets/
    └── resumes/
        └── jane_doe.txt            # Sample resume for testing
```

---

## Agents & Responsibilities

| Agent Name            | Role                                                                 |
|-----------------------|----------------------------------------------------------------------|
| `ResumeIngestion`     | Parses raw resume text → returns `ResumeProfile` JSON               |
| `SeniorityClassifier` | Evaluates profile → returns `SeniorityAssessment` JSON              |
| `InterviewPlanner`    | Generates structured `InterviewPlan` JSON (rounds + questions)      |
| `Evaluator`           | Scores candidate based on notes + plan → returns `EvaluationResult` |

---

## Execution Modes

### `--mode simple` (default)
Sequential step-by-step pipeline:
1. Resume ingestion → `ResumeProfile`
2. Seniority classification → `SeniorityAssessment`
3. Interview planning → `InterviewPlan`
4. Human-in-the-loop approval / revision loop
5. Evaluation with interview notes → `EvaluationResult`

### `--mode workflow`
Uses `InterviewWorkflowRunner` to orchestrate agents as a single workflow.
All three planning stages run under a unified orchestration context.

---

## CLI Usage

```bash
dotnet run --project src/InterviewAssistant \
  --mode simple \
  --role "Backend Engineer" \
  --resume assets/resumes/jane_doe.txt
```

---

## Key Patterns & Conventions

- **Structured JSON output:** All agents return typed JSON deserialized via `JsonAgentRunner.RunJsonAsync<T>()`.
- **Human-in-the-loop:** After planning, the user can approve or provide feedback to trigger a revision pass.
- **Prompt composition:** System prompts are centralized in `AgentPrompts` static class.
- **Agent creation:** Always use `AgentFactory.CreateAzureOpenAIAgent(name, systemPrompt)`.
- **Models are immutable DTOs:** Do not add business logic inside model classes.

---

## Important Keywords
`AIAgent`, `AgentFactory`, `JsonAgentRunner`, `InterviewWorkflowRunner`,
`ResumeProfile`, `SeniorityAssessment`, `InterviewPlan`, `EvaluationResult`,
`AgentPrompts`, `ChatMessage`, `ChatRole`

---

## Environment Requirements
- Azure OpenAI endpoint and API key must be configured via environment variables
  or `appsettings.json` before running the project.
- .NET 8 SDK or later required.

