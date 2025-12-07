param(
    [string]$BaseUrl = "http://localhost:5131",
    [string]$Namespace = "example",
    [string]$Name = "dummy",
    [string]$Provider = "aws",
    [string]$Version = "0.0.1",
    [string]$Description = "Dummy module uploaded by script",
    [string]$ApiKey = "API_KEY_HERE",
    [string]$WorkDir = "$PSScriptRoot/dummy-module",
    [string]$ZipPath = "$PSScriptRoot/dummy-module.zip",
    [switch]$ReplaceIfExists
)

# Create a tiny dummy module on disk
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
@'
# Dummy module
variable "example" {
  type = string
  default = "hello"
}

output "example" {
  value = var.example
}
'@ | Set-Content -Path (Join-Path $WorkDir "main.tf") -Encoding UTF8

# Zip the module
if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }
Compress-Archive -Path (Join-Path $WorkDir '*') -DestinationPath $ZipPath

# Build target URL
$uri = "$BaseUrl/v1/modules/$Namespace/$Name/$Provider/$Version"

# Prepare form data
$form = @{
    moduleFile  = Get-Item $ZipPath
    description = $Description
    replace     = if ($ReplaceIfExists) { "true" } else { "false" }
}

$headers = @{}
if ($ApiKey) {
    $headers["Authorization"] = "Bearer $ApiKey"
}

Write-Host "Uploading $ZipPath to $uri" -ForegroundColor Cyan
$resp = Invoke-RestMethod -Method Post -Uri $uri -Form $form -Headers $headers -ContentType "multipart/form-data"
$resp | ConvertTo-Json -Depth 5
