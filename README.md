# Private Terraform Registry

A lightweight private Terraform Registry implementation currently focusing on support for modules.

## Features

- Full support for the Terraform Registry Protocol v1 for modules
- Support for different storage and database implementations
- OpenAPI documentation
- Configurable via environment variables for easy containerization and deployment

## API Endpoints

### Service Discovery

- `GET /.well-known/terraform.json` - Terraform service discovery endpoint

### Module Operations

- `GET /v1/modules` - List or search modules
- `GET /v1/modules/{namespace}/{name}/{provider}/{version}` - Get specific module
- `GET /v1/modules/{namespace}/{name}/{provider}/versions` - Get all module versions
- `GET /v1/modules/{namespace}/{name}/{provider}/{version}/download` - Download module
- `POST /v1/modules/{namespace}/{name}/{provider}/{version}` - Upload new module

## Configuration

### Environment Variables

The application can be configured using the following environment variables (all must be prefixed with `TF_REG_`):

| Environment Variable                  | Description                                      | Default Value           | Required                                   |
| ------------------------------------- | ------------------------------------------------ | ----------------------- | ------------------------------------------ |
| `TF_REG_PORT`                         | Port the application listens on                  | `5131`                  | No                                         |
| `TF_REG_BASEURL`                      | Base URL for the Terraform Registry              | `http://localhost:5131` | Yes                                        |
| `TF_REG_DATABASEPROVIDER`             | Database provider (`postgres` or `inmemory`)     | `inmemory`              | No                                         |
| `TF_REG_POSTGRESQL__CONNECTIONSTRING` | PostgreSQL connection string                     | Empty                   | Only when `DatabaseProvider` is `postgres` |
| `TF_REG_STORAGEPROVIDER`              | Storage provider (`local` or `azure`)            | `local`                 | Yes                                        |
| `TF_REG_MODULESTORAGEPATH`            | Local path to store modules                      | `modules`               | Only when `StorageProvider` is `local`     |
| `TF_REG_AZUREBLOB__CONNECTIONSTRING`  | Azure Blob Storage connection string             | Empty                   | Only when `StorageProvider` is `azure`     |
| `TF_REG_AZUREBLOB__CONTAINERNAME`     | Azure Blob Storage container name                | `modules`               | Only when `StorageProvider` is `azure`     |
| `TF_REG_AUTHORIZATIONTOKEN`           | Token required for API authentication            | Empty                   | No                                         |
| `TF_REG_ENABLESWAGGER`                | Enable or disable Swagger UI (`true` or `false`) | `true` in dev           | No                                         |

> **Note:** For nested configuration (like `PostgreSQL:ConnectionString`), use double underscores:  
> `TF_REG_POSTGRESQL__CONNECTIONSTRING`

### Configuration Precedence

Configuration values are loaded in the following order, with later values overriding earlier ones:

1. Default values
2. Configuration in `appsettings.json`
3. Configuration in `appsettings.{Environment}.json`
4. Environment variables (with `TF_REG_` prefix)

### Docker Example

```bash
docker run -p 5131:80 \
  -e TF_REG_PORT=80 \
  -e TF_REG_BASEURL=http://registry.example.com \
  -e TF_REG_STORAGEPROVIDER=azure \
  -e TF_REG_AZUREBLOB__CONNECTIONSTRING="DefaultEndpointsProtocol=https;AccountName=youraccountname;AccountKey=youraccountkey;EndpointSuffix=core.windows.net" \
  terraform-registry
```

You can override the port mapping at runtime with Docker's `-p` flag, regardless of the `EXPOSE` instruction in the Dockerfile.

### Usage with Terraform

Configure Terraform to use this private registry:

```terraform
terraform {
  required_providers {
    aws = {
      source = "localhost:5131/hashicorp/aws"
    }
  }
}
```
