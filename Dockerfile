FROM golang:1.26-alpine@sha256:ce864e7223ac17b1775e6fd0b4c0db580c2eb50e7953a427916379e4b92a1628 AS terraform-config-inspect
ARG TERRAFORM_CONFIG_INSPECT_VERSION=2fb54c236733ee65ee877105d595c124c993c64d
RUN GOBIN=/out go install github.com/hashicorp/terraform-config-inspect@${TERRAFORM_CONFIG_INSPECT_VERSION}

FROM node:24-alpine@sha256:a0b9bf06e4e6193cf7a0f58816cc935ff8c2a908f81e6f1a95432d679c54fbfd AS frontend
WORKDIR /app/TerraformRegistry/web-src
COPY TerraformRegistry/web-src/package.json TerraformRegistry/web-src/package-lock.json ./
RUN npm ci
COPY TerraformRegistry/web-src/ ./
ARG FRONTEND_BUILD_MARKER=local
RUN printf '%s\n' "$FRONTEND_BUILD_MARKER" > public/.build-marker && npm run generate

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:940f919ae84dd92ccd4aab7686fa5b777870b006c9360351039e16bcaad73d89 AS build
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

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:57bd717ac18ff6c8a39cc0ee4a76c1f15adc46df50434c73eff0c3f1df4c88f0 AS final
WORKDIR /app
ENV TF_REG_Sqlite__ConnectionString="Data Source=/data/terraform.db"
COPY --from=publish /app/publish .
COPY --from=terraform-config-inspect /out/terraform-config-inspect /usr/local/bin/terraform-config-inspect
RUN mkdir -p /app/modules /app/providers /data && chown app:app /app/modules /app/providers /data
COPY --from=frontend /app/TerraformRegistry/web-src/.output/public /app/web
USER app
ENTRYPOINT ["dotnet", "TerraformRegistry.dll"]
