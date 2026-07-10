FROM golang:1.26-alpine AS terraform-config-inspect
ARG TERRAFORM_CONFIG_INSPECT_VERSION=latest
RUN GOBIN=/out go install github.com/hashicorp/terraform-config-inspect@${TERRAFORM_CONFIG_INSPECT_VERSION}

FROM node:24-alpine AS frontend
WORKDIR /app/TerraformRegistry/web-src
COPY TerraformRegistry/web-src/package.json TerraformRegistry/web-src/package-lock.json ./
RUN npm ci
COPY TerraformRegistry/web-src/ ./
ARG FRONTEND_BUILD_MARKER=local
RUN printf '%s\n' "$FRONTEND_BUILD_MARKER" > public/.build-marker && npm run generate

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /app


# Copy solution and project files
COPY terraform-registry.sln ./
COPY TerraformRegistry/TerraformRegistry.csproj TerraformRegistry/
COPY TerraformRegistry.API/TerraformRegistry.API.csproj TerraformRegistry.API/
COPY TerraformRegistry.AzureBlob/TerraformRegistry.AzureBlob.csproj TerraformRegistry.AzureBlob/
COPY TerraformRegistry.Models/TerraformRegistry.Models.csproj TerraformRegistry.Models/
COPY TerraformRegistry.Migrations/TerraformRegistry.Migrations.csproj TerraformRegistry.Migrations/
COPY TerraformRegistry.PostgreSQL/TerraformRegistry.PostgreSQL.csproj TerraformRegistry.PostgreSQL/
COPY TerraformRegistry.S3/TerraformRegistry.S3.csproj TerraformRegistry.S3/
COPY TerraformRegistry.Tests/TerraformRegistry.Tests.csproj TerraformRegistry.Tests/

# Restore using the solution file
RUN dotnet restore terraform-registry.sln

# Copy the rest of the source code
COPY . .

WORKDIR /app/TerraformRegistry
RUN dotnet build TerraformRegistry.csproj -c Release -o /app/build

FROM build AS publish
RUN dotnet publish TerraformRegistry.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
ENV TF_REG_Sqlite__ConnectionString="Data Source=/data/terraform.db"
RUN apk upgrade --no-cache
COPY --from=publish /app/publish .
COPY --from=terraform-config-inspect /out/terraform-config-inspect /usr/local/bin/terraform-config-inspect
RUN mkdir -p /app/modules /app/providers /data && chown app:app /app/modules /app/providers /data
COPY --from=frontend /app/TerraformRegistry/web-src/.output/public /app/web
USER app
ENTRYPOINT ["dotnet", "TerraformRegistry.dll"]
