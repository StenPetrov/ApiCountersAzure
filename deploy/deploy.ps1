[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string] $ResourceGroup,

    [Parameter(Position = 1)]
    [string] $BaseName = 'apicounters',

    [Parameter(Position = 2)]
    [string] $Location = 'centralus'
)

# deploy.ps1 - Build, publish, and deploy ApiCounters to Azure
# Usage: .\deploy\deploy.ps1 <resource-group> [base-name] [location]
#
# Prerequisites:
#   - Azure CLI (az) installed and logged in
#   - .NET SDK 10.0+

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot 'publish'
$zipFile = Join-Path $repoRoot 'apicounters.zip'
$projectFile = Join-Path $repoRoot 'src\ApiCounters\ApiCounters.csproj'
$bicepFile = Join-Path $PSScriptRoot 'main.bicep'

Write-Host '=== Deploying ApiCounters ==='
Write-Host "Resource Group : $ResourceGroup"
Write-Host "Base Name      : $BaseName"
Write-Host "Location       : $Location"

Write-Host ''
Write-Host "[1/4] Ensuring resource group '$ResourceGroup' exists..."
& az group create --name $ResourceGroup --location $Location --output none
if ($LASTEXITCODE -ne 0) {
    throw "Azure CLI failed to create or update resource group '$ResourceGroup'."
}

Write-Host ''
Write-Host '[2/4] Deploying infrastructure...'
$deploymentJson = & az deployment group create `
    --resource-group $ResourceGroup `
    --template-file $bicepFile `
    --parameters "baseName=$BaseName" "location=$Location" `
    --output json | Out-String
if ($LASTEXITCODE -ne 0) {
    throw 'Azure CLI infrastructure deployment failed.'
}

$deployment = $deploymentJson | ConvertFrom-Json
$functionAppName = $deployment.properties.outputs.functionAppName.value
Write-Host "Function App   : $functionAppName"

Write-Host ''
Write-Host '[3/4] Building and publishing the function app...'
Remove-Item -Path $publishDir -Recurse -Force -ErrorAction SilentlyContinue
& dotnet publish $projectFile --configuration Release --output $publishDir
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

Remove-Item -Path $zipFile -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipFile -CompressionLevel Optimal

Write-Host ''
Write-Host '[4/4] Deploying application package...'
& az functionapp deployment source config-zip `
    --resource-group $ResourceGroup `
    --name $functionAppName `
    --src $zipFile
if ($LASTEXITCODE -ne 0) {
    throw 'Azure CLI application package deployment failed.'
}

$functionAppUrl = $deployment.properties.outputs.functionAppUrl.value
$keyId = [guid]::NewGuid()

Write-Host ''
Write-Host '=== Deployment complete ==='
Write-Host "Function App URL: $functionAppUrl"
Write-Host ''
Write-Host 'Example usage:'
Write-Host "  POST: curl.exe -X POST `"$functionAppUrl/api/counter/myCounter`" ``"
Write-Host "             -H `"key-id: d14d435f-f690-7aff-e29f-2afbea68dd4e`" ``"
Write-Host '             -H "counter-dimensions: region:us-east-1,env:prod"'
Write-Host '             -H "counter-value-append: 5"'
Write-Host '             -H "tracked-max: 1.01"'
Write-Host ''
Write-Host "  GET:  curl.exe `"$functionAppUrl/api/counter/myCounter`""