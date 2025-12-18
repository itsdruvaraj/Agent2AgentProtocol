// Simple A2A Host for Azure AI Foundry Agent
#pragma warning disable MEAI001 // McpServerToolApprovalRequestContent is experimental

using A2A;
using A2A.AspNetCore;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient().AddLogging();

var app = builder.Build();

// Configuration
string endpoint = builder.Configuration["AZURE_FOUNDRY_PROJECT_ENDPOINT"]
    ?? throw new InvalidOperationException("AZURE_FOUNDRY_PROJECT_ENDPOINT is required");
string agentId = builder.Configuration["AZURE_FOUNDRY_AGENT_ID"]
    ?? throw new InvalidOperationException("AZURE_FOUNDRY_AGENT_ID is required");
string? mcpBearerToken = builder.Configuration["MCP_BEARER_TOKEN"]; // Optional Bearer token for MCP tools

// Create the Foundry agent
var persistentAgentsClient = new PersistentAgentsClient(endpoint, new AzureCliCredential());
PersistentAgent persistentAgent = await persistentAgentsClient.Administration.GetAgentAsync(agentId);

Console.WriteLine($"Loaded agent from Foundry:");
Console.WriteLine($"  ID: {persistentAgent.Id}");
Console.WriteLine($"  Name: {persistentAgent.Name}");
Console.WriteLine($"  Model: {persistentAgent.Model}");
Console.WriteLine($"  Tools: {persistentAgent.Tools?.Count ?? 0}");

// List tool types if any
if (persistentAgent.Tools?.Count > 0)
{
    foreach (var tool in persistentAgent.Tools)
    {
        Console.WriteLine($"    - {tool.GetType().Name}");
    }
}

AIAgent agent = await persistentAgentsClient.GetAIAgentAsync(persistentAgent.Id);

// Note: Bearer token for MCP tools must be configured in Azure AI Foundry when creating/updating the agent.
// Runtime header injection for pre-existing agents is not supported in the current .NET SDK.
// The MCP_BEARER_TOKEN config is reserved for future use or if implementing lower-level API calls.
if (!string.IsNullOrEmpty(mcpBearerToken))
{
    Console.WriteLine("[Warning] MCP_BEARER_TOKEN is set but runtime header injection for existing agents is not currently supported.");
    Console.WriteLine("          Configure MCP tool headers in Azure AI Foundry instead, or create a new agent with HostedMcpServerTool.AuthorizationToken.");
}

// Create an auto-approving wrapper to handle MCP tool approvals
var autoApprovingAgent = new AutoApprovingAgentWrapper(agent);

// Define the agent card for A2A discovery
var agentCard = new AgentCard
{
    Name = persistentAgent.Name ?? "Foundry Agent",
    Description = persistentAgent.Description ?? "Azure AI Foundry Agent with A2A",
    Version = "1.0.0",
    Url = "http://localhost:5000/",
    Capabilities = new AgentCapabilities
    {
        Streaming = true,
        PushNotifications = false
    },
    DefaultInputModes = ["text"],
    DefaultOutputModes = ["text"],
    Skills = 
    [
        new AgentSkill
        {
            Id = "foundry-agent",
            Name = persistentAgent.Name ?? "Foundry Agent",
            Description = persistentAgent.Description ?? "Azure AI Foundry Agent",
            Tags = ["foundry", "ai-agent"],
            Examples = ["What can you help me with?"]
        }
    ]
};

// Map A2A endpoints
app.MapA2A(
    autoApprovingAgent,
    path: "/",
    agentCard: agentCard,
    taskManager => app.MapWellKnownAgentCard(taskManager, "/"));

Console.WriteLine($"A2A Server running with agent: {agentCard.Name}");
Console.WriteLine("Endpoints:");
Console.WriteLine("  - Agent Card: /v1/card");
Console.WriteLine("  - A2A Messages: /");

await app.RunAsync();

// Auto-approving wrapper that automatically approves MCP tool calls
public class AutoApprovingAgentWrapper : AIAgent
{
    private readonly AIAgent _innerAgent;

    public AutoApprovingAgentWrapper(AIAgent innerAgent)
    {
        _innerAgent = innerAgent;
    }

    public override string Id => _innerAgent.Id ?? string.Empty;
    public override string? Name => _innerAgent.Name;
    public override string? Description => _innerAgent.Description;

    public override AgentThread GetNewThread() => _innerAgent.GetNewThread();

    public override AgentThread DeserializeThread(System.Text.Json.JsonElement threadState, System.Text.Json.JsonSerializerOptions? options = null)
        => _innerAgent.DeserializeThread(threadState, options) ?? throw new InvalidOperationException("Failed to deserialize thread");

    public override async Task<AgentRunResponse> RunAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _innerAgent.RunAsync(messages, thread, options, cancellationToken);
        
        // Keep running until we get a final response (no more approval requests)
        while (response.UserInputRequests?.Any() == true)
        {
            var approvalRequests = response.UserInputRequests
                .OfType<McpServerToolApprovalRequestContent>()
                .ToList();

            if (approvalRequests.Count == 0)
                break; // No MCP approvals pending, return as-is

            Console.WriteLine($"[AutoApprove] Auto-approving {approvalRequests.Count} MCP tool call(s)");

            // Create approval responses
            var approvalMessages = approvalRequests
                .Select(req =>
                {
                    Console.WriteLine($"  - Approving: {req.ToolCall.ServerName}/{req.ToolCall.ToolName}");
                    return new ChatMessage(ChatRole.User, [req.CreateResponse(approved: true)]);
                })
                .ToList();

            // Continue the conversation with approvals
            response = await _innerAgent.RunAsync(approvalMessages, thread, options, cancellationToken);
        }

        Console.WriteLine($"[AutoApprove] Final response: {response.Text?.Substring(0, Math.Min(100, response.Text?.Length ?? 0))}...");
        return response;
    }

    public override async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pendingApprovals = new List<McpServerToolApprovalRequestContent>();
        
        await foreach (var update in _innerAgent.RunStreamingAsync(messages, thread, options, cancellationToken))
        {
            // Collect any approval requests
            if (update.UserInputRequests?.Any() == true)
            {
                var approvalRequests = update.UserInputRequests
                    .OfType<McpServerToolApprovalRequestContent>()
                    .ToList();
                
                pendingApprovals.AddRange(approvalRequests);
            }
            
            yield return update;
        }

        // If we have pending approvals, auto-approve and continue
        while (pendingApprovals.Count > 0)
        {
            Console.WriteLine($"[AutoApprove-Streaming] Auto-approving {pendingApprovals.Count} MCP tool call(s)");

            var approvalMessages = pendingApprovals
                .Select(req =>
                {
                    Console.WriteLine($"  - Approving: {req.ToolCall.ServerName}/{req.ToolCall.ToolName}");
                    return new ChatMessage(ChatRole.User, [req.CreateResponse(approved: true)]);
                })
                .ToList();

            pendingApprovals.Clear();

            // Continue with approvals
            await foreach (var update in _innerAgent.RunStreamingAsync(approvalMessages, thread, options, cancellationToken))
            {
                if (update.UserInputRequests?.Any() == true)
                {
                    var newApprovals = update.UserInputRequests
                        .OfType<McpServerToolApprovalRequestContent>()
                        .ToList();
                    
                    pendingApprovals.AddRange(newApprovals);
                }
                
                yield return update;
            }
        }
    }
}

