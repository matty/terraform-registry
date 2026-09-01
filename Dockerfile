FROM golang:1.26-alpine@sha256:28d89ee9cc0ff9fec75c82ca201e6bf7fdf9a679d4b7b24dfa04f2bb766bb468 AS terraform-config-inspect
ARG TERRAFORM_CONFIG_INSPECT_VERSION=2fb54c236733ee65ee877105d595c124c993c64d
ARG TERRAFORM_CONFIG_INSPECT_ARCHIVE_SHA256=83aedf832593023babc90bd49dce5adb58e8f0774bacea4992a3d350f33af915
ARG TERRAFORM_CONFIG_INSPECT_X_TEXT_VERSION=0.39.0
RUN mkdir /src \
    && wget -qO /tmp/terraform-config-inspect.tar.gz \
        "https://github.com/hashicorp/terraform-config-inspect/archive/${TERRAFORM_CONFIG_INSPECT_VERSION}.tar.gz" \
    && echo "${TERRAFORM_CONFIG_INSPECT_ARCHIVE_SHA256}  /tmp/terraform-config-inspect.tar.gz" | sha256sum -c - \
    && tar -xzf /tmp/terraform-config-inspect.tar.gz --strip-components=1 -C /src \
    && cd /src \
    && go get "golang.org/x/text@v${TERRAFORM_CONFIG_INSPECT_X_TEXT_VERSION}" \
    && GOBIN=/out go install .

FROM node:24-alpine@sha256:a0b9bf06e4e6193cf7a0f58816cc935ff8c2a908f81e6f1a95432d679c54fbfd AS frontend
WORKDIR /app/TerraformRegistry/web-src
COPY TerraformRegistry/web-src/package.json TerraformRegistry/web-src/package-lock.json ./
RUN npm ci
COPY TerraformRegistry/web-src/ ./
ARG FRONTEND_BUILD_MARKER=local
RUN printf '%s\n' "$FRONTEND_BUILD_MARKER" > public/.build-marker && npm run generate

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:620e765fe18186c08399f7aa978f79f04b6bbf0ee1b3b8a91e2d5c9619e59da1 AS build
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

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:c4b29bf368004ad9076c1ab9bc91fb373561e3905b4345637e14e8b8c57e3be8 AS final
WORKDIR /app
ENV TF_REG_Sqlite__ConnectionString="Data Source=/data/terraform.db"
RUN apk add --no-cache libcrypto3=3.5.8-r0 libssl3=3.5.8-r0
COPY --from=publish /app/publish .
COPY --from=terraform-config-inspect /out/terraform-config-inspect /usr/local/bin/terraform-config-inspect
RUN mkdir -p /app/modules /app/providers /data && chown app:app /app/modules /app/providers /data
COPY --from=frontend /app/TerraformRegistry/web-src/.output/public /app/web
USER app
ENTRYPOINT ["dotnet", "TerraformRegistry.dll"]
