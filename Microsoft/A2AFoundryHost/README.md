# A2A Foundry Agent Host

A simple ASP.NET Core application that hosts an Azure AI Foundry agent via the A2A (Agent-to-Agent) protocol.

## Prerequisites

- .NET 10 SDK
- Azure AI Foundry project with a deployed agent
- Azure CLI authenticated (`az login`)

## Configuration

Set the following environment variables:

```powershell
$env:AZURE_FOUNDRY_PROJECT_ENDPOINT = "https://your-project.services.ai.azure.com/api/projects/your-project"
$env:AZURE_FOUNDRY_AGENT_ID = "your-agent-id"
```

### MCP Tool Authorization

If your agent uses MCP tools that require Bearer token authentication, configure the token **in Azure AI Foundry** when creating/updating the agent, or use `HostedMcpServerTool.AuthorizationToken` when creating a new agent programmatically.

> **Note:** Runtime header injection for pre-existing Foundry agents is not currently supported in the .NET SDK. Headers must be configured at agent creation time.

## Run

```bash
dotnet run --urls "http://localhost:5000"
```

## Endpoints

- `/v1/card` - Agent card discovery
- `/` - A2A message endpoint (JSON-RPC)

## Testing

### Get Agent Card

```bash
curl -k https://localhost:57694/v1/card
```

**Response:**
```json
{
  "name": "Foundry-MCP-Microsoft-Learn",
  "description": "Microsoft Learn Agent with A2A",
  "version": "1.0.0",
  "protocolVersion": "0.3.0",
  "capabilities": {
    "streaming": true,
    "pushNotifications": false,
    "stateTransitionHistory": false
  },
  "defaultInputModes": ["text"],
  "defaultOutputModes": ["text"],
  "preferredTransport": "JSONRPC"
}
```

### Send a Message

```bash
curl -k -X POST https://localhost:57694/ \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "method": "message/send",
    "id": "1",
    "params": {
      "message": {
        "kind": "message",
        "role": "user",
        "parts": [{"kind": "text", "text": "Hello, what can you help me with?"}],
        "messageId": "test-123"
      }
    }
  }'
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "result": {
    "kind": "message",
    "role": "agent",
    "parts": [{"kind": "text", "text": "Hello! I'm here to help with..."}],
    "messageId": "run_xxx",
    "contextId": "xxx"
  }
}
```

> **Note:** Use `-k` flag to skip SSL certificate verification for local development.
