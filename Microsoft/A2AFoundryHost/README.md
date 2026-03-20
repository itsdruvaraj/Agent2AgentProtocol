# A2A Foundry Agent Host

An ASP.NET Core application that hosts an Azure AI Foundry agent via the [A2A (Agent-to-Agent) protocol](https://google.github.io/A2A/).

## Prerequisites

- .NET 10 SDK
- Azure AI Foundry project with a deployed agent
- Azure CLI authenticated (`az login`)

## Configuration

Update `appsettings.json` or set environment variables:

```powershell
$env:AZURE_FOUNDRY_PROJECT_ENDPOINT = "https://your-project.services.ai.azure.com/api/projects/your-project"
$env:AZURE_FOUNDRY_AGENT_ID = "your-agent-id"
```

### MCP Tool Authorization

If your agent uses MCP tools that require Bearer token authentication, configure the token **in Azure AI Foundry** when creating/updating the agent, or use `HostedMcpServerTool.AuthorizationToken` when creating a new agent programmatically.

> **Note:** Runtime header injection for pre-existing Foundry agents is not currently supported in the .NET SDK. Headers must be configured at agent creation time.

## Run

```bash
dotnet run
```

The server starts on the URLs configured in `Properties/launchSettings.json` (default: `https://localhost:57694` and `http://localhost:57695`).

## Endpoints

| Endpoint | Description |
|---|---|
| `/.well-known/agent-card.json` | Agent card discovery (A2A v1) |
| `/` | A2A JSON-RPC message endpoint |

## Testing

Use the companion **A2AFoundryClient** project to test:

```bash
cd ../A2AFoundryClient
dotnet run
```

### Get Agent Card (curl)

```bash
curl http://localhost:57695/.well-known/agent-card.json
```

### Send a Message (curl)

```bash
curl -X POST http://localhost:57695/ \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "method": "SendMessage",
    "id": "1",
    "params": {
      "message": {
        "role": "ROLE_USER",
        "parts": [{"text": "Hello, what can you help me with?"}]
      }
    }
  }'
```

## A2A v1 SDK — Known Limitations

This project uses the `A2A` and `A2A.AspNetCore` NuGet packages at version **1.0.0-preview**, which implement the **A2A v1 specification**. Be aware of the following:

### Not compatible with the Python A2A Inspector

The A2A v1 spec introduced breaking changes from v0.3. The Python-based [A2A Inspector](https://github.com/google/A2A/tree/main/samples/python/hosts/inspector) still uses v0.3 conventions and **will not work** with this server. Key differences:

| Area | v0.3 (Python Inspector) | v1 (This project) |
|---|---|---|
| JSON-RPC methods | `message/send`, `message/stream` | `SendMessage`, `SendStreamingMessage` |
| Agent card path | `/.well-known/agent.json` | `/.well-known/agent-card.json` |
| Agent card `url` field | Required top-level field | Removed (use `SupportedInterfaces`) |
| Enum values | kebab-case (`working`) | SCREAMING_SNAKE_CASE (`TASK_STATE_WORKING`) |
| Part model | `TextPart`, `FilePart` subclasses | Single `Part` class with `ContentCase` |

See the [migration guide](https://github.com/a2aproject/a2a-dotnet/blob/main/docs/migration-guide-v1.md) for full details.

### Package version constraints

- `Microsoft.Extensions.AI` must be pinned to **10.3.0** (not 10.4.x) to match the transitive dependency from `Microsoft.Agents.AI` 1.0.0-rc4. Using 10.4.x causes a runtime `TypeLoadException` for `McpServerToolApprovalResponseContent`.
- `Microsoft.Agents.AI` is pulled transitively at **1.0.0-rc4** by `Microsoft.Agents.AI.AzureAI.Persistent`. Adding it explicitly at `1.0.0-rc4` avoids version conflicts.

## Architecture

```
┌─────────────────────┐         ┌─────────────────────┐
│   A2AFoundryClient  │  A2A    │   A2AFoundryHost    │
│   (.NET Client)     │────────►│   (This Project)    │
│                     │  v1     │                     │
│  - A2AClient        │         │  - FoundryAgentHandler
│  - Interactive CLI  │         │  - AutoApprovingAgent
└─────────────────────┘         └─────────────────────┘
                                          │
                                          ▼
                                ┌─────────────────────┐
                                │  Azure AI Foundry   │
                                │  Persistent Agent   │
                                └─────────────────────┘
```
