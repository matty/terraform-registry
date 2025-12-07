FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /app


# Copy solution and project files
COPY terraform-registry.sln ./
COPY TerraformRegistry/TerraformRegistry.csproj TerraformRegistry/
COPY TerraformRegistry.API/TerraformRegistry.API.csproj TerraformRegistry.API/
COPY TerraformRegistry.AzureBlob/TerraformRegistry.AzureBlob.csproj TerraformRegistry.AzureBlob/
COPY TerraformRegistry.Models/TerraformRegistry.Models.csproj TerraformRegistry.Models/
COPY TerraformRegistry.PostgreSQL/TerraformRegistry.PostgreSQL.csproj TerraformRegistry.PostgreSQL/
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
COPY --from=publish /app/publish .
# Create modules directory
RUN mkdir -p /app/modules
# Create web directory and copy static files
COPY TerraformRegistry/web /app/web
ENTRYPOINT ["dotnet", "TerraformRegistry.dll"]