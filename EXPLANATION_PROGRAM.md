# Program.cs - Detailed Explanation

This document provides a comprehensive explanation of the `Program.cs` file, which serves as the main entry point for the **AI-powered Interview Assistant** application built with the Microsoft Agent Framework.

---

## Overview

The Interview Assistant is a console application that leverages Azure OpenAI agents to automate and enhance the technical interview process. It demonstrates both **AI Agents** (LLM-driven, dynamic decision-making) and **Workflows** (explicit orchestration with control flow).

---

## Core Functionality

### 1. Command-Line Arguments

The application accepts three optional command-line arguments:

```csharp
GetArg(args, "--mode")   // Execution mode: "simple" or "workflow"
GetArg(args, "--role")   // Target role (default: "Software Engineer")
GetArg(args, "--resume") // Path to resume file (default: "assets/resumes/jane_doe.txt")
```

**Helper Function:**
```csharp
static string? GetArg(string[] args, string name)
```
Searches the args array for a named argument and returns its value.

---

### 2. Four Specialized AI Agents

The application creates four distinct agents, each with a specific responsibility:

| Agent | Purpose | Input | Output |
|-------|---------|-------|--------|
| **ResumeIngestion** | Extracts structured data from raw resume text | Resume text | `ResumeProfile` JSON |
| **SeniorityClassifier** | Determines candidate experience level | `ResumeProfile` | `SeniorityAssessment` JSON |
| **InterviewPlanner** | Creates customized interview plans | Role, Profile, Seniority | `InterviewPlan` JSON |
| **Evaluator** | Assesses post-interview performance | Profile, Plan, Notes | `EvaluationResult` JSON |

All agents are created using:
```csharp
AIAgent agent = AgentFactory.CreateAzureOpenAIAgent(name, systemPrompt);
```

---

### 3. Two Execution Modes

#### **Simple Mode** (Default)

Sequential, step-by-step execution with clear visibility into each stage:

**Step 1: Resume Ingestion**
```csharp
(profile, _) = await JsonAgentRunner.RunJsonAsync<ResumeProfile>(ingestionAgent, ingestPrompt);
```
Extracts:
- Candidate name
- Core skills
- Years of experience
- Education
- Previous roles

**Step 2: Seniority Classification**
```csharp
(seniority, _) = await JsonAgentRunner.RunJsonAsync<SeniorityAssessment>(seniorityAgent, seniorityPrompt);
```
Returns:
- Level (Junior/Mid/Senior/Staff/Principal)
- Confidence score (0.0 - 1.0)
- Rationale

**Step 3: Interview Planning**
```csharp
(plan, _) = await JsonAgentRunner.RunJsonAsync<InterviewPlan>(plannerAgent, planPrompt);
```
Generates:
- Interview rounds
- Duration per round
- Targeted questions based on role and seniority
- Focus areas

#### **Workflow Mode** (`--mode workflow`)

All three planning steps (ingest → classify → plan) run as an **orchestrated workflow**:

```csharp
var (plannerRaw, perExecutor) = await InterviewWorkflowRunner.RunPlanWorkflowAsync(
    ingestionAgent,
    seniorityAgent,
    plannerAgent,
    input);
```

**Key Differences:**
- Single input message containing all context
- Explicit orchestration graph
- Streaming output per executor for debugging
- Demonstrates production-ready control flow

---

### 4. Human-in-the-Loop Approval

After generating the interview plan, the application pauses for human review:

```csharp
Console.Write("Approve this plan? (y/n): ");
var approved = Console.ReadLine()...
```

**If Rejected:**
1. User provides feedback (e.g., "more system design, fewer trivia")
2. Planner agent revises the plan based on feedback
3. Revised plan is displayed in JSON format

This demonstrates **checkpoint-based workflows** where critical decisions require human validation before proceeding.

---

### 5. Post-Interview Evaluation

The final stage simulates the post-interview assessment:

**Input Collection:**
```csharp
Console.WriteLine("Type a few bullet notes about the candidate's performance...");
```
Interviewer provides freeform notes via console input.

**Evaluation:**
```csharp
var (evaluation, _) = await JsonAgentRunner.RunJsonAsync<EvaluationResult>(evaluatorAgent, evalPrompt);
```

