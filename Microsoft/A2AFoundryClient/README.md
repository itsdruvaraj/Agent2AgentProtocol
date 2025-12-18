# A2A Foundry Client

A client application that connects to the A2AFoundryHost server using the Agent-to-Agent (A2A) protocol to invoke Azure AI Foundry agents.

## Overview

This client demonstrates how to:
- Connect to an A2A-enabled agent host
- Discover agent capabilities via the AgentCard
- Send messages and receive responses (streaming and non-streaming)
- Maintain conversation context across multiple turns

## Prerequisites

- .NET 10.0 SDK
- A running A2AFoundryHost server

## Configuration

The client can be configured using environment variables or user secrets:

| Variable | Description | Default |
|----------|-------------|---------|
| `A2A_SERVER_URL` | URL of the A2A server | `http://localhost:5000` |

### Using Environment Variables

```bash
export A2A_SERVER_URL=http://localhost:5000
```

### Using User Secrets

```bash
dotnet user-secrets set "A2A_SERVER_URL" "http://localhost:5000"
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
   - Fetch and display the agent card
   - Start an interactive chat session

## Usage

Once running, you can:
- Type messages to interact with the agent
- Type `:q`, `quit`, or `exit` to end the session

## A2A Protocol

This client uses the Agent-to-Agent (A2A) protocol which provides:
- **Agent Discovery**: Fetch agent capabilities via `/v1/card`
- **Message Exchange**: Send/receive messages via A2A endpoints
- **Streaming Support**: Real-time response streaming when supported by the agent

## Architecture

```
┌─────────────────────┐         ┌─────────────────────┐
│   A2AFoundryClient  │  A2A    │   A2AFoundryHost    │
│   (This Project)    │────────►│   (Agent Server)    │
│                     │         │                     │
│  - A2AClient        │         │  - AIAgent          │
│  - Interactive CLI  │         │  - AgentCard        │
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
