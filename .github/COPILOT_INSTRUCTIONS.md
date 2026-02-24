# GitHub Copilot Instructions

> These instructions guide Copilot to generate code that is consistent with this
> project's architecture, style, and quality standards.

---

## Project Overview

**MAF Interview Assistant** is a C# (.NET 8) console application that uses the
**Microsoft Agent Framework (MAF)** and **Azure OpenAI** to orchestrate a
multi-agent pipeline for AI-powered interview planning and evaluation.

### Core Stack
| Layer | Technology |
|---|---|
| Runtime | .NET 8 / C# 12 |
| AI Agents | `Microsoft.Agents.AI` + `Microsoft.Agents.AI.Workflows` |
| LLM Backend | Azure OpenAI (`Azure.AI.OpenAI`) |
| Serialization | `System.Text.Json` |
| Auth | `Azure.Identity` (`AzureCliCredential` / `AzureKeyCredential`) |

### Key Namespaces
```
InterviewAssistant.Agents      → AIAgent creation, prompts, JSON runner
InterviewAssistant.Models      → Immutable record/POCO models (ResumeProfile, etc.)
InterviewAssistant.Workflows   → WorkflowBuilder orchestration
```

---

## Architecture Rules

### Agents (`Agents/`)
- `AgentFactory` is the **only** place where `AIAgent` instances are created.
  Never call `AzureOpenAIClient` directly outside of this class.
- `AgentPrompts` holds **all** system prompt strings as `public const string`.
  Never inline prompt text in `Program.cs` or workflow code.
- `JsonAgentRunner` is the **only** entry point for calling an agent and
  deserializing its JSON response. Always use `RunJsonAsync<T>`.
- When adding a new agent, add it in this order:
  1. Add the prompt constant to `AgentPrompts`.
  2. Register creation in `AgentFactory` (or reuse `CreateAzureOpenAIAgent`).
  3. Wire the agent into the workflow or call it via `JsonAgentRunner`.

### Models (`Models/`)
- Models are **plain data containers** — no business logic, no I/O.
- Use `sealed` classes with `[JsonPropertyName]` on every property.
- Use `init`-only setters or `required` when a property must always be set.
- Collections default to `new()` — never return `null` for list properties.
- Use nullable reference types (`string?`, `double?`) for optional fields.

### Workflows (`Workflows/`)
- Workflows are orchestrated via `WorkflowBuilder` from `Microsoft.Agents.AI.Workflows`.
- Each workflow runner method returns a named tuple for clarity.
- Always accept and forward a `CancellationToken` parameter.
- Do **not** parse or interpret agent output inside the workflow runner —
  that is the responsibility of the caller.

---

## Clean Code Standards

### Naming
| Construct | Convention | Example |
|---|---|---|
| Classes / Methods / Properties | `PascalCase` | `RunPlanWorkflowAsync` |
| Local variables / Parameters | `camelCase` | `resumeText` |
| Private fields | `_camelCase` | `_jsonOptions` |
| Interfaces | `IPascalCase` | `IAgentRunner` |
| Constants | `PascalCase` | `AgentPrompts.ResumeIngestion` |
| Async methods | Suffix `Async` | `RunJsonAsync` |

### Methods
- Keep methods **short and focused** — one responsibility per method.
- Use **guard clauses** (early returns) instead of deeply nested `if` blocks.
- Limit parameters to **3 or fewer**; group related params into a record/class.
- Prefer **expression-bodied members** for simple one-liners.

### Variables & Types
- Use `var` only when the type is obvious from the right-hand side.
- Prefer `readonly` for fields that do not change after construction.
- Avoid magic strings/numbers — use named constants or enums.
- Prefer `sealed` classes to prevent unintended inheritance.

---

## SOLID Principles

### Single Responsibility
Each class has exactly one reason to change:
- `AgentFactory` → agent construction only.
- `AgentPrompts` → prompt text only.
- `JsonAgentRunner` → invoke agent + deserialize only.
- `InterviewWorkflowRunner` → workflow orchestration only.
- Model classes → data shape only.

### Open / Closed
- Add new agent types by adding a new creation method to `AgentFactory`,
  not by modifying existing methods.
- Add new workflow stages by extending `WorkflowBuilder` chains, not by
  rewriting existing workflow runners.

### Liskov Substitution
- If you introduce an `IAgentRunner` abstraction, all implementations must
  honour the same contract (same exceptions, same return semantics).

### Interface Segregation
- Prefer small, focused interfaces. Do not create a single large `IAgent`
  interface that mixes concerns (creation, execution, logging).

### Dependency Inversion
- Depend on `AIAgent` (framework abstraction), not on `AzureOpenAIClient` directly.
- Inject dependencies via constructor parameters in non-static classes.
- Avoid calling `Environment.GetEnvironmentVariable` outside of `AgentFactory`.

---

## Async / Await

- Always `await` tasks — never use `.Result` or `.Wait()`.
- Pass `CancellationToken` through the entire call chain.
- Use `ConfigureAwait(false)` in library/utility code
  (e.g., `WatchStreamAsync(...).ConfigureAwait(false)`).
- Name async methods with the `Async` suffix.

---

## Error Handling

- Never silently swallow exceptions.
- Throw `InvalidOperationException` for unrecoverable configuration errors
  (missing env vars, null deserialization results).
- Wrap agent JSON-parse failures with a descriptive message that includes
  the raw agent output (see `JsonAgentRunner`).
- Validate required inputs (file existence, env vars) at the earliest
  possible point — fail fast.

---

## Azure OpenAI / Environment Variables

| Variable | Purpose |
|---|---|
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI resource URL (required) |
| `AZURE_OPENAI_DEPLOYMENT` | Model deployment name (required) |
| `AZURE_OPENAI_API_KEY` | API key (optional — falls back to `AzureCliCredential`) |

- Never hardcode endpoints, keys, or deployment names in source code.
- Always use `?? throw new InvalidOperationException(...)` when reading
  required environment variables.

---

## Testing Guidelines

- Follow the **AAA** pattern: **Arrange**, **Act**, **Assert**.
- Test method naming: `MethodName_Scenario_ExpectedBehavior`
  - Example: `RunJsonAsync_InvalidJson_ThrowsInvalidOperationException`
- Mock `AIAgent` via an interface or wrapper — never call real Azure endpoints
  in unit tests.
- Test model serialization round-trips to catch `[JsonPropertyName]` regressions.
- Aim for full coverage of `JsonAgentRunner` and model deserialization paths.

---

## What to Avoid

- ❌ Inline prompt strings outside of `AgentPrompts`.
- ❌ Creating `AzureOpenAIClient` instances outside of `AgentFactory`.
- ❌ Calling `.Result` or `.Wait()` on tasks.
- ❌ Returning `null` from collection properties on models.
- ❌ Catching bare `Exception` without re-throwing or detailed logging.
- ❌ Hardcoded secrets, endpoints, or deployment names.
- ❌ Business/parsing logic inside workflow runner methods.
- ❌ Static mutable state.

