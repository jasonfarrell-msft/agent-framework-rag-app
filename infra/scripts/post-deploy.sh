#!/usr/bin/env bash
#
# post-deploy.sh
# Operations that cannot be performed via Bicep (search index schema, SWA config, etc.)
#
# Usage:
#   ./infra/scripts/post-deploy.sh <resource-group> [search-index-name]
#
set -euo pipefail

RESOURCE_GROUP="${1:?Usage: $0 <resource-group> [search-index-name]}"
SEARCH_INDEX_NAME="${2:-multimodal-rag-1771601932521-single-manual}"

echo "=== Post-deployment tasks ==="
echo "Resource group: ${RESOURCE_GROUP}"

# ─── 1. Discover resource names from the resource group ────────────────────────

echo ""
echo "--- Discovering resources ---"

SEARCH_SERVICE_NAME=$(az search service list \
  --resource-group "${RESOURCE_GROUP}" \
  --query "[0].name" -o tsv)

WEB_APP_NAME=$(az webapp list \
  --resource-group "${RESOURCE_GROUP}" \
  --query "[0].name" -o tsv)

SWA_NAME=$(az staticwebapp list \
  --resource-group "${RESOURCE_GROUP}" \
  --query "[0].name" -o tsv)

OPENAI_ACCOUNT_NAME=$(az cognitiveservices account list \
  --resource-group "${RESOURCE_GROUP}" \
  --query "[?kind=='OpenAI'] | [0].name" -o tsv)

COSMOS_ACCOUNT_NAME=$(az cosmosdb list \
  --resource-group "${RESOURCE_GROUP}" \
  --query "[0].name" -o tsv)

echo "Search service:  ${SEARCH_SERVICE_NAME}"
echo "Web app:         ${WEB_APP_NAME}"
echo "Static web app:  ${SWA_NAME}"
echo "OpenAI account:  ${OPENAI_ACCOUNT_NAME}"
echo "Cosmos account:  ${COSMOS_ACCOUNT_NAME}"

# ─── 2. Verify App Service System Assigned Managed Identity ────────────────────

if [[ -n "${WEB_APP_NAME}" ]]; then
  echo ""
  echo "--- Verifying App Service Managed Identity ---"

  PRINCIPAL_ID=$(az webapp identity show \
    --name "${WEB_APP_NAME}" \
    --resource-group "${RESOURCE_GROUP}" \
    --query "principalId" -o tsv 2>/dev/null || echo "")

  if [[ -z "${PRINCIPAL_ID}" ]]; then
    echo "System Assigned Identity not found. Enabling it..."
    PRINCIPAL_ID=$(az webapp identity assign \
      --name "${WEB_APP_NAME}" \
      --resource-group "${RESOURCE_GROUP}" \
      --query "principalId" -o tsv)
  fi

  echo "Managed Identity Principal ID: ${PRINCIPAL_ID}"
fi

# ─── 3. Assign roles to the App Service Managed Identity ──────────────────────

if [[ -n "${PRINCIPAL_ID}" ]]; then
  echo ""
  echo "--- Assigning roles to App Service Managed Identity ---"

  # Cognitive Services OpenAI User on the OpenAI account
  if [[ -n "${OPENAI_ACCOUNT_NAME}" ]]; then
    OPENAI_RESOURCE_ID=$(az cognitiveservices account show \
      --name "${OPENAI_ACCOUNT_NAME}" \
      --resource-group "${RESOURCE_GROUP}" \
      --query "id" -o tsv)

    echo "Assigning 'Cognitive Services OpenAI User' on ${OPENAI_ACCOUNT_NAME}..."
    az role assignment create \
      --assignee-object-id "${PRINCIPAL_ID}" \
      --assignee-principal-type ServicePrincipal \
      --role "Cognitive Services OpenAI User" \
      --scope "${OPENAI_RESOURCE_ID}" \
      2>/dev/null && echo "  OK" || echo "  Already assigned or failed."
  fi

  # Search Index Data Reader on the Search service
  if [[ -n "${SEARCH_SERVICE_NAME}" ]]; then
    SEARCH_RESOURCE_ID=$(az search service show \
      --name "${SEARCH_SERVICE_NAME}" \
      --resource-group "${RESOURCE_GROUP}" \
      --query "id" -o tsv)

    echo "Assigning 'Search Index Data Reader' on ${SEARCH_SERVICE_NAME}..."
    az role assignment create \
      --assignee-object-id "${PRINCIPAL_ID}" \
      --assignee-principal-type ServicePrincipal \
      --role "Search Index Data Reader" \
      --scope "${SEARCH_RESOURCE_ID}" \
      2>/dev/null && echo "  OK" || echo "  Already assigned or failed."

    echo "Assigning 'Search Service Contributor' on ${SEARCH_SERVICE_NAME}..."
    az role assignment create \
      --assignee-object-id "${PRINCIPAL_ID}" \
      --assignee-principal-type ServicePrincipal \
      --role "Search Service Contributor" \
      --scope "${SEARCH_RESOURCE_ID}" \
      2>/dev/null && echo "  OK" || echo "  Already assigned or failed."
  fi

  # DocumentDB Account Contributor (control plane) on Cosmos DB
  if [[ -n "${COSMOS_ACCOUNT_NAME}" ]]; then
    COSMOS_RESOURCE_ID=$(az cosmosdb show \
      --name "${COSMOS_ACCOUNT_NAME}" \
      --resource-group "${RESOURCE_GROUP}" \
      --query "id" -o tsv)

    echo "Assigning 'DocumentDB Account Contributor' on ${COSMOS_ACCOUNT_NAME}..."
    az role assignment create \
      --assignee-object-id "${PRINCIPAL_ID}" \
      --assignee-principal-type ServicePrincipal \
      --role "DocumentDB Account Contributor" \
      --scope "${COSMOS_RESOURCE_ID}" \
      2>/dev/null && echo "  OK" || echo "  Already assigned or failed."

    # Cosmos DB Built-in Data Contributor (data plane — requires cosmosdb sql role assignment)
    echo "Assigning Cosmos DB Built-in Data Contributor (data plane) on ${COSMOS_ACCOUNT_NAME}..."
    az cosmosdb sql role assignment create \
      --account-name "${COSMOS_ACCOUNT_NAME}" \
      --resource-group "${RESOURCE_GROUP}" \
      --role-definition-id "00000000-0000-0000-0000-000000000002" \
      --principal-id "${PRINCIPAL_ID}" \
      --scope "${COSMOS_RESOURCE_ID}" \
      2>/dev/null && echo "  OK" || echo "  Already assigned or failed."
  fi
