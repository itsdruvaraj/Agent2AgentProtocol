// Simple A2A Host for Azure AI Foundry Agent

using A2A;
using A2A.AspNetCore;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient().AddLogging();

var app = builder.Build();

// Configuration
string endpoint = builder.Configuration["AZURE_FOUNDRY_PROJECT_ENDPOINT"]
    ?? throw new InvalidOperationException("AZURE_FOUNDRY_PROJECT_ENDPOINT is required");
string agentId = builder.Configuration["AZURE_FOUNDRY_AGENT_ID"]
    ?? throw new InvalidOperationException("AZURE_FOUNDRY_AGENT_ID is required");
string? mcpBearerToken = builder.Configuration["MCP_BEARER_TOKEN"]; // Optional fallback Bearer token for MCP tools
string mcpServerLabel = builder.Configuration["MCP_SERVER_LABEL"] ?? "custom_mcp_server";
string mcpApprovalMode = builder.Configuration["MCP_APPROVAL_MODE"] ?? "never";

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

// MCP bearer token is expected from A2A client via metadata (mcp_bearer_token).
// Host config MCP_BEARER_TOKEN serves only as a fallback.
if (!string.IsNullOrEmpty(mcpBearerToken))
{
    Console.WriteLine($"[Config] Fallback MCP bearer token configured ({mcpBearerToken.Length} chars)");
}
else
{
    Console.WriteLine("[Config] No fallback MCP_BEARER_TOKEN set. Token must come from A2A client metadata.");
}

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
// MCP bearer token comes from A2A client metadata per-request
var agentHandler = new FoundryAgentHandler(persistentAgentsClient, persistentAgent, mcpBearerToken, mcpServerLabel, mcpApprovalMode);
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

// IAgentHandler implementation that bridges Foundry Persistent Agents to A2A protocol
// Reads MCP bearer token from A2A client metadata per-request
public class FoundryAgentHandler : IAgentHandler
{
    private readonly PersistentAgentsClient _client;
    private readonly PersistentAgent _agent;
    private readonly string? _fallbackBearerToken;
    private readonly string _defaultServerLabel;
    private readonly string _defaultApprovalMode;

