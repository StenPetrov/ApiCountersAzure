#!/usr/bin/env bash
# deploy.sh — Build, publish and deploy ApiCounters to Azure
# Usage: ./deploy/deploy.sh <resource-group> [base-name] [location]
#
# Prerequisites:
#   - Azure CLI (az) installed and logged in
#   - dotnet SDK 10.0+

set -euo pipefail

RESOURCE_GROUP="${1:?Usage: $0 <resource-group> [base-name] [location]}"
BASE_NAME="${2:-apicounters}"
LOCATION="${3:-eastus}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PUBLISH_DIR="${REPO_ROOT}/publish"

echo "=== Deploying ApiCounters ==="
echo "Resource Group : ${RESOURCE_GROUP}"
echo "Base Name      : ${BASE_NAME}"
echo "Location       : ${LOCATION}"

# 1. Ensure resource group exists
echo ""
echo "[1/4] Ensuring resource group '${RESOURCE_GROUP}' exists..."
az group create --name "${RESOURCE_GROUP}" --location "${LOCATION}" --output none

# 2. Deploy infrastructure via Bicep
echo ""
echo "[2/4] Deploying infrastructure..."
DEPLOY_OUTPUT=$(az deployment group create \
  --resource-group "${RESOURCE_GROUP}" \
  --template-file "${SCRIPT_DIR}/main.bicep" \
  --parameters baseName="${BASE_NAME}" location="${LOCATION}" \
  --output json)

FUNCTION_APP_NAME=$(echo "${DEPLOY_OUTPUT}" | python3 -c "import sys,json; print(json.load(sys.stdin)['properties']['outputs']['functionAppName']['value'])")
echo "Function App   : ${FUNCTION_APP_NAME}"

# 3. Build and publish the function app
echo ""
echo "[3/4] Building and publishing the function app..."
rm -rf "${PUBLISH_DIR}"
dotnet publish "${REPO_ROOT}/src/ApiCounters/ApiCounters.csproj" \
  --configuration Release \
  --output "${PUBLISH_DIR}"

# Create a zip package
ZIP_FILE="${REPO_ROOT}/apicounters.zip"
(cd "${PUBLISH_DIR}" && zip -r "${ZIP_FILE}" .)

# 4. Deploy the zip package
echo ""
echo "[4/4] Deploying application package..."
az functionapp deployment source config-zip \
  --resource-group "${RESOURCE_GROUP}" \
  --name "${FUNCTION_APP_NAME}" \
  --src "${ZIP_FILE}"

FUNCTION_APP_URL=$(echo "${DEPLOY_OUTPUT}" | python3 -c "import sys,json; print(json.load(sys.stdin)['properties']['outputs']['functionAppUrl']['value'])")
echo ""
echo "=== Deployment complete ==="
echo "Function App URL: ${FUNCTION_APP_URL}"
echo ""
echo "Example usage:"
echo "  POST: curl -X POST \"${FUNCTION_APP_URL}/api/counter/myCounter\" \\"
echo "             -H \"key-id: $(uuidgen)\" \\"
echo "             -H \"counter-dimensions: region:us-east-1,env:prod\""
echo ""
echo "  GET:  curl \"${FUNCTION_APP_URL}/api/counter/myCounter\""
