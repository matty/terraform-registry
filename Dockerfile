FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Define build arguments with defaults
ARG USE_POSTGRESQL=false
ARG USE_AZURE_BLOB=false

COPY ["TerraformRegistry/TerraformRegistry.csproj", "TerraformRegistry/"]
RUN dotnet restore "TerraformRegistry/TerraformRegistry.csproj" /p:UsePostgreSQL=${USE_POSTGRESQL} /p:UseAzureBlob=${USE_AZURE_BLOB}
COPY . .
WORKDIR "/src/TerraformRegistry"
RUN dotnet build "TerraformRegistry.csproj" -c Release -o /app/build /p:UsePostgreSQL=${USE_POSTGRESQL} /p:UseAzureBlob=${USE_AZURE_BLOB}

FROM build AS publish
RUN dotnet publish "TerraformRegistry.csproj" -c Release -o /app/publish /p:UseAppHost=false /p:UsePostgreSQL=${USE_POSTGRESQL} /p:UseAzureBlob=${USE_AZURE_BLOB}

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Create modules directory
RUN mkdir -p /app/modules
# Create web directory and copy static files
COPY TerraformRegistry/web /app/web
EXPOSE 80
EXPOSE 443
ENTRYPOINT ["dotnet", "TerraformRegistry.dll"]