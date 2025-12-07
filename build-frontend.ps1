# Build script for the Terraform Registry frontend (Nuxt.js)
# This script builds the frontend and copies the output to the web folder

param(
    [switch]$SkipInstall,
    [switch]$Watch
)

$ErrorActionPreference = "Stop"

$webSrcPath = Join-Path $PSScriptRoot "TerraformRegistry\web-src"
$webOutputPath = Join-Path $PSScriptRoot "TerraformRegistry\web"

Write-Host "=== Terraform Registry Frontend Build ===" -ForegroundColor Cyan
Write-Host ""

# Navigate to web-src directory
Push-Location $webSrcPath

try {
    # Install dependencies (unless skipped)
    if (-not $SkipInstall) {
        Write-Host "Installing npm dependencies..." -ForegroundColor Yellow
        npm install
        if ($LASTEXITCODE -ne 0) {
            Write-Host "npm install failed!" -ForegroundColor Red
            exit 1
        }
        Write-Host "Dependencies installed successfully." -ForegroundColor Green
        Write-Host ""
    }

    if ($Watch) {
        # Development mode with hot reload
        Write-Host "Starting Nuxt development server..." -ForegroundColor Yellow
        Write-Host "Note: This runs the frontend separately. Access at http://localhost:3000" -ForegroundColor Cyan
        npm run dev
    }
    else {
        # Production build
        Write-Host "Building frontend for production..." -ForegroundColor Yellow
        npm run generate
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Build failed!" -ForegroundColor Red
            exit 1
        }
        Write-Host "Build completed successfully." -ForegroundColor Green
        Write-Host ""

        # Define source path (Nuxt generate outputs to .output/public)
        $buildOutputPath = Join-Path $webSrcPath ".output\public"

        if (-not (Test-Path $buildOutputPath)) {
            Write-Host "Build output not found at: $buildOutputPath" -ForegroundColor Red
            exit 1
        }

        # Clear the destination web folder
        Write-Host "Clearing existing web folder..." -ForegroundColor Yellow
        if (Test-Path $webOutputPath) {
            Get-ChildItem -Path $webOutputPath -Recurse | Remove-Item -Recurse -Force
        }
        else {
            New-Item -ItemType Directory -Path $webOutputPath | Out-Null
        }

        # Copy build output to web folder
        Write-Host "Copying build output to web folder..." -ForegroundColor Yellow
        Copy-Item -Path "$buildOutputPath\*" -Destination $webOutputPath -Recurse -Force

        Write-Host ""
        Write-Host "=== Build Complete ===" -ForegroundColor Green
        Write-Host "Frontend files copied to: $webOutputPath" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Files in web folder:" -ForegroundColor Yellow
        Get-ChildItem -Path $webOutputPath | ForEach-Object { Write-Host "  $_" }
    }
}
finally {
    Pop-Location
}
