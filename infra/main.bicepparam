using 'main.bicep'

param baseName = 'pseg-main-eus2-mx02'

param location = 'eastus2'

param openAiDeploymentName = 'gpt-4.1-deployment'
param openAiModelName = 'gpt-4.1'
param openAiModelVersion = '2025-04-14'

param cosmosDatabaseName = 'chat-app'
param cosmosContainerName = 'conversations'

param searchIndexName = 'multimodal-rag-1771601932521-single-manual'

param tags = {
  project: 'agent-framework-rag-app'
  environment: 'dev' 
  SecurityControl: 'Ignore'
}
