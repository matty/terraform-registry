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

The application can be configured using the following environment variables:

| Environment Variable                         | Description                           | Default Value           | Required                               |
| -------------------------------------------- | ------------------------------------- | ----------------------- | -------------------------------------- |
| `TF_REG_BaseUrl`                             | Base URL for the Terraform Registry   | `http://localhost:5131` | Yes                                    |
| `TF_REG_StorageProvider`                     | Storage provider (`local` or `azure`) | `local`                 | Yes                                    |
| `TF_REG_ModuleStoragePath`                   | Local path to store modules           | `modules`               | Only when `StorageProvider` is `local` |
| `TF_REG_AzureStorage__ConnectionString`      | Azure Storage connection string       | Empty                   | Only when `StorageProvider` is `azure` |
| `TF_REG_AzureStorage__ContainerName`         | Azure Storage container name          | `modules`               | Only when `StorageProvider` is `azure` |
| `TF_REG_AzureStorage__SasTokenExpiryMinutes` | Expiry time in minutes for SAS tokens | `5`                     | Only when `StorageProvider` is `azure` |

### Configuration Precedence

Configuration values are loaded in the following order, with later values overriding earlier ones:

1. Default values
2. Configuration in `appsettings.json`
3. Configuration in `appsettings.{Environment}.json`
4. Environment variables

### Docker Example

```bash
docker run -p 5131:80 \
  -e TF_REG_BaseUrl=http://registry.example.com \
  -e TF_REG_StorageProvider=azure \
  -e TF_REG_AzureStorage__ConnectionString="DefaultEndpointsProtocol=https;AccountName=youraccountname;AccountKey=youraccountkey;EndpointSuffix=core.windows.net" \
  terraform-registry
```

## Usage with Terraform

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

## Development

### Building and Running

```bash
# Build the project
dotnet build

# Run the project
dotnet run

# Build for Release
dotnet publish -c Release
```

### Testing API Endpoints

When running in development mode, navigate to `/swagger` for interactive API documentation.

## Project Structure

- `Handlers/` - Minimal API endpoint handlers
- `Models/` - Data models for the Terraform Registry
- `Services/` - Business logic services
