// A2A Host for Azure AI Foundry Agent using the new Responses API
// Uses Azure.AI.Projects + Azure.AI.Extensions.OpenAI (ProjectResponsesClient)

#pragma warning disable OPENAI001

using A2A;
using A2A.AspNetCore;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using OpenAI.Responses;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient().AddLogging();

var app = builder.Build();

// Configuration
string endpoint = builder.Configuration["AZURE_FOUNDRY_PROJECT_ENDPOINT"]
    ?? throw new InvalidOperationException("AZURE_FOUNDRY_PROJECT_ENDPOINT is required");
string agentName = builder.Configuration["AZURE_FOUNDRY_AGENT_NAME"] ?? "MCP-ManagedIdentity";

// Create Foundry project client and verify agent exists
var credential = new AzureCliCredential();
var projectClient = new AIProjectClient(new Uri(endpoint), credential);
AgentRecord agentRecord = await projectClient.Agents.GetAgentAsync(agentName);

Console.WriteLine($"Loaded agent from Foundry:");
Console.WriteLine($"  Name: {agentRecord.Name}");
Console.WriteLine($"  ID: {agentRecord.Id}");

// Define the agent card for A2A discovery
var agentCard = new AgentCard
{
    Name = agentRecord.Name ?? "Foundry Agent",
    Description = $"Azure AI Foundry Agent ({agentName}) via Responses API",
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
            Name = agentRecord.Name ?? "Foundry Agent",
            Description = $"Azure AI Foundry Agent using Responses API",
            Tags = ["foundry", "ai-agent", "responses-api"],
            Examples = ["What can you help me with?"]
        }
    ]
};

// Create A2A server with our agent handler
var agentHandler = new FoundryAgentHandler(projectClient, agentName);
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

// IAgentHandler implementation that bridges Foundry Responses API to A2A protocol
public class FoundryAgentHandler : IAgentHandler
{
    private readonly AIProjectClient _projectClient;
    private readonly string _agentName;

    public FoundryAgentHandler(AIProjectClient projectClient, string agentName)
    {
        _projectClient = projectClient;
        _agentName = agentName;
    }

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue events, CancellationToken cancellationToken)
    {
        var taskUpdater = new TaskUpdater(events, context.TaskId ?? Guid.NewGuid().ToString(), context.ContextId ?? "");
        await taskUpdater.StartWorkAsync(new Message { Role = Role.Agent, Parts = [Part.FromText("Processing...")] }, cancellationToken);

        try
        {
            Console.WriteLine($"[Handler] Received request: '{context.UserText}' (streaming={context.StreamingResponse})");

            // Create a conversation for this request
            ProjectConversation conversation = _projectClient.OpenAI.Conversations.CreateProjectConversation();
            Console.WriteLine($"[Handler] Created conversation: {conversation.Id}");

            // Get responses client for this agent + conversation
            ProjectResponsesClient responsesClient = _projectClient.OpenAI.GetProjectResponsesClientForAgent(
                defaultAgent: _agentName,
                defaultConversationId: conversation.Id);

            Console.WriteLine($"[Handler] Calling CreateResponse (sync)...");
            ResponseResult response = responsesClient.CreateResponse(context.UserText ?? "");
            string outputText = response.GetOutputText();
            Console.WriteLine($"[Handler] Got response: {outputText[..Math.Min(100, outputText.Length)]}...");

            await taskUpdater.CompleteAsync(new Message
            {
                Role = Role.Agent,
                Parts = [Part.FromText(outputText)]
            }, cancellationToken);
            Console.WriteLine($"[Handler] Completed task");
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
        finally
        {
            events.Complete();
        }
    }

    public Task CancelAsync(RequestContext context, AgentEventQueue events, CancellationToken cancellationToken)
    {
        events.Complete();
        return Task.CompletedTask;
    }
}
