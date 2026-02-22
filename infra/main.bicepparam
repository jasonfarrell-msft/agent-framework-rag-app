using 'main.bicep'

param baseName = 'pseg-main-eus2-mx01'

param location = 'eastus2'

// Object ID of the developer / deploying user for local RBAC grants.
// Set to empty string ('') to skip user-level role assignments.
param userPrincipalId = '61a37498-9ab6-43d2-b70f-706fd58274e7'

param openAiDeploymentName = 'gpt-4.1-deployment'
param openAiModelName = 'gpt-4.1'
param openAiModelVersion = '2025-04-14'

param cosmosDatabaseName = 'chat-app'
param cosmosContainerName = 'conversations'

param searchIndexName = 'multimodal-rag-1771601932521-single-manual'

param tags = {
  project: 'agent-framework-rag-app'
  environment: 'dev'
}