**Output:**
- Overall score (0-10)
- Recommendation (Hire/No-Hire/Maybe)
- Summary narrative
- Strengths (list)
- Risks/concerns (list)
- Follow-up questions for next rounds

---

## Data Flow

```
┌─────────────┐
│ Resume Text │
└──────┬──────┘
       │
       ▼
┌─────────────────┐
│ Ingestion Agent │──────► ResumeProfile (JSON)
└─────────────────┘              │
                                 ▼
                      ┌────────────────────┐
                      │ Seniority Agent    │──────► SeniorityAssessment (JSON)
                      └────────────────────┘              │
                                                          ▼
                                               ┌─────────────────┐
                                               │ Planner Agent   │──────► InterviewPlan (JSON)
                                               └─────────────────┘              │
                                                                                ▼
                                                                     ┌──────────────────┐
                                                                     │ Human Approval   │
                                                                     └────────┬─────────┘
                                                                              │
                                                                  ┌───────────┴───────────┐
                                                                  │                       │
                                                               Approved              Rejected
                                                                  │                       │
                                                                  │                       ▼
                                                                  │              ┌────────────────┐
                                                                  │              │ Revision Loop  │
                                                                  │              └────────┬───────┘
                                                                  │                       │
                                                                  └───────────┬───────────┘
                                                                              ▼
                                                                    ┌──────────────────┐
                                                                    │ Conduct Interview│
                                                                    └────────┬─────────┘
                                                                             │
                                                                             ▼
                                                                  ┌─────────────────────┐
                                                                  │ Evaluator Agent     │──────► EvaluationResult (JSON)
                                                                  └─────────────────────┘
```

---

## Key Technologies

### Microsoft Agent Framework
- **`AIAgent`**: Core abstraction for LLM-backed agents
- **`JsonAgentRunner`**: Ensures structured JSON outputs from agents
- **`ChatMessage`**: Standard message format for agent communication

### Azure OpenAI
- Backend LLM service providing the AI capabilities
- Authentication via Azure CLI (`az login`) or API key
- Deployment of chat models (e.g., `gpt-4o-mini`)

### Strongly-Typed Models
All agent outputs use POCOs for type safety:
- `ResumeProfile`
- `SeniorityAssessment`
- `InterviewPlan`
- `EvaluationResult`

---

## Workflow Summary

```
Resume → Ingest → Classify → Plan → [Human Review] → Revise? → Interview → Evaluate
```

**Simple Mode:** Sequential, transparent, easy to debug  
**Workflow Mode:** Orchestrated, production-ready, checkpoint-enabled

---

## Usage Examples

### Example 1: Simple Mode with Default Resume
```bash
dotnet run
```

### Example 2: Workflow Mode with Custom Resume
```bash
dotnet run --mode workflow --resume ../../assets/resumes/senior_dev.txt --role "Principal Engineer"
```

### Example 3: Specific Role Targeting
```bash
dotnet run --role "DevOps Engineer" --resume ../../assets/resumes/ops_specialist.txt
```

---

## Design Patterns Demonstrated

1. **Multi-Agent Collaboration**: Four specialized agents working together
2. **Structured Outputs**: All LLM responses are validated JSON
3. **Human-in-the-Loop**: Critical decisions require human approval
4. **Iterative Refinement**: Feedback loop for plan revision
5. **Separation of Concerns**: Each agent has a single, well-defined responsibility
6. **Workflow Orchestration**: Explicit vs. dynamic execution modes

---

## Extension Points

The application is designed to be easily extended:

- **Add New Agents**: Create agents for coding challenges, behavioral assessment, etc.
- **Custom Workflows**: Define complex orchestration graphs with branching logic
- **Integration**: Connect to ATS systems, calendar APIs, or video interview platforms
- **Persistence**: Save plans and evaluations to a database
- **Multi-Modal**: Add support for video/audio resume analysis

---

## Related Documentation

- [Agents.md](./Agents.md) - Deep dive into agent architecture and prompts
- [README.md](../src/README.md) - Setup and quick-start guide

---

*This explanation is based on the .NET 8 / C# 12.0 implementation using Microsoft Agent Framework.*
