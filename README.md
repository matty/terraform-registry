# Private Terraform Registry

A lightweight, feature-rich private Terraform Registry implementation with full support for modules!

[![.NET](https://img.shields.io/badge/.NET-10-purple?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/Docker-Ready-blue?style=flat-square&logo=docker)](https://docker.com/)
[![Azure](https://img.shields.io/badge/Azure-Compatible-0078d4?style=flat-square&logo=microsoftazure)](https://azure.microsoft.com/)

## Features

- Full Terraform Registry Protocol v1 for modules
- Built-in web UI and OpenAPI (Swagger) documentation
- OIDC Authentication for web portal (GitHub, Azure AD)
- API Token authentication for Terraform CLI (currently set via env vars)
- Local filesystem and Azure Blob Storage support
- PostgreSQL database
- Docker-ready deployment

## Quick Start

### Using Docker (Recommended)

```bash
# Run with local storage
docker run -p 5131:80 \
  -v ./modules:/app/modules \
  -e TF_REG_PORT=80 \
  -e TF_REG_BASEURL=http://localhost:5131 \
  -e TF_REG_AUTHORIZATIONTOKEN=your-secure-token \
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
- `POST /v1/modules/{namespace}/{name}/{provider}/{version}` - Upload new module (auth required)

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
| `TF_REG_AUTHORIZATIONTOKEN`                              | API authentication token                            | -                                                                | Recommended         | `your-secure-token-here`                                              |
| **Database Settings**                                    |                                                     |                                                                  |                     |
| `TF_REG_DATABASEPROVIDER`                                | Database type (`sqlite`/`postgres`)                 | `sqlite`                                                         | No                  | `postgres`                                                            |
| `TF_REG_SQLITE__CONNECTIONSTRING`                        | SQLite connection string                            | `Data Source=terraform.db`                                       | If using SQLite     | `Data Source=/data/terraform.db`                                      |
| `TF_REG_POSTGRESQL__CONNECTIONSTRING`                    | PostgreSQL connection                               | -                                                                | If using PostgreSQL | `Host=localhost;Database=tfregistry;...`                              |
| **Storage Settings**                                     |                                                     |                                                                  |                     |
| `TF_REG_STORAGEPROVIDER`                                 | Storage type (`local`/`azure`)                      | `local`                                                          | No                  | `azure`                                                               |
| `TF_REG_MODULESTORAGEPATH`                               | Local storage path                                  | `modules`                                                        | If using local      | `/data/modules`                                                       |
| **Azure Storage Settings**                               |                                                     |                                                                  |                     |
| `TF_REG_AZURESTORAGE__CONNECTIONSTRING`                  | Azure connection string                             | -                                                                | If using Azure      | `DefaultEndpointsProtocol=https;...`                                  |
| `TF_REG_AZURESTORAGE__ACCOUNTNAME`                       | Storage account name                                | -                                                                | If using Azure      | `mystorageaccount`                                                    |
| `TF_REG_AZURESTORAGE__CONTAINERNAME`                     | Blob container name                                 | `modules`                                                        | If using Azure      | `terraform-modules`                                                   |
| `TF_REG_AZURESTORAGE__SASTOKENEXPIRYMINUTES`             | SAS token expiry                                    | `5`                                                              | No                  | `10`                                                                  |
| **OIDC Authentication Settings**                         |                                                     |                                                                  |                     |
| `TF_REG_OIDC__JWTSECRETKEY`                              | JWT signing key (min 32 chars)                      | -                                                                | Yes (for OIDC)      | `your-256-bit-secret-key-here...`                                     |
| `TF_REG_OIDC__PROVIDERS__GITHUB__CLIENTID`               | GitHub OAuth App Client ID                          | -                                                                | If using GitHub     | `Iv1.xxxxxxxxxxxx`                                                    |
| `TF_REG_OIDC__PROVIDERS__GITHUB__CLIENTSECRET`           | GitHub OAuth App Client Secret                      | -                                                                | If using GitHub     | `xxxxxxxxxxxx`                                                        |
| `TF_REG_OIDC__PROVIDERS__GITHUB__ENABLED`                | Enable GitHub OIDC                                  | `false`                                                          | No                  | `true`                                                                |
| `TF_REG_OIDC__PROVIDERS__AZUREAD__CLIENTID`              | Azure AD App Client ID                              | -                                                                | If using Azure AD   | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`                                |
| `TF_REG_OIDC__PROVIDERS__AZUREAD__CLIENTSECRET`          | Azure AD App Client Secret                          | -                                                                | If using Azure AD   | `xxxxxxxxxxxx`                                                        |
| `TF_REG_OIDC__PROVIDERS__AZUREAD__ENABLED`               | Enable Azure AD OIDC                                | `false`                                                          | No                  | `true`                                                                |
| `TF_REG_OIDC__PROVIDERS__AZUREAD__AUTHORIZATIONENDPOINT` | Azure AD auth URL (use tenant ID if single-tenant)  | `https://login.microsoftonline.com/common/oauth2/v2.0/authorize` | If overriding       | `https://login.microsoftonline.com/<tenant-id>/oauth2/v2.0/authorize` |
| `TF_REG_OIDC__PROVIDERS__AZUREAD__TOKENENDPOINT`         | Azure AD token URL (use tenant ID if single-tenant) | `https://login.microsoftonline.com/common/oauth2/v2.0/token`     | If overriding       | `https://login.microsoftonline.com/<tenant-id>/oauth2/v2.0/token`     |
| **Development Settings**                                 |                                                     |                                                                  |                     |
| `TF_REG_ENABLESWAGGER`                                   | Enable Swagger UI                                   | `true` (dev)                                                     | No                  | `false`                                                               |

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
      - TF_REG_DATABASEPROVIDER=postgres
      - TF_REG_POSTGRESQL__CONNECTIONSTRING=Host=postgres;Database=registry;Username=user;Password=pass
      - TF_REG_STORAGEPROVIDER=azure
      - TF_REG_AZURESTORAGE__ACCOUNTNAME=mystorageaccount
      - TF_REG_AUTHORIZATIONTOKEN=super-secure-token
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
    TF_REG_STORAGEPROVIDER=azure \
    TF_REG_AZURESTORAGE__CONNECTIONSTRING="DefaultEndpointsProtocol=https;AccountName=mystorageaccount;AccountKey=...;EndpointSuffix=core.windows.net" \
    TF_REG_AZURESTORAGE__ACCOUNTNAME=mystorageaccount \
  --assign-identity \
  --scope /subscriptions/.../resourceGroups/.../providers/Microsoft.Storage/storageAccounts/mystorageaccount
```

## Usage with Terraform

### Configure Terraform CLI

Create or update `~/.terraformrc`:

```hcl
host "registry.company.com" {
  services = {
    "modules.v1" = "/v1/modules/"
  }

  credentials {
    token = "your-auth-token-here"
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
