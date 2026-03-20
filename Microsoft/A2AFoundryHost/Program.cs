// Simple A2A Host for Azure AI Foundry Agent
#pragma warning disable MEAI001 // McpServerToolApprovalRequestContent is experimental

using A2A;
using A2A.AspNetCore;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text;

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

// Create A2A server with our agent handler
var agentHandler = new FoundryAgentHandler(autoApprovingAgent);
var a2aServer = new A2AServer(
    agentHandler,
    new InMemoryTaskStore(),
    new ChannelEventNotifier(),
    app.Services.GetRequiredService<ILogger<A2AServer>>());

// Map A2A JSON-RPC endpoint
app.MapA2A(a2aServer, "/");

// Map well-known agent card endpoint for discovery
app.MapWellKnownAgentCard(agentCard, "/");

Console.WriteLine($"A2A Server running with agent: {agentCard.Name}");
Console.WriteLine("Endpoints:");
Console.WriteLine("  - Agent Card: /.well-known/agent-card.json");
Console.WriteLine("  - A2A Messages: /");

await app.RunAsync();

// IAgentHandler implementation that bridges AIAgent to A2A protocol
public class FoundryAgentHandler : IAgentHandler
{
    private readonly AIAgent _agent;

    public FoundryAgentHandler(AIAgent agent) => _agent = agent;

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue events, CancellationToken cancellationToken)
    {
        var taskUpdater = new TaskUpdater(events, context.TaskId ?? Guid.NewGuid().ToString(), context.ContextId ?? "");
        await taskUpdater.StartWorkAsync(new Message { Role = Role.Agent, Parts = [Part.FromText("Processing...")] }, cancellationToken);

        try
        {
            var session = await _agent.CreateSessionAsync(cancellationToken);
            var userMessage = new ChatMessage(ChatRole.User, context.UserText ?? "");

            if (context.StreamingResponse)
            {
                var fullText = new StringBuilder();
                await foreach (var update in _agent.RunStreamingAsync(userMessage, session, cancellationToken: cancellationToken))
                {
                    if (update.Text is { } text)
                    {
                        fullText.Append(text);
                        await events.EnqueueStatusUpdateAsync(new TaskStatusUpdateEvent
                        {
                            TaskId = taskUpdater.TaskId,
                            ContextId = taskUpdater.ContextId,
                            Status = new A2A.TaskStatus
                            {
                                State = TaskState.Working,
                                Message = new Message { Role = Role.Agent, Parts = [Part.FromText(text)] }
                            }
                        }, cancellationToken);
                    }
                }

                await taskUpdater.CompleteAsync(new Message
                {
                    Role = Role.Agent,
                    Parts = [Part.FromText(fullText.ToString())]
                }, cancellationToken);
            }
            else
            {
                var response = await _agent.RunAsync(userMessage, session, cancellationToken: cancellationToken);
                await taskUpdater.CompleteAsync(new Message
                {
                    Role = Role.Agent,
                    Parts = [Part.FromText(response.Text ?? "")]
                }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FoundryAgentHandler] Error: {ex}");
            await taskUpdater.FailAsync(new Message
            {
                Role = Role.Agent,
                Parts = [Part.FromText($"Error: {ex.Message}")]
            }, cancellationToken);
        }
    }

    public Task CancelAsync(RequestContext context, AgentEventQueue events, CancellationToken cancellationToken)
    {
        events.Complete();
        return Task.CompletedTask;
    }
}

// Auto-approving wrapper that automatically approves MCP tool calls
public class AutoApprovingAgentWrapper : DelegatingAIAgent
{
    public AutoApprovingAgentWrapper(AIAgent innerAgent) : base(innerAgent) { }

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await InnerAgent.RunAsync(messages, session, options, cancellationToken);
        
        // Keep running until we get a final response (no more approval requests)
        var approvalRequests = GetApprovalRequests(response);
        while (approvalRequests.Count > 0)
        {
            Console.WriteLine($"[AutoApprove] Auto-approving {approvalRequests.Count} tool call(s)");

            // Create approval responses
            var approvalMessages = approvalRequests
                .Select(req =>
                {
                    LogApproval(req);
                    return new ChatMessage(ChatRole.User, [req.CreateResponse(approved: true)]);
                })
                .ToList();

            // Continue the conversation with approvals
            response = await InnerAgent.RunAsync(approvalMessages, session, options, cancellationToken);
            approvalRequests = GetApprovalRequests(response);
        }

        Console.WriteLine($"[AutoApprove] Final response: {response.Text?.Substring(0, Math.Min(100, response.Text?.Length ?? 0))}...");
        return response;
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pendingApprovals = new List<McpServerToolApprovalRequestContent>();
        
        await foreach (var update in InnerAgent.RunStreamingAsync(messages, session, options, cancellationToken))
        {
            // Collect any approval requests from update contents
            pendingApprovals.AddRange(update.Contents.OfType<McpServerToolApprovalRequestContent>());
            yield return update;
        }

        // If we have pending approvals, auto-approve and continue
        while (pendingApprovals.Count > 0)
        {
            Console.WriteLine($"[AutoApprove-Streaming] Auto-approving {pendingApprovals.Count} tool call(s)");

            var approvalMessages = pendingApprovals
                .Select(req =>
                {
                    LogApproval(req);
                    return new ChatMessage(ChatRole.User, [req.CreateResponse(approved: true)]);
                })
                .ToList();

            pendingApprovals.Clear();

            // Continue with approvals
            await foreach (var update in InnerAgent.RunStreamingAsync(approvalMessages, session, options, cancellationToken))
            {
                pendingApprovals.AddRange(update.Contents.OfType<McpServerToolApprovalRequestContent>());
                yield return update;
            }
        }
    }

    private static List<McpServerToolApprovalRequestContent> GetApprovalRequests(AgentResponse response)
        => response.Messages
            .SelectMany(m => m.Contents)
            .OfType<McpServerToolApprovalRequestContent>()
            .ToList();

    private static void LogApproval(McpServerToolApprovalRequestContent req)
    {
        var name = $"{req.ToolCall.ServerName}/{req.ToolCall.ToolName}";
        Console.WriteLine($"  - Approving: {name}");
    }
}

