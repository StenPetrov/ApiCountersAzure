# ApiCounters

An Azure Functions application (dotnet isolated, v4) that provides HTTP-accessible named counters backed by Azure Table Storage.

## API

### POST `/api/counter/{counter-name}`

Records a counter event.

**Request headers:**

| Header | Required | Description |
|---|---|---|
| `key-id` | Yes | A GUID that uniquely identifies the caller/event |
| `counter-dimensions` | Yes | Comma-separated dimension pairs: `dim1:val1,dim2:val2` or `dim1=val1,dim2=val2` |
| `counter-value-append` | No | Optional value to add to the counter, default is 1 |
| `tracked-max` | No | Optional, positive floating point value to keep max of, default is 0.0 |

**Response:** `200 OK` with JSON body:
```json
{ "counterName": "myCounter", "keyId": "<guid>", "dimensions": { "region": "us-east-1", "env": "prod" }, "count": 3 }
```

### GET `/api/counter/{counter-name}`

Returns the total count of recorded events for the counter.

An optional `key-id` header limits the result to that caller across all dimensions.

**Response:** `200 OK` with JSON body:
```json
{ "counterName": "myCounter", "value": 12345145, "count": 42, "trackedMax": 65.3 }
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local) (for local development)
- [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) (for deployment)
- [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite) or Azure Storage Account (for local testing)

## Local development

1. **Install Azurite** for local storage emulation:
   ```bash
   npm install -g azurite
   azurite &
   ```

2. **Restore and build**:
   ```bash
   dotnet build
   ```

3. **Run locally** (requires Azure Functions Core Tools):
   ```bash
   cd src/ApiCounters
   func start
   ```

4. **Test locally**:
   ```bash
   # POST a counter event
   curl -X POST http://localhost:7071/api/counter/myCounter \
        -H "key-id: d14d435f-f690-7aff-e29f-2afbea68dd4e" \
        -H "counter-dimensions: pipeline:CL-JKJTSTNG1,providerType:otel,mode:enabled,fields:25,samplingBinMax:25,samplingTimeWindowMax:300s" \
        -H "counter-value-append: 5"

   # GET the counter value
   curl http://localhost:7071/api/counter/myCounter
   ```

## Running tests

```bash
dotnet test
```

All unit tests are in `tests/ApiCounters.Tests/`.

## Deployment

```bash
# Log in to Azure
az login

# Deploy (creates resource group if it doesn't exist)
bash deploy/deploy.sh <resource-group> [base-name] [location]

# PowerShell
.\deploy\deploy.ps1 <resource-group> [base-name] [location]

# Example
bash deploy/deploy.sh clvz-apicounters clvzcounters centralus

# PowerShell example
.\deploy\deploy.ps1 clvz-apicounters clvzcounters centralus
```

The deployment script:
1. Creates (or reuses) the resource group.
2. Deploys infrastructure via `deploy/main.bicep` (Storage Account, Consumption App Service Plan, Application Insights, Function App).
3. Publishes and zip-deploys the function app.

## Project structure

```
├── src/
│   └── ApiCounters/
│       ├── ApiCounters.csproj
│       ├── Program.cs               # DI / host setup
│       ├── host.json
│       ├── local.settings.json      # local dev settings (not deployed)
│       ├── Functions/
│       │   └── CounterFunction.cs   # HTTP trigger (GET + POST)
│       └── Models/
│           └── CounterEntity.cs     # Azure Table Storage entity
├── tests/
│   └── ApiCounters.Tests/
│       └── CounterFunctionTests.cs  # Unit tests
├── deploy/
│   ├── main.bicep                   # Azure infrastructure template
│   ├── deploy.sh                    # Bash deployment script
│   └── deploy.ps1                   # PowerShell deployment script
└── ApiCounters.sln
```

## Storage schema

Counter events are stored in the `counters` Azure Storage Table:

| Column | Value |
|---|---|
| `PartitionKey` | counter name |
| `RowKey` | key-id plus MD5 of canonical dimensions: `<guid>_<32-char-hash>` |
| `Dimensions` | serialized dimensions `dim1:val1,dim2:val2` |
| `Count` | number of POST calls for this key-id and dimensions combination |
| `CreatedAt` | UTC timestamp |