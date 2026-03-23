# A2A Foundry Client

An interactive CLI client that connects to the A2AFoundryHost server using the [A2A (Agent-to-Agent) protocol](https://google.github.io/A2A/) to invoke Azure AI Foundry agents.

## Overview

This client demonstrates how to:
- Connect to an A2A-enabled agent host
- Discover agent capabilities via the AgentCard
- Send messages and receive responses (streaming and non-streaming)
- Maintain conversation context across multiple turns

## Prerequisites

- .NET 10 SDK
- A running A2AFoundryHost server

## Configuration

The client can be configured using `appsettings.json`, environment variables, or user secrets:

| Variable | Description | Default |
|----------|-------------|---------|
| `A2A_SERVER_URL` | URL of the A2A server | `http://localhost:57695` |

### Using Environment Variables

```powershell
$env:A2A_SERVER_URL = "http://localhost:57695"
```

### Using User Secrets

```bash
dotnet user-secrets set "A2A_SERVER_URL" "http://localhost:57695"
```

## Running the Client

1. First, ensure the A2AFoundryHost server is running:
   ```bash
   cd ../A2AFoundryHost
   dotnet run
   ```

2. Then start the client:
   ```bash
   dotnet run
   ```

3. The client will:
   - Connect to the A2A server
   - Fetch and display the agent card from `/.well-known/agent-card.json`
   - Start an interactive chat session

## Usage

Once running, you can:
- Type messages to interact with the agent
- Type `:q`, `quit`, or `exit` to end the session

## A2A v1 Protocol

This client uses the `A2A` NuGet package at version **1.0.0-preview** (A2A v1 spec), which provides:
- **Agent Discovery**: Fetch agent capabilities via `/.well-known/agent-card.json`
- **Message Exchange**: Send/receive messages via JSON-RPC (`SendMessage`, `SendStreamingMessage`)
- **Streaming Support**: Real-time SSE response streaming when supported by the agent

> **Note:** This client is **not compatible** with A2A v0.3 servers (which use `message/send` method names and `/.well-known/agent.json`). Both client and server must use the same A2A spec version. See the [migration guide](https://github.com/a2aproject/a2a-dotnet/blob/main/docs/migration-guide-v1.md) for details on v0.3 → v1 differences.

## Architecture

```
┌─────────────────────┐         ┌─────────────────────┐
│   A2AFoundryClient  │  A2A    │   A2AFoundryHost    │
│   (This Project)    │────────►│   (Agent Server)    │
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

## Related Projects

- **A2AFoundryHost**: The server component that hosts the Azure AI Foundry agent with A2A protocol support
