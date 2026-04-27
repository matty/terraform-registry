# Private Terraform Registry

A lightweight, feature-rich private Terraform module registry implementation.

[![.NET](https://img.shields.io/badge/.NET-10-purple?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/Docker-Ready-blue?style=flat-square&logo=docker)](https://docker.com/)
[![Azure](https://img.shields.io/badge/Azure-Compatible-0078d4?style=flat-square&logo=microsoftazure)](https://azure.microsoft.com/)

## Features

- Terraform module registry protocol support for private module discovery and downloads
- Built-in web UI and OpenAPI (Swagger) documentation
- OIDC Authentication for web portal (GitHub, Azure AD)
- Terraform CLI authentication via `terraform login` and per-user API keys
- Manual portal upload for users with `modules.upload`
- GitHub-linked module publishing, tag backfill, and webhook sync for users with `vcs.manage`
- Local filesystem and Azure Blob Storage support
- PostgreSQL database
- Docker-ready deployment

## Current Scope

This project is focused on private Terraform **modules**. It does not currently implement the Terraform provider registry protocol (`providers.v1`), provider package distribution, provider checksums, or signing-key endpoints. Provider registry support is a separate roadmap item.

Module publishing is supported through:

- authenticated HTTP upload (`POST /v1/modules/{namespace}/{name}/{provider}/{version}`)
- manual upload in the web UI for users with `modules.upload`
- GitHub repository linking and tag backfill for users with `vcs.manage`
- GitHub webhook auto-publish for linked repositories

Workflow details are documented in [docs/module-publishing-workflows.md](/home/rocky/terraform-registry/.worktrees/module-publishing-workflows/docs/module-publishing-workflows.md).

## Quick Start

### Using Docker (Recommended)

```bash
# Run with local storage
docker run -p 5131:80 \
  -v ./modules:/app/modules \
  -e TF_REG_PORT=80 \
  -e TF_REG_BASEURL=http://localhost:5131 \
  -e TF_REG_AUTHORIZATIONTOKEN=your-secure-token \
  -e TF_REG_OIDC__JWTSECRETKEY=replace-with-a-32-character-minimum-secret \
  terraform-registry
```

### Using .NET CLI

```bash
git clone <repository-url>
cd terraform-registry/TerraformRegistry
dotnet run
```

Visit `http://localhost:5131` to access the web interface!

## API Endpoints

### Service Discovery

- `GET /.well-known/terraform.json` - Terraform service discovery endpoint

### Module Operations

- `GET /v1/modules` - List or search modules with filtering
- `GET /v1/modules/{namespace}/{name}/{provider}/{version}` - Get specific module details
- `GET /v1/modules/{namespace}/{name}/{provider}/versions` - Get all module versions
- `GET /v1/modules/{namespace}/{name}/{provider}/{version}/download` - Download specific version
- `GET /v1/modules/{namespace}/{name}/{provider}/download` - Download latest version
- `POST /v1/modules/{namespace}/{name}/{provider}/{version}` - Upload new module by API/CLI or portal (auth required)
- `GET /api/vcs/sources/module/{namespace}/{name}/{provider}` - Get linked VCS source for a module (auth required)
- `POST /api/vcs/sources/{id}/sync` - Manually sync a linked GitHub source (auth required)

### Documentation

- `GET /swagger` - Interactive API documentation (when enabled)

_Endpoints requiring authentication are marked accordingly._

## Configuration

### Environment Variables

Configure the application using environment variables (prefix with `TF_REG_`):

| Variable                                                 | Description                                         | Default                                                          | Required            | Example                                                               |
| -------------------------------------------------------- | --------------------------------------------------- | ---------------------------------------------------------------- | ------------------- | --------------------------------------------------------------------- |
| **Core Settings**                                        |                                                     |                                                                  |                     |
| `TF_REG_PORT`                                            | Application port                                    | `5131`                                                           | No                  | `80`                                                                  |
| `TF_REG_BASEURL`                                         | Registry base URL                                   | `http://localhost:5131`                                          | Yes                 | `https://registry.company.com`                                        |
| `TF_REG_AUTHORIZATIONTOKEN`                              | API authentication token                            | -                                                                | Yes                 | `your-secure-token-here`                                              |
| **Database Settings**                                    |                                                     |                                                                  |                     |                                                                       |
| `TF_REG_DATABASEPROVIDER`                                | Database type (`sqlite`/`postgres`)                 | `sqlite`                                                         | No                  | `postgres`                                                            |
| `TF_REG_SQLITE__CONNECTIONSTRING`                        | SQLite connection string                            | `Data Source=terraform.db`                                       | If using SQLite     | `Data Source=/data/terraform.db`                                      |
| `TF_REG_POSTGRESQL__CONNECTIONSTRING`                    | PostgreSQL connection                               | -                                                                | If using PostgreSQL | `Host=localhost;Database=tfregistry;...`                              |
| `TF_REG_DATABASERETRY__MAXRETRYATTEMPTS`                 | Max retry attempts on connection failure            | `5`                                                              | No                  | `10`                                                                  |
| `TF_REG_DATABASERETRY__INITIALDELAYSECONDS`              | Initial delay before first retry (exponential backoff) | `2`                                                           | No                  | `5`                                                                   |
| `TF_REG_DATABASERETRY__MAXDELAYSECONDS`                  | Maximum delay between retries                       | `30`                                                             | No                  | `60`                                                                  |
| **Storage Settings**                                     |                                                     |                                                                  |                     |
| `TF_REG_STORAGEPROVIDER`                                 | Storage type (`local`/`azure`)                      | `local`                                                          | No                  | `azure`                                                               |
| `TF_REG_MODULESTORAGEPATH`                               | Local storage path                                  | `modules`                                                        | If using local      | `/data/modules`                                                       |
| **Azure Storage Settings**                               |                                                     |                                                                  |                     |
| `TF_REG_AZURESTORAGE__CONNECTIONSTRING`                  | Azure connection string                             | -                                                                | If using Azure      | `DefaultEndpointsProtocol=https;...`                                  |
| `TF_REG_AZURESTORAGE__ACCOUNTNAME`                       | Storage account name                                | -                                                                | If using Azure      | `mystorageaccount`                                                    |
| `TF_REG_AZURESTORAGE__CONTAINERNAME`                     | Blob container name                                 | `modules`                                                        | If using Azure      | `terraform-modules`                                                   |
| `TF_REG_AZURESTORAGE__SASTOKENEXPIRYMINUTES`             | SAS token expiry                                    | `5`                                                              | No                  | `10`                                                                  |
| **OIDC Authentication Settings**                         |                                                     |                                                                  |                     |
| `TF_REG_OIDC__JWTSECRETKEY`                              | JWT signing key for portal sessions (min 32 chars)  | -                                                                | Yes                 | `<unique-generated-secret-32-chars-min>`                              |
| `TF_REG_OIDC__JWTEXPIRYHOURS`                            | JWT token expiration time (hours)                   | `24`                                                             | No                  | `48`                                                                  |
| `TF_REG_OIDC__PROVIDERS__GITHUB__CLIENTID`               | GitHub OAuth App Client ID                          | -                                                                | If using GitHub     | `Iv1.xxxxxxxxxxxx`                                                    |
| `TF_REG_OIDC__PROVIDERS__GITHUB__CLIENTSECRET`           | GitHub OAuth App Client Secret                      | -                                                                | If using GitHub     | `xxxxxxxxxxxx`                                                        |
| `TF_REG_OIDC__PROVIDERS__GITHUB__AUTHORIZATIONENDPOINT`  | GitHub OAuth authorization URL                      | `https://github.com/login/oauth/authorize`                       | No                  |                                                                       |
| `TF_REG_OIDC__PROVIDERS__GITHUB__TOKENENDPOINT`          | GitHub OAuth token endpoint                         | `https://github.com/login/oauth/access_token`                    | No                  |                                                                       |
| `TF_REG_OIDC__PROVIDERS__GITHUB__USERINFOENDPOINT`       | GitHub user info endpoint                           | `https://api.github.com/user`                                    | No                  |                                                                       |
| `TF_REG_OIDC__PROVIDERS__GITHUB__ENABLED`                | Enable GitHub OIDC                                  | `false`                                                          | No                  | `true`                                                                |
| `TF_REG_OIDC__PROVIDERS__AZUREAD__CLIENTID`              | Azure AD App Client ID                              | -                                                                | If using Azure AD   | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`                                |
| `TF_REG_OIDC__PROVIDERS__AZUREAD__CLIENTSECRET`          | Azure AD App Client Secret                          | -                                                                | If using Azure AD   | `xxxxxxxxxxxx`                                                        |
| `TF_REG_OIDC__PROVIDERS__AZUREAD__ENABLED`               | Enable Azure AD OIDC                                | `false`                                                          | No                  | `true`                                                                |
| `TF_REG_OIDC__PROVIDERS__AZUREAD__AUTHORIZATIONENDPOINT` | Azure AD auth URL (use tenant ID if single-tenant)  | `https://login.microsoftonline.com/common/oauth2/v2.0/authorize` | If overriding       | `https://login.microsoftonline.com/<tenant-id>/oauth2/v2.0/authorize` |
| `TF_REG_OIDC__PROVIDERS__AZUREAD__TOKENENDPOINT`         | Azure AD token URL (use tenant ID if single-tenant) | `https://login.microsoftonline.com/common/oauth2/v2.0/token`     | If overriding       | `https://login.microsoftonline.com/<tenant-id>/oauth2/v2.0/token`     |
| `TF_REG_OIDC__PROVIDERS__AZUREAD__USERINFOENDPOINT`      | Azure AD user info endpoint                         | `https://graph.microsoft.com/v1.0/me`                            | No                  |                                                                       |
| **Development Settings**                                 |                                                     |                                                                  |                     |
| `TF_REG_ADMINEMAILS`                                     | Comma-separated emails to bootstrap as admin        | -                                                                | Recommended         | `admin@company.com,ops@company.com`                                   |
| `TF_REG_ENABLESWAGGER`                                   | Enable Swagger UI                                   | `true` (dev)                                                     | No                  | `false`                                                               |
| **Development Settings**                                 |                                                     |                                                                  |                     |                                                                       |
| `TF_REG_DEVAUTHBYPASS`                                   | Enable dev auth bypass (Development env only)       | `false`                                                          | No                  | `true`                                                                |
| `TF_REG_DEVAUTHBYPASS__USERID`                           | Dev user ID when bypassing auth                     | `dev-user-001`                                                   | No                  |                                                                       |
| `TF_REG_DEVAUTHBYPASS__EMAIL`                            | Dev user email when bypassing auth                  | `dev@localhost`                                                  | No                  |                                                                       |
| `TF_REG_DEVAUTHBYPASS__NAME`                             | Dev user display name when bypassing auth           | `Dev User`                                                       | No                  |                                                                       |

## Security Notes

- `AuthorizationToken` / `TF_REG_AUTHORIZATIONTOKEN` must be set to a unique secret outside `Development` and `Test`.
- `Oidc:JwtSecretKey` / `TF_REG_OIDC__JWTSECRETKEY` must be set to a secret that is at least 32 characters long. Outside `Development`, the placeholder value is rejected.
- OIDC login requires a non-empty provider email and rejects same-email logins when they resolve to a different provider or provider ID.
- Outbound admin webhooks only support `http` and `https` targets. Private and local network destinations are blocked unless `WebhookSecurity:AllowPrivateNetworks` / `TF_REG_WEBHOOKSECURITY__ALLOWPRIVATENETWORKS` is explicitly enabled.

### Architecture Options

#### Local Development

```bash
# SQLite database (default) + local file storage
TF_REG_DATABASEPROVIDER=sqlite
TF_REG_SQLITE__CONNECTIONSTRING="Data Source=terraform.db"
TF_REG_STORAGEPROVIDER=local
TF_REG_MODULESTORAGEPATH=./modules
```

#### Production (PostgreSQL + Local)

```bash
# PostgreSQL database + local file storage
TF_REG_DATABASEPROVIDER=postgres
TF_REG_POSTGRESQL__CONNECTIONSTRING=Host=db;Database=registry;...
TF_REG_STORAGEPROVIDER=local
TF_REG_MODULESTORAGEPATH=/data/modules
```

#### Cloud (PostgreSQL + Azure)

```bash
# PostgreSQL database + Azure Blob Storage
TF_REG_DATABASEPROVIDER=postgres
TF_REG_POSTGRESQL__CONNECTIONSTRING=Host=db.postgres.database.azure.com;...
TF_REG_STORAGEPROVIDER=azure
TF_REG_AZURESTORAGE__CONNECTIONSTRING=DefaultEndpointsProtocol=https;AccountName=mystorageaccount;AccountKey=...;EndpointSuffix=core.windows.net
TF_REG_AZURESTORAGE__ACCOUNTNAME=mystorageaccount
TF_REG_AZURESTORAGE__CONTAINERNAME=modules
```

## Docker Deployment

### Docker Compose Example

```yaml
version: "3.8"
services:
  terraform-registry:
    image: terraform-registry
    ports:
      - "5131:80"
    environment:
      - TF_REG_PORT=80
      - TF_REG_BASEURL=https://registry.company.com
      - TF_REG_AUTHORIZATIONTOKEN=super-secure-token
      - TF_REG_OIDC__JWTSECRETKEY=replace-with-a-32-character-minimum-secret
      - TF_REG_DATABASEPROVIDER=postgres
      - TF_REG_POSTGRESQL__CONNECTIONSTRING=Host=postgres;Database=registry;Username=user;Password=pass
      - TF_REG_STORAGEPROVIDER=azure
      - TF_REG_AZURESTORAGE__ACCOUNTNAME=mystorageaccount
    depends_on:
      - postgres

  postgres:
    image: postgres:15
    environment:
      - POSTGRES_DB=registry
      - POSTGRES_USER=user
      - POSTGRES_PASSWORD=pass
    volumes:
      - postgres_data:/var/lib/postgresql/data

volumes:
  postgres_data:
```

### Azure Container Instances

```bash
az container create \
  --resource-group myResourceGroup \
  --name terraform-registry \
  --image terraform-registry \
  --dns-name-label terraform-registry \
  --ports 80 \
  --environment-variables \
    TF_REG_PORT=80 \
    TF_REG_BASEURL=https://terraform-registry.eastus.azurecontainer.io \
    TF_REG_AUTHORIZATIONTOKEN=super-secure-token \
    TF_REG_OIDC__JWTSECRETKEY=replace-with-a-32-character-minimum-secret \
    TF_REG_STORAGEPROVIDER=azure \
    TF_REG_AZURESTORAGE__CONNECTIONSTRING="DefaultEndpointsProtocol=https;AccountName=mystorageaccount;AccountKey=...;EndpointSuffix=core.windows.net" \
    TF_REG_AZURESTORAGE__ACCOUNTNAME=mystorageaccount \
  --assign-identity \
  --scope /subscriptions/.../resourceGroups/.../providers/Microsoft.Storage/storageAccounts/mystorageaccount
```

## Usage with Terraform

### Configure Terraform CLI

Preferred interactive flow:

```bash
terraform login registry.company.com
```

This registry advertises Terraform's `login.v1` protocol and issues a new per-user API key on each successful login. CLI-issued keys expire after 90 days and can be revoked from the API Keys page in the web UI.

Manual credentials fallback:

```hcl
host "registry.company.com" {
  services = {
    "modules.v1" = "/v1/modules/"
  }

  credentials {
    token = "your-user-api-token-here"
  }
}
```

### Use Modules in Terraform

```hcl
terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

# Use a module from your private registry
module "vpc" {
  source  = "registry.company.com/myorg/vpc/aws"
  version = "1.2.3"

  cidr_block = "10.0.0.0/16"
  name       = "my-vpc"
}
```

### Upload Modules

```bash
# Create a module archive
tar -czf vpc-aws-1.2.3.tar.gz -C ./vpc-module .

# Upload using curl
curl -X POST \
  -H "Authorization: Bearer your-auth-token" \
  -F "moduleFile=@vpc-aws-1.2.3.tar.gz" \
  -F "description=VPC module for AWS" \
  "https://registry.company.com/v1/modules/myorg/vpc/aws/1.2.3"
```

### Health Checks

```bash
# Check service discovery
curl https://registry.company.com/.well-known/terraform.json

# List available modules
curl https://registry.company.com/v1/modules

# Check specific module
curl https://registry.company.com/v1/modules/myorg/vpc/aws/1.2.3
```

## Development

### Prerequisites

- .NET 10 SDK
- PostgreSQL (optional, for database testing)
- Azure Storage Emulator (optional, for Azure testing)

### Run Locally

```bash
cd TerraformRegistry
dotnet restore
dotnet run
```

### Run Tests

```bash
dotnet test
```

### Build Docker Image

```bash
docker build -t terraform-registry .
```

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Support

- Check the [API documentation](http://localhost:5131/swagger) when running locally
- Report issues on GitHub

---
