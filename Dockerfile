FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /app

# Define build arguments with defaults
ARG USE_POSTGRESQL=true
ARG USE_AZURE_BLOB=true

# Copy solution and project files
COPY terraform-registry.sln ./
COPY TerraformRegistry/TerraformRegistry.csproj TerraformRegistry/
COPY TerraformRegistry.API/TerraformRegistry.API.csproj TerraformRegistry.API/
COPY TerraformRegistry.AzureBlob/TerraformRegistry.AzureBlob.csproj TerraformRegistry.AzureBlob/
COPY TerraformRegistry.Models/TerraformRegistry.Models.csproj TerraformRegistry.Models/
COPY TerraformRegistry.PostgreSQL/TerraformRegistry.PostgreSQL.csproj TerraformRegistry.PostgreSQL/

# Restore using the solution file
RUN dotnet restore terraform-registry.sln /p:UsePostgreSQL=${USE_POSTGRESQL} /p:UseAzureBlob=${USE_AZURE_BLOB}

# Copy the rest of the source code
COPY . .

WORKDIR /app/TerraformRegistry
RUN dotnet build TerraformRegistry.csproj -c Release -o /app/build /p:UsePostgreSQL=${USE_POSTGRESQL} /p:UseAzureBlob=${USE_AZURE_BLOB}

FROM build AS publish
RUN dotnet publish TerraformRegistry.csproj -c Release -o /app/publish /p:UseAppHost=false /p:UsePostgreSQL=${USE_POSTGRESQL} /p:UseAzureBlob=${USE_AZURE_BLOB}

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Create modules directory
RUN mkdir -p /app/modules
# Create web directory and copy static files
COPY TerraformRegistry/web /app/web
EXPOSE 80
EXPOSE 443
ENTRYPOINT ["dotnet", "TerraformRegistry.dll"]