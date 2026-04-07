# Agent Framework RAG App

A Retrieval-Augmented Generation (RAG) chat application built with the Microsoft Agent Framework. The app enables electrical field workers to query technical manuals through a conversational interface, powered by Azure OpenAI, Azure AI Search, and Cosmos DB for conversation history.

---

## Key Points

- **Backend**: ASP.NET Core 9 Minimal API (`Example.ChatApi`) using the [Microsoft Agent Framework](https://github.com/microsoft/agents) for orchestrating LLM-powered chat with RAG context.
- **Frontend**: React 19 + Vite SPA with Bootstrap styling and Markdown rendering.
- **RAG Pipeline**: User queries trigger a hybrid search (keyword + vector) against Azure AI Search. The top results are injected into the agent context before the LLM generates a grounded response.
- **Conversation History**: Persisted in Cosmos DB via `CosmosChatHistoryProvider`, keyed by `conversationId`. Chat history is automatically loaded on subsequent requests in the same conversation.
- **Authentication**: All Azure service calls use `DefaultAzureCredential` (Managed Identity in production, Azure CLI/VS Code credentials locally). No API keys are stored in code or config.
- **Infrastructure as Code**: Full Azure deployment defined in Bicep (`infra/`), with a post-deploy script for role assignments.
- **Deployed URL**:
  - Backend API: `https://app-pseg-main-eus2-mx01.azurewebsites.net/api/chat`

---

## Configuration Hooks

### Backend (`Example.ChatApi/appsettings.json`)

| Setting | Description | Example |
|---|---|---|
| `AzureOpenAI__Endpoint` | Azure OpenAI resource endpoint | `https://aoi-pseg-main-eus2-mx01.openai.azure.com/` |
| `AzureOpenAI__DeploymentName` | Model deployment name | `gpt-4.1-deployment` |
| `CosmosDb__Endpoint` | Cosmos DB account endpoint | `https://cosmos-pseg-main-eus2-mx01.documents.azure.com:443/` |
| `CosmosDb__DatabaseId` | Cosmos DB database name | `chat-app` |
| `CosmosDb__ContainerId` | Cosmos DB container name | `conversations` |
| `AzureSearch__Endpoint` | Azure AI Search service endpoint | `https://search-techmanual-eus2-mx01.search.windows.net` |
| `AzureSearch__IndexName` | Search index name | `multimodal-rag-1771601932521-single-manual` |

All settings use the `__` (double underscore) separator so they can be overridden by environment variables or Azure App Service app settings.

### Frontend

| File | Variable | Description |
|---|---|---|
| `frontend/.env.development` | `VITE_API_URL` | API endpoint used during local dev (default: `/api/chat`, proxied by Vite) |
| `frontend/src/services/api.js` | Fallback `API_URL` | Production fallback: `https://app-pseg-main-eus2-mx01.azurewebsites.net/api/chat` |
| `frontend/vite.config.js` | `server.proxy` | Dev proxy — forwards `/api/*` to the Azure backend to avoid CORS issues |

### CORS (`Example.ChatApi/Program.cs`)

Allowed origins are configured in the backend CORS policy:
- `http://localhost:5173` — Vite dev server
- `https://app-pseg-main-eus2-mx01.azurewebsites.net` — deployed backend

The `post-deploy.sh` script handles role assignments at deploy time.

### Infrastructure (`infra/main.bicepparam`)

The `baseName` parameter drives all resource names via naming conventions:

| Parameter | Description | Current Value |
|---|---|---|
| `baseName` | Naming prefix for all resources | `pseg-main-eus2-mx02` |
| `location` | Azure region | `eastus2` |
| `openAiDeploymentName` | OpenAI deployment | `gpt-4.1-deployment` |
| `openAiModelName` | Model name | `gpt-4.1` |
| `openAiModelVersion` | Model version | `2025-04-14` |
| `cosmosDatabaseName` | Cosmos DB database | `chat-app` |
| `cosmosContainerName` | Cosmos DB container | `conversations` |
| `searchIndexName` | AI Search index | `multimodal-rag-1771601932521-single-manual` |

Resource names are derived from `baseName` using the pattern:
- App Service: `app-{baseName}`
- OpenAI: `aoi-{baseName}`
- Cosmos DB: `cosmos-{baseName}`
- Search: `search-{baseName}`
- Storage: `st{baseName}` (hyphens removed, max 24 chars)

---

## Setup Instructions

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 18+](https://nodejs.org/) and npm
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
- An Azure subscription with the following resources provisioned (or deploy via the Bicep templates):
  - Azure OpenAI (with a GPT-4.1 deployment)
  - Azure AI Search (with an indexed dataset)
  - Azure Cosmos DB (NoSQL API)
  - Azure App Service (Linux, .NET 9)

### 1. Clone the Repository

```bash
git clone <repository-url>
cd agent-framework-rag-app
```

### 2. Authenticate with Azure

```bash
az login
```

`DefaultAzureCredential` will use your Azure CLI session for local development.

### 3. Run the Backend Locally

```bash
cd Example.ChatApi
dotnet run
```

The API starts at `http://localhost:5001`. Ensure `appsettings.json` points to valid Azure resource endpoints.

### 4. Run the Frontend Locally

```bash
cd frontend
npm install
npm run dev
```

The Vite dev server starts at `http://localhost:5173`. The dev proxy in `vite.config.js` forwards `/api/*` requests to the Azure backend, so no local backend is required unless you want to test offline.

### 5. Deploy Infrastructure (Optional)

```bash
# Create the resource group
az group create --name rg-pseg-main-eus2-mx01 --location eastus2

# Deploy all resources
az deployment group create \
  --resource-group rg-pseg-main-eus2-mx01 \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam

# Run post-deployment tasks (role assignments, etc.)
./infra/scripts/post-deploy.sh rg-pseg-main-eus2-mx01
```

### 6. Deploy the Backend

```bash
cd Example.ChatApi
dotnet publish -c Release -o ../publish
cd ..
zip -r publish.zip publish/
az webapp deploy \
  --name app-pseg-main-eus2-mx01 \
  --resource-group rg-pseg-main-eus2-mx01 \
  --src-path publish.zip \
  --type zip
```

### 7. Deploy the Frontend

Build the production bundle and serve it from your preferred hosting platform:

```bash
cd frontend
npm run build
```

The `dist/` folder contains the static assets ready for deployment.

---

## Project Structure

```
agent-framework-rag-app/
├── Example.ChatApi/           # .NET 9 backend API
│   ├── Program.cs             # Minimal API entry point & route definitions
│   ├── Services/
│   │   ├── ChatService.cs     # Agent Framework orchestration, Cosmos history, LLM calls
│   │   └── SearchService.cs   # Azure AI Search hybrid queries
│   ├── appsettings.json       # Production configuration (Azure endpoints)
│   └── Properties/
│       └── launchSettings.json # Local dev server settings (ports)
├── frontend/                  # React + Vite SPA
│   ├── src/
│   │   ├── App.jsx            # Main chat UI component
│   │   ├── components/
│   │   │   └── ChatMessage.jsx # Message rendering with Markdown support
│   │   └── services/
│   │       └── api.js         # Backend API client
│   ├── .env.development       # Dev environment variables
│   └── vite.config.js         # Vite config with API proxy
├── infra/                     # Azure infrastructure
│   ├── main.bicep             # Root Bicep template
│   ├── main.bicepparam        # Parameter values
│   ├── modules/               # Modular Bicep definitions
│   │   ├── app-service.bicep
│   │   ├── cosmos.bicep
│   │   ├── openai.bicep
│   │   ├── role-assignments.bicep
│   │   ├── search.bicep
│   │   └── storage.bicep
│   └── scripts/
│       └── post-deploy.sh     # Post-deploy role assignments
└── agent-framework-rag-app.sln
```
