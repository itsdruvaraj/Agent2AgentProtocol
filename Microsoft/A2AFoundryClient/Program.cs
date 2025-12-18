// A2A Client for invoking Azure AI Foundry Agent Host
// This client connects to the A2AFoundryHost server and invokes the agent using the A2A protocol

using A2A;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.A2A;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

// Configuration
IConfigurationRoot configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>(optional: true)
    .Build();

string serverUrl = configuration["A2A_SERVER_URL"] ?? "http://localhost:5000";

Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║        A2A Foundry Client - Agent-to-Agent Protocol      ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine($"Connecting to A2A server at: {serverUrl}");
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

// Create an AIAgent from the agent card
AIAgent agent = agentCard.GetAIAgent();

// Create a new conversation thread
AgentThread thread = agent.GetNewThread();
List<ChatMessage> messages = [];

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

        // Add user message to conversation
        messages.Add(new ChatMessage(ChatRole.User, userInput));

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("\nAgent: ");
        Console.ResetColor();

        try
        {
            // Check if streaming is supported
            if (agentCard.Capabilities?.Streaming == true)
            {
                // Stream the response
                await foreach (AgentRunResponseUpdate update in agent.RunStreamingAsync(messages, thread))
                {
                    foreach (AIContent content in update.Contents)
                    {
                        if (content is TextContent textContent)
                        {
                            Console.Write(textContent.Text);
                        }
                    }
                }
                Console.WriteLine();
            }
            else
            {
                // Non-streaming response
                AgentRunResponse response = await agent.RunAsync(messages, thread);
                Console.WriteLine(response.Text);
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
