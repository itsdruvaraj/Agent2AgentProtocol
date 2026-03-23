# A2A Server Sample

Shows how to create an A2A Server which makes an agent available via the A2A protocol.

## Run the Sample

### Configuring for use with Chat Completion Agents

Provide your OpenAI API key via an environment variable:

```powershell
$env:OPENAI_API_KEY="<Your OpenAI API Key>"
```

Use the following commands to run each A2A server:

Execute the following command to build the sample:

```powershell
cd A2AServer
dotnet build
```

```bash
dotnet run --urls "http://localhost:5000;https://localhost:5010" --agentType "invoice" --no-build
```

```bash
dotnet run --urls "http://localhost:5001;https://localhost:5011" --agentType "policy" --no-build
```

```bash
dotnet run --urls "http://localhost:5002;https://localhost:5012" --agentType "logistics" --no-build
```

### Configuring for use with Azure AI Agents

For this sample you will need to pre-create three agents in Azure Foundry. 

1. InvoiceAgent with the following system prompt:
    ```
    You specialize in handling queries related to invoices.
    ```

2. PolicyAgent with the following system prompt:
    ```
    You specialize in handling queries related to policies and customer communications.
    
    Always reply with exactly this text:
    
    Policy: Short Shipment Dispute Handling Policy V2.1
    
    Summary:
    "For short shipments reported by customers, first verify internal shipment records
    (SAP) and physical logistics scan data (BigQuery). If discrepancy is confirmed and logistics data
    shows fewer items packed than invoiced, issue a credit for the missing items. Document the
    resolution in SAP CRM and notify the customer via email within 2 business days, referencing the
    original invoice and the credit memo number. Use the 'Formal Credit Notification' email
    template."
    ```

3. LogisticsAgent with the following system prompt:
    ```
    You specialize in handling queries related to logistics.

    Always reply with exactly:

        Shipment number: SHPMT-SAP-001
        Item: TSHIRT-RED-L
        Quantity: 900"
    ```

```powershell
$env:AZURE_FOUNDRY_PROJECT_ENDPOINT="https://ai-foundry-your-project.services.ai.azure.com/api/projects/ai-proj-ga-your-project" # Replace with your Foundry Project endpoint
```

Use the following commands to run each A2A server:

```bash
dotnet run --urls "http://localhost:5000;https://localhost:5010" --agentId "<Invoice Agent Id>" --agentType "invoice" --no-build
```

```bash
dotnet run --urls "http://localhost:5001;https://localhost:5011" --agentId "<Policy Agent Id>" --agentType "policy" --no-build
```

```bash
dotnet run --urls "http://localhost:5002;https://localhost:5012" --agentId "<Logistics Agent Id>" --agentType "logistics" --no-build
```

## Testing the Agents

Query agent card:
```
GET http://localhost:5000/.well-known/agent-card.json
```

Send a message:
```
POST http://localhost:5000
Content-Type: application/json

{
    "id": "1",
    "jsonrpc": "2.0",
    "method": "message/send",
    "params": {
        "id": "12345",
        "message": {
            "kind": "message",
            "role": "user",
            "parts": [
                {
                    "kind": "text",
                    "text": "Show me all invoices for Contoso?"
                }
            ]
        }
    }
}
```