    public FoundryAgentHandler(PersistentAgentsClient client, PersistentAgent agent, string? fallbackBearerToken, string defaultServerLabel, string defaultApprovalMode)
    {
        _client = client;
        _agent = agent;
        _fallbackBearerToken = fallbackBearerToken;
        _defaultServerLabel = defaultServerLabel;
        _defaultApprovalMode = defaultApprovalMode;
    }

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue events, CancellationToken cancellationToken)
    {
        var taskUpdater = new TaskUpdater(events, context.TaskId ?? Guid.NewGuid().ToString(), context.ContextId ?? "");
        await taskUpdater.StartWorkAsync(new Message { Role = Role.Agent, Parts = [Part.FromText("Processing...")] }, cancellationToken);

        PersistentAgentThread? thread = null;

        try
        {
            // DEBUG: Dump all metadata received from A2A client
            Console.WriteLine($"[Handler] === METADATA DEBUG ===");
            Console.WriteLine($"[Handler] context.Metadata is null: {context.Metadata is null}");
            Console.WriteLine($"[Handler] context.Metadata count: {context.Metadata?.Count ?? 0}");
            if (context.Metadata is not null)
            {
                foreach (var kvp in context.Metadata)
                {
                    var valPreview = kvp.Value.ToString();
                    if (valPreview.Length > 50) valPreview = valPreview[..50] + "...";
                    Console.WriteLine($"[Handler]   key='{kvp.Key}', valueKind={kvp.Value.ValueKind}, value={valPreview}");
                }
            }
            Console.WriteLine($"[Handler] === END METADATA DEBUG ===");

            // Read MCP config from A2A metadata (sent by client per-request)
            string? bearerToken = GetMetadataString(context, "mcp_bearer_token") ?? _fallbackBearerToken;
            string approvalMode = GetMetadataString(context, "mcp_approval_mode") ?? _defaultApprovalMode;
            string mcpServerLabel = GetMetadataString(context, "mcp_server_label") ?? _defaultServerLabel;

            Console.WriteLine($"[Handler] Resolved - approval_mode: {approvalMode}, server_label: {mcpServerLabel}");
            Console.WriteLine($"[Handler] Bearer token: {(bearerToken is not null ? $"received ({bearerToken.Length} chars, starts with: {bearerToken[..Math.Min(20, bearerToken.Length)]}...)" : "NULL - NOT FOUND")}");

            if (bearerToken is null)
            {
                Console.WriteLine("[Handler] ERROR: No MCP bearer token. MCP tools will fail.");
            }

            // Create a thread for this request
            thread = await _client.Threads.CreateThreadAsync(cancellationToken: cancellationToken);
            Console.WriteLine($"[Handler] Thread created: {thread.Id}");

            // Add user message to thread
            await _client.Messages.CreateMessageAsync(
                thread.Id,
                MessageRole.User,
                context.UserText ?? "",
                cancellationToken: cancellationToken);

            // Build MCP tool resources with bearer token and approval mode
            ToolResources? toolResources = null;
            if (bearerToken is not null)
            {
                var mcpToolResource = new MCPToolResource(mcpServerLabel)
                {
                    RequireApproval = new MCPApproval(approvalMode)
                };
                mcpToolResource.UpdateHeader("Authorization", $"Bearer {bearerToken}");
                toolResources = mcpToolResource.ToToolResources();
                Console.WriteLine($"[Handler] MCPToolResource configured with bearer token and approval_mode={approvalMode}");
            }

            // Create and run the agent
            ThreadRun run = await _client.Runs.CreateRunAsync(
                thread,
                _agent,
                toolResources,
                cancellationToken: cancellationToken);

            // Wait for completion
            run = await WaitForRunCompletionAsync(thread.Id, run.Id, cancellationToken);

            if (run.Status != RunStatus.Completed)
            {
                string errorMsg = run.LastError?.Message ?? $"Run failed with status: {run.Status}";
                Console.Error.WriteLine($"[Handler] Run failed: {errorMsg}");
                await taskUpdater.FailAsync(new Message
                {
                    Role = Role.Agent,
                    Parts = [Part.FromText($"Error: {errorMsg}")]
                }, cancellationToken);
                return;
            }

            // Get the latest assistant response
            string responseText = await GetLatestResponseAsync(thread.Id, cancellationToken);
            Console.WriteLine($"[Handler] Got response: {responseText[..Math.Min(100, responseText.Length)]}...");

            await taskUpdater.CompleteAsync(new Message
            {
                Role = Role.Agent,
                Parts = [Part.FromText(responseText)]
            }, cancellationToken);
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

    private async Task<ThreadRun> WaitForRunCompletionAsync(string threadId, string runId, CancellationToken cancellationToken)
    {
        ThreadRun run;
        do
        {
            await Task.Delay(500, cancellationToken);
            run = await _client.Runs.GetRunAsync(threadId, runId, cancellationToken);

            // Handle RequiresAction — auto-approve MCP tool calls
            if (run.Status == RunStatus.RequiresAction && run.RequiredAction is SubmitToolOutputsAction toolAction)
            {
                Console.WriteLine($"[Handler] Run requires action — auto-approving {toolAction.ToolCalls.Count} tool call(s)");

                var toolOutputs = new List<ToolOutput>();
                foreach (var toolCall in toolAction.ToolCalls)
                {
                    Console.WriteLine($"[Handler]   Auto-approving tool: {toolCall.Id}");
                    toolOutputs.Add(new ToolOutput(toolCall.Id, "approved"));
                }

                run = await _client.Runs.SubmitToolOutputsToRunAsync(run, toolOutputs, cancellationToken: cancellationToken);
            }
        } while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress);
        return run;
    }

    private Task<string> GetLatestResponseAsync(string threadId, CancellationToken cancellationToken)
    {
        var messages = _client.Messages.GetMessages(threadId, limit: 1, order: ListSortOrder.Descending);
        foreach (var msg in messages)
        {
            if (msg.Role == MessageRole.Agent)
            {
                var sb = new StringBuilder();
                foreach (var content in msg.ContentItems)
                {
                    if (content is MessageTextContent textContent)
                    {
                        sb.Append(textContent.Text);
                    }
                }
                return Task.FromResult(sb.ToString());
            }
        }
        return Task.FromResult("(No response)");
    }

    private static string? GetMetadataString(RequestContext context, string key)
    {
        if (context.Metadata?.TryGetValue(key, out var value) == true)
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        }
        return null;
    }
}

