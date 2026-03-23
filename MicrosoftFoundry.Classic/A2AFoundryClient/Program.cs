// A2A Client for invoking Azure AI Foundry Agent Host
// This client connects to the A2AFoundryHost server and invokes the agent using the A2A protocol

using A2A;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

// Configuration
IConfigurationRoot configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>(optional: true)
    .Build();

string serverUrl = configuration["A2A_SERVER_URL"] ?? "http://localhost:57695";
string mcpApprovalMode = configuration["MCP_APPROVAL_MODE"] ?? "never";
string? mcpBearerToken = configuration["MCP_BEARER_TOKEN"];

// Treat empty string as null
if (string.IsNullOrWhiteSpace(mcpBearerToken))
{
    mcpBearerToken = null;
}

Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║        A2A Foundry Client - Agent-to-Agent Protocol      ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine($"Connecting to A2A server at: {serverUrl}");
Console.WriteLine($"MCP approval mode: {mcpApprovalMode}");
Console.WriteLine($"MCP bearer token: {(mcpBearerToken is not null ? $"configured ({mcpBearerToken.Length} chars, starts: {mcpBearerToken[..Math.Min(20, mcpBearerToken.Length)]}...)" : "NOT SET - MCP tools will fail!")}");
Console.WriteLine();

// Create the A2A card resolver using the well-known agent card location
A2ACardResolver agentCardResolver = new(new Uri(serverUrl));

// Get the agent card to discover capabilities
Console.WriteLine("Fetching agent card...");
AgentCard agentCard = await agentCardResolver.GetAgentCardAsync();

Console.WriteLine();
Console.WriteLine("┌─ Agent Card ─────────────────────────────────────────────┐");
Console.WriteLine($"│ Name: {agentCard.Name,-52} │");
Console.WriteLine($"│ Description: {(agentCard.Description?.Length > 44 ? agentCard.Description[..44] + "..." : agentCard.Description ?? "N/A"),-47} │");
Console.WriteLine($"│ Version: {agentCard.Version,-49} │");
Console.WriteLine($"│ Streaming: {(agentCard.Capabilities?.Streaming == true ? "Yes" : "No"),-47} │");
Console.WriteLine("└──────────────────────────────────────────────────────────┘");
Console.WriteLine();

// Create an A2A client for sending messages
using var httpClient = new HttpClient();
using var a2aClient = new A2AClient(new Uri(serverUrl), httpClient);

Console.WriteLine("Agent is ready! Type your message or ':q' to quit.");
Console.WriteLine("──────────────────────────────────────────────────────────────");
Console.WriteLine();

try
{
    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("You: ");
        Console.ResetColor();
        
        string? userInput = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(userInput))
        {
            Console.WriteLine("(Empty input, please type a message)");
            continue;
        }

        if (userInput.Equals(":q", StringComparison.OrdinalIgnoreCase) || 
            userInput.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
            userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\nGoodbye!");
            break;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("\nAgent: ");
        Console.ResetColor();

        try
        {
            // Build A2A metadata with MCP config
            var metadata = new Dictionary<string, JsonElement>
            {
                ["mcp_approval_mode"] = JsonSerializer.SerializeToElement(mcpApprovalMode)
            };

            // Always log what we're sending
            Console.WriteLine($"[DEBUG] mcpBearerToken is null: {mcpBearerToken is null}");
            Console.WriteLine($"[DEBUG] mcpBearerToken length: {mcpBearerToken?.Length ?? 0}");

            if (mcpBearerToken is not null)
            {
                metadata["mcp_bearer_token"] = JsonSerializer.SerializeToElement(mcpBearerToken);
                Console.WriteLine($"[DEBUG] Added mcp_bearer_token to metadata ({mcpBearerToken.Length} chars)");
            }
            else
            {
                Console.WriteLine("[DEBUG] WARNING: No bearer token to send!");
            }

            Console.WriteLine($"[DEBUG] Metadata keys being sent: {string.Join(", ", metadata.Keys)}");

            // Check if streaming is supported
            if (agentCard.Capabilities?.Streaming == true)
            {
                // Build full request with metadata
                var request = new SendMessageRequest
                {
                    Message = new Message { Role = Role.User, Parts = [Part.FromText(userInput)] },
                    Metadata = metadata
                };

                // Stream the response
                await foreach (StreamResponse streamResponse in a2aClient.SendStreamingMessageAsync(request))
                {
                    if (streamResponse.StatusUpdate is { } statusUpdate)
                    {
                        if (statusUpdate.Status.Message?.Parts is { } parts)
                        {
                            foreach (var part in parts)
                            {
                                if (part.Text is { } text)
                                {
                                    Console.Write(text);
                                }
                            }
                        }
                    }
                    else if (streamResponse.ArtifactUpdate is { } artifactUpdate)
                    {
                        if (artifactUpdate.Artifact?.Parts is { } parts)
                        {
                            foreach (var part in parts)
                            {
                                if (part.Text is { } text)
                                {
                                    Console.Write(text);
                                }
                            }
                        }
                    }
                }
                Console.WriteLine();
            }
            else
            {
                // Build full request with metadata
                var request = new SendMessageRequest
                {
                    Message = new Message { Role = Role.User, Parts = [Part.FromText(userInput)] },
                    Metadata = metadata
                };

                // Non-streaming response
                SendMessageResponse response = await a2aClient.SendMessageAsync(request);
                if (response.Task is { } task)
                {
                    // Get text from artifacts
                    if (task.Artifacts is { } artifacts)
                    {
                        foreach (var artifact in artifacts)
                        {
                            foreach (var part in artifact.Parts)
                            {
                                if (part.Text is { } text)
                                {
                                    Console.Write(text);
                                }
                            }
                        }
                        Console.WriteLine();
                    }
                    else if (task.Status?.Message?.Parts is { } parts)
                    {
                        foreach (var part in parts)
                        {
                            if (part.Text is { } text)
                            {
                                Console.Write(text);
                            }
                        }
                        Console.WriteLine();
                    }
                }
                else if (response.Message is { } message)
                {
                    foreach (var part in message.Parts)
                    {
                        if (part.Text is { } text)
                        {
                            Console.Write(text);
                        }
                    }
                    Console.WriteLine();
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine();
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nFatal error: {ex.Message}");
    Console.ResetColor();
}

internal partial class Program { }
