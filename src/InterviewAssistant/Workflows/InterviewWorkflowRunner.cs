using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace InterviewAssistant.Workflows;

public static class InterviewWorkflowRunner
{
    /// <summary>
    /// Runs a simple sequential workflow:
    /// ResumeIngestionAgent -> SeniorityClassifierAgent -> InterviewPlannerAgent
    ///
    /// Uses the same patterns as the Agent Framework "Agents in Workflows" tutorial:
    /// - WorkflowBuilder
    /// - InProcessExecution.StreamAsync
    /// - TurnToken to trigger execution
    /// - WatchStreamAsync to receive AgentResponseUpdateEvent
    /// </summary>
    public static async Task<(string PlannerRawJson, Dictionary<string, string> PerExecutorOutput)> RunPlanWorkflowAsync(
        AIAgent resumeIngestionAgent,
        AIAgent seniorityAgent,
        AIAgent plannerAgent,
        ChatMessage input,
        CancellationToken cancellationToken = default)
    {
        var workflow = new WorkflowBuilder(resumeIngestionAgent)
            .AddEdge(resumeIngestionAgent, seniorityAgent)
            .AddEdge(seniorityAgent, plannerAgent)
            .Build();

        // Collect streamed tokens per executor.
        var buffers = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);

        await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, input, cancellationToken: cancellationToken);

        // Must send the turn token to trigger the agents.
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        await foreach (WorkflowEvent evt in run.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (evt is AgentResponseUpdateEvent update)
            {
                if (!buffers.TryGetValue(update.ExecutorId, out var sb))
                {
                    sb = new StringBuilder();
                    buffers[update.ExecutorId] = sb;
                }
                sb.Append(update.Data);
            }
        }

        var outputs = buffers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());

        // Heuristic: planner output is the last executor's output in this workflow.
        // If your agent names differ, you can look for a key containing "Planner".
        var plannerKey = outputs.Keys.LastOrDefault()
                        ?? throw new InvalidOperationException("No workflow output captured.");

        return (outputs[plannerKey].Trim(), outputs);
    }
}
