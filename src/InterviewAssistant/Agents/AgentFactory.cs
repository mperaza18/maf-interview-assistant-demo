using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using OpenAI.Chat;

namespace InterviewAssistant.Agents;

public static class AgentFactory
{
    /// <summary>
    /// Creates a simple Agent Framework <see cref="AIAgent"/> backed by Azure OpenAI Chat Completion.
    ///
    /// This follows the pattern used in the Learn quick-start (AzureOpenAIClient -> GetChatClient -> AsAIAgent). 
    /// </summary>
    public static AIAgent CreateAzureOpenAIAgent(string name, string instructions)
    {
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
                       ?? throw new InvalidOperationException("Missing AZURE_OPENAI_ENDPOINT env var");

        var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT")
                         ?? throw new InvalidOperationException("Missing AZURE_OPENAI_DEPLOYMENT env var");

        var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

        // If AZURE_OPENAI_API_KEY is not set, fall back to Azure CLI / RBAC auth.
        var credential = string.IsNullOrWhiteSpace(apiKey)
            ? (object)new AzureCliCredential()
            : new AzureKeyCredential(apiKey);

        // AzureOpenAIClient supports both TokenCredential (AzureCliCredential) and AzureKeyCredential.
        var client = credential switch
        {
            AzureCliCredential c => new AzureOpenAIClient(new Uri(endpoint), c),
            AzureKeyCredential k => new AzureOpenAIClient(new Uri(endpoint), k),
            _ => throw new InvalidOperationException("Unsupported credential")
        };

        // NOTE: GetChatClient + AsAIAgent are provided by Agent Framework integrations.
        return client
            .GetChatClient(deployment)
            .AsAIAgent(instructions: instructions, name: name);
    }
}
