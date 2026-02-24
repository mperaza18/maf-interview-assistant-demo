# Copilot Instructions Audit

> Audit date: 2026-02-23  
> Audited against: `.github/COPILOT_INSTRUCTIONS.md`  
> Status key: 🔴 Violation &nbsp;|&nbsp; 🟡 Partial &nbsp;|&nbsp; 🟢 Compliant

---

## Summary

| Severity | Count |
|---|---|
| 🔴 Violation | 9 |
| 🟡 Partial | 3 |
| 🟢 Compliant | 18 |

---

## Findings

| # | File | Line(s) | Rule Violated | Description | Proposed Fix |
|---|---|---|---|---|---|
| 1 | `Program.cs` | 47–48 | **Agents › No inline prompt text** | An inline prompt string is constructed directly in `Program.cs`: `"First extract a ResumeProfile JSON, then classify seniority, then produce an InterviewPlan JSON."` This business instruction text belongs in `AgentPrompts`, not in the entry point. | Move the workflow input template string to a new constant `AgentPrompts.WorkflowInput` (or `AgentPrompts.WorkflowCombinedInput`) and reference it from `Program.cs`. |
| 2 | `Program.cs` | 60–61 | **Agents › No inline prompt text** | The reformat instruction `"Reformat this EXACT content as a single valid InterviewPlan JSON (no markdown):\n\n{plannerRaw}"` is an inline prompt built in `Program.cs`. | Extract this template into `AgentPrompts.ReformatInterviewPlan` and format it with the dynamic value at call-site. |
| 3 | `Program.cs` | 122–128 | **Agents › No inline prompt text** | The plan revision prompt (`"Revise the InterviewPlan JSON below based on this feedback..."`) is composed inline in `Program.cs`. | Add `AgentPrompts.RevisePlan` constant and use it from `Program.cs`. |
| 4 | `Program.cs` | 15–17 | **Clean Code › Magic strings** | Default values `"simple"`, `"Software Engineer"`, and the default resume path `"assets/resumes/jane_doe.txt"` are magic strings scattered inline. | Define them as `private const string` (or top-level constants in a `AppDefaults` static class) and reference by name. |
| 5 | `Program.cs` | 1–184 | **SOLID › Single Responsibility** | `Program.cs` mixes at least five concerns in one flat top-level script: argument parsing, file I/O, agent orchestration (both modes), human-in-the-loop console interaction, and final output rendering. Each concern should be a method or class of its own. | Extract responsibilities into dedicated classes: e.g., `CliArgumentParser`, `InterviewSessionRunner` (simple mode), `WorkflowSessionRunner` (workflow mode), `ConsoleReporter`. `Program.cs` should only wire them together. |
| 6 | `Program.cs` | 1–184 | **Clean Code › Method length / one responsibility per method** | The entire application logic lives in a single flat top-level file with no methods beyond the local `GetArg` helper. All steps (ingest, classify, plan, human checkpoint, revise, evaluate) are sequentially inlined. | Decompose into well-named `async` methods or a coordinator class, each covering one step (e.g., `RunIngestionStepAsync`, `RunPlanningStepAsync`, `RunEvaluationStepAsync`). |
| 7 | `Program.cs` | 64–65 | **Models › No magic/hardcoded values** | `new ResumeProfile { CandidateName = "(captured in workflow output)" }` and `Confidence = 0.8` are hardcoded placeholder values (including a magic number `0.8`). | Replace the magic number `0.8` with a named constant (e.g., `const double WorkflowPlaceholderConfidence = 0.8`) and the placeholder string with a constant too. |
| 8 | `AgentFactory.cs` | 9 | **SOLID › Dependency Inversion / testability** | `AgentFactory` is a `static` class that reads `Environment.GetEnvironmentVariable` directly, making it impossible to inject configuration or test without setting real env vars. | Convert `AgentFactory` to a non-static class that accepts an `IConfiguration` or a strongly-typed `AzureOpenAIOptions` record via constructor injection. Keep a static factory method only as a convenience bootstrapper for the entry point. |
| 9 | `JsonAgentRunner.cs` | 7 | **SOLID › Dependency Inversion / testability** | `JsonAgentRunner` is a `static` class. There is no interface (`IJsonAgentRunner`) to depend on, so callers cannot be unit-tested by swapping the implementation with a mock. | Extract an `IJsonAgentRunner` interface with `RunJsonAsync<T>` signature, implement it in a non-static `JsonAgentRunner` class, and register it via DI. |
| 10 | `InterviewWorkflowRunner.cs` | 8 | **SOLID › Dependency Inversion / testability** | Same issue as above — `InterviewWorkflowRunner` is a `static` class with no abstraction interface, making workflow orchestration untestable in isolation. | Introduce `IInterviewWorkflowRunner` and make `InterviewWorkflowRunner` a non-static implementation. |
| 11 | `Program.cs` | 75–76 | **Clean Code › `var` only when type is obvious** | `var ingestPrompt = $"..."` — the type is `string` and obvious here (acceptable), but the broader concern is that several `var` usages follow the same pattern throughout. However, `var (plannerRaw, perExecutor) = await ...` on line 52 unpacks a named tuple cleanly and is fine. This is a minor/borderline finding. | Low priority; consistent use is acceptable when the right-hand side makes the type unambiguous. No change strictly required, but worth noting for team consistency. |
| 12 | `Models/InterviewPlan.cs` | 15–24 | **Models › One class per file** | `InterviewRound` and `RubricItem` are defined in the same file as `InterviewPlan` (`InterviewPlan.cs`). The instructions state one class per file. | Move `InterviewRound` to `Models/InterviewRound.cs` and `RubricItem` to `Models/RubricItem.cs`. |