fi

# ─── 4. Get the backend URL and configure the SWA to proxy to it ───────────────

if [[ -n "${WEB_APP_NAME}" && -n "${SWA_NAME}" ]]; then
  BACKEND_URL="https://${WEB_APP_NAME}.azurewebsites.net"
  echo ""
  echo "--- Linking SWA to backend ---"
  echo "Backend URL: ${BACKEND_URL}"

  # Azure Static Web Apps can link a backend via CLI
  az staticwebapp backends link \
    --name "${SWA_NAME}" \
    --resource-group "${RESOURCE_GROUP}" \
    --backend-resource-id "$(az webapp show \
      --name "${WEB_APP_NAME}" \
      --resource-group "${RESOURCE_GROUP}" \
      --query id -o tsv)" \
    --backend-region "$(az webapp show \
      --name "${WEB_APP_NAME}" \
      --resource-group "${RESOURCE_GROUP}" \
      --query location -o tsv)" \
    2>/dev/null || echo "Backend link already exists or SWA CLI extension not available. Configure manually if needed."
fi

# ─── 5. Update App Service CORS to include the SWA hostname ───────────────────

if [[ -n "${WEB_APP_NAME}" && -n "${SWA_NAME}" ]]; then
  SWA_HOSTNAME=$(az staticwebapp show \
    --name "${SWA_NAME}" \
    --resource-group "${RESOURCE_GROUP}" \
    --query "defaultHostname" -o tsv)

  echo ""
  echo "--- Updating App Service CORS ---"
  echo "Adding SWA origin: https://${SWA_HOSTNAME}"

  az webapp cors add \
    --name "${WEB_APP_NAME}" \
    --resource-group "${RESOURCE_GROUP}" \
    --allowed-origins "https://${SWA_HOSTNAME}" \
    2>/dev/null || echo "CORS update failed. Update manually via portal."
fi

# ─── 6. Verify search index exists ────────────────────────────────────────────

if [[ -n "${SEARCH_SERVICE_NAME}" ]]; then
  echo ""
  echo "--- Verifying search index ---"

  SEARCH_ADMIN_KEY=$(az search admin-key show \
    --service-name "${SEARCH_SERVICE_NAME}" \
    --resource-group "${RESOURCE_GROUP}" \
    --query "primaryKey" -o tsv 2>/dev/null || echo "")

  if [[ -n "${SEARCH_ADMIN_KEY}" ]]; then
    INDEX_EXISTS=$(curl -s -o /dev/null -w "%{http_code}" \
      "https://${SEARCH_SERVICE_NAME}.search.windows.net/indexes/${SEARCH_INDEX_NAME}?api-version=2024-07-01" \
      -H "api-key: ${SEARCH_ADMIN_KEY}")

    if [[ "${INDEX_EXISTS}" == "200" ]]; then
      echo "Search index '${SEARCH_INDEX_NAME}' exists."
    else
      echo "WARNING: Search index '${SEARCH_INDEX_NAME}' does NOT exist (HTTP ${INDEX_EXISTS})."
      echo "You must create the index separately (e.g., via the Azure Portal or a data ingestion pipeline)."
      echo "The index requires at minimum these fields:"
      echo "  - document_title (Edm.String, filterable, retrievable)"
      echo "  - content_text (Edm.String, searchable, retrievable)"
      echo "  - content_embedding (Collection(Edm.Single), searchable, vector dimension as appropriate)"
    fi
  else
    echo "Could not retrieve search admin key. Verify index exists manually."
  fi
fi

# ─── 7. Print deployment summary ──────────────────────────────────────────────

echo ""
echo "=== Deployment Summary ==="

if [[ -n "${WEB_APP_NAME}" ]]; then
  echo "Backend API:      https://${WEB_APP_NAME}.azurewebsites.net/api/chat"
fi

if [[ -n "${SWA_NAME}" ]]; then
  SWA_HOSTNAME=$(az staticwebapp show \
    --name "${SWA_NAME}" \
    --resource-group "${RESOURCE_GROUP}" \
    --query "defaultHostname" -o tsv 2>/dev/null || echo "unknown")
  echo "Frontend (SWA):   https://${SWA_HOSTNAME}"
fi

echo ""
echo "Done."
