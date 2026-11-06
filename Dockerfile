FROM golang:1.27-alpine@sha256:fa3d596898d9f126af7c01d14ee7597bab3f39cccf392ef36b69fe77cd681f18 AS terraform-config-inspect
ARG TERRAFORM_CONFIG_INSPECT_VERSION=2fb54c236733ee65ee877105d595c124c993c64d
ARG TERRAFORM_CONFIG_INSPECT_ARCHIVE_SHA256=83aedf832593023babc90bd49dce5adb58e8f0774bacea4992a3d350f33af915
ARG TERRAFORM_CONFIG_INSPECT_X_TEXT_VERSION=0.41.0
RUN mkdir /src \
    && wget -qO /tmp/terraform-config-inspect.tar.gz \
        "https://github.com/hashicorp/terraform-config-inspect/archive/${TERRAFORM_CONFIG_INSPECT_VERSION}.tar.gz" \
    && echo "${TERRAFORM_CONFIG_INSPECT_ARCHIVE_SHA256}  /tmp/terraform-config-inspect.tar.gz" | sha256sum -c - \
    && tar -xzf /tmp/terraform-config-inspect.tar.gz --strip-components=1 -C /src \
    && cd /src \
    && go get "golang.org/x/text@v${TERRAFORM_CONFIG_INSPECT_X_TEXT_VERSION}" \
    && GOBIN=/out go install .

FROM node:24-alpine@sha256:a34ab6d7f6f4813f4d0c5469d74685604b962fbdbf5793a7bc7b4998ecb867c8 AS frontend
WORKDIR /app/TerraformRegistry/web-src
COPY TerraformRegistry/web-src/package.json TerraformRegistry/web-src/package-lock.json ./
RUN npm ci
COPY TerraformRegistry/web-src/ ./
ARG FRONTEND_BUILD_MARKER=local
RUN printf '%s\n' "$FRONTEND_BUILD_MARKER" > public/.build-marker && npm run generate

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:5562fd626eec39f6f53218f9b7d7856ab7195d51d846568304fbe85071a3b6e6 AS build
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

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:486297ae006e95ee2ee3112a2c03e3d26e41fbbc64b7a29e28e7a18cc36b12bb AS final
WORKDIR /app
ENV TF_REG_Sqlite__ConnectionString="Data Source=/data/terraform.db"
RUN apk add --no-cache libcrypto3=3.5.9-r0 libssl3=3.5.9-r0
COPY --from=publish /app/publish .
COPY --from=terraform-config-inspect /out/terraform-config-inspect /usr/local/bin/terraform-config-inspect
RUN mkdir -p /app/modules /app/providers /data && chown app:app /app/modules /app/providers /data
COPY --from=frontend /app/TerraformRegistry/web-src/.output/public /app/web
USER app
ENTRYPOINT ["dotnet", "TerraformRegistry.dll"]