---

## Compliant Areas (no action required)

| Area | Rule | Notes |
|---|---|---|
| `AgentPrompts.cs` | All system prompts are `public const string` | ✅ Fully compliant |
| `AgentFactory.cs` | `AzureOpenAIClient` only created here | ✅ Correctly centralized |
| `AgentFactory.cs` | Falls back to `AzureCliCredential` when no API key | ✅ Follows env var / no-hardcoded-secret rule |
| `JsonAgentRunner.cs` | Single entry point for agent calls + JSON deserialization | ✅ Correct responsibility boundary |
| `JsonAgentRunner.cs` | Wraps parse failure with raw output in `InvalidOperationException` | ✅ Follows error handling rule |
| `JsonAgentRunner.cs` | Uses `?? throw new JsonException(...)` for null deserialization | ✅ Fail-fast pattern |
| All models | `sealed` classes with `[JsonPropertyName]` on every property | ✅ Fully compliant |
| All models | Collections initialized to `new()` — never `null` | ✅ Fully compliant |
| All models | Nullable reference types used for optional fields | ✅ Fully compliant |
| All models | No business logic or I/O in model classes | ✅ Pure data containers |
| `InterviewWorkflowRunner.cs` | Accepts and forwards `CancellationToken` | ✅ Compliant |
| `InterviewWorkflowRunner.cs` | Uses `ConfigureAwait(false)` on `WatchStreamAsync` | ✅ Compliant |
| `InterviewWorkflowRunner.cs` | Returns named tuple `(PlannerRawJson, PerExecutorOutput)` | ✅ Compliant |
| `InterviewWorkflowRunner.cs` | Does not parse/interpret agent JSON output internally | ✅ Compliant |
| `Program.cs` | All agent calls go through `JsonAgentRunner.RunJsonAsync<T>` | ✅ Compliant |
| `Program.cs` | `await` used consistently — no `.Result` / `.Wait()` | ✅ Compliant |
| `Program.cs` | Resume file existence validated early (fail-fast) | ✅ Compliant |
| `InterviewAssistant.csproj` | `<Nullable>enable</Nullable>` is set | ✅ Nullable reference types enabled project-wide |

