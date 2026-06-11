# Etisalat SaaS Callback Service

.NET 10 Web API service for handling ISV Provisioning Status callbacks with Etisalat XaaS platform.

**Author:** imarouf  
**Version:** 1.0  
**Framework:** .NET 10  

---

## Table of Contents

- [Features](#features)
- [Architecture](#architecture)
- [Quick Start](#quick-start)
- [API Endpoints](#api-endpoints)
- [Configuration](#configuration)
- [Deployment](#deployment)
- [Testing](#testing)
- [Error Codes](#error-codes)
- [Documentation](#documentation)

---

## Features

| Feature | Description |
|---------|-------------|
| **Inbound Callbacks** | Receive provisioning status from Etisalat/ISVs |
| **Outbound Callbacks** | Send provisioning status to Etisalat |
| **MongoDB Storage** | Persist all callback records and subscription states |
| **Basic Authentication** | Secure API with Base64 encoded credentials |
| **IP Whitelisting** | Optional IP-based access control |
| **Retry Policy** | Automatic retry with exponential backoff (3 attempts) |
| **Swagger Documentation** | Interactive API documentation |
| **Docker Support** | Containerized deployment ready |
| **Logging** | Serilog with console and file sinks |

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                 ETISALAT XAAS PLATFORM                      │
│           https://contentapi-s.etisalat.ae                  │
└──────────────────────────┬──────────────────────────────────┘
                           │ HTTPS (REST/JSON)
                           │ Basic Auth + IP Whitelist
┌──────────────────────────┴──────────────────────────────────┐
│              ETISALAT SAAS CALLBACK SERVICE                 │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Controllers: CallbackController, HealthController      │ │
│  ├────────────────────────────────────────────────────────┤ │
│  │ Services: ProvisioningService, MongoDbService,         │ │
│  │           EtisalatCallbackClient                       │ │
│  ├────────────────────────────────────────────────────────┤ │
│  │ Auth: BasicAuthHandler, IpWhitelistMiddleware          │ │
│  └────────────────────────────────────────────────────────┘ │
└──────────────────────────┬──────────────────────────────────┘
                           │
              ┌────────────┴────────────┐
              │   MongoDB (ServiceMeTest)│
              │  - provisioning_records  │
              │  - subscription_states   │
              └─────────────────────────┘
```

### Project Structure

```
EtisalatSaasCallback/
├── Authentication/
│   └── BasicAuthenticationHandler.cs   # Auth + IP middleware
├── Configuration/
│   ├── EtisalatSettings.cs             # Outbound API config
│   └── MongoDbSettings.cs              # Database config
├── Controllers/
│   ├── CallbackController.cs           # Main API endpoints
│   └── HealthController.cs             # Health checks
├── Models/
│   ├── IsvProvisioningStatusRequest.cs # Request DTOs
│   ├── IsvProvisioningStatusResponse.cs# Response DTOs + Error codes
│   └── ProvisioningRecord.cs           # MongoDB entities
├── Services/
│   ├── EtisalatCallbackClient.cs       # HTTP client for Etisalat
│   ├── MongoDbService.cs               # Data access layer
│   └── ProvisioningService.cs          # Business logic
├── docs/
│   ├── index.html                      # Full documentation
│   ├── deployment-guide.html           # Deployment instructions
│   └── api-reference.html              # API reference
├── Program.cs                          # Application entry point
├── appsettings.json                    # Configuration
├── Dockerfile
├── docker-compose.yml
└── README.md
```

---

## Quick Start

### Prerequisites

- .NET 10 SDK
- MongoDB 7.0+ (or Docker)

### Option 1: Docker Compose (Recommended)

```bash
cd d:/RD/Docs/EtisalatSaasCallback

# Start services (API + MongoDB)
docker-compose up -d

# View logs
docker-compose logs -f api
```

### Option 2: Run Locally

```bash
# Ensure MongoDB is running on localhost:27017

cd d:/RD/Docs/EtisalatSaasCallback

# Restore packages
dotnet restore

# Run the application
dotnet run

# Access Swagger UI
# http://localhost:5000/swagger
```

---

## API Endpoints

### 1. Receive Callback (Inbound)

**POST** `/rest/xaas/v1/isvProvisioningStatus`

Receives provisioning status callbacks from Etisalat or other ISVs.

```json
// Request
{
  "isvProvisioningStatusRequest": {
    "referenceNumber": "REF123456",
    "subscriptionId": "SUB789012",
    "action": "ACCEPTED",
    "billingDate": "20260609143000",
    "serviceAttribute": []
  }
}

// Response
{
  "isvProvisioningStatusResponse": {
    "referenceNumber": "REF123456",
    "responseCode": "0",
    "status": 0,
    "description": "Successful",
    "responseAttributes": []
  }
}
```

### 2. Send Callback (Outbound)

**POST** `/rest/xaas/v1/sendCallback`

Sends provisioning status callback to Etisalat XaaS platform.

### 3. Health Check

**GET** `/api/health` - Basic health check  
**GET** `/api/health/detailed` - Detailed health with MongoDB status

### Actions

| Action | Description | Billing Impact |
|--------|-------------|----------------|
| `ACCEPTED` | Customer onboarded successfully | Billing starts |
| `REJECTED` | Customer not onboarded | No billing |
| `EXPIRED` | SLA breached (30 days) | Order expired |

---

## Configuration

### appsettings.json

```json
{
  "MongoDbSettings": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "ServiceMeTest",
    "ProvisioningCollection": "provisioning_records",
    "SubscriptionCollection": "subscription_states"
  },

  "Etisalat": {
    "BaseUrl": "https://contentapi-s.etisalat.ae/rest/xaas/v1",
    "Username": "YOUR_ETISALAT_USERNAME",
    "Password": "YOUR_ETISALAT_PASSWORD",
    "TimeoutSeconds": 30,
    "RetryCount": 3,
    "SlaDays": 30
  },

  "Isv": {
    "WhitelistedIps": ["127.0.0.1", "::1"],
    "Username": "isv_user",
    "Password": "isv_password",
    "EnableIpWhitelisting": false
  }
}
```

### Configuration Sections

| Section | Purpose | Direction |
|---------|---------|-----------|
| `MongoDbSettings` | Database connection | Internal |
| `Etisalat` | Credentials for calling Etisalat | Outbound |
| `Isv` | Credentials others use to call you | Inbound |

### Environment Variables

Override settings using double underscore notation:

```bash
# Docker / Linux
export MongoDbSettings__ConnectionString="mongodb://mongo-prod:27017"
export Etisalat__Username="prod_user"
export Etisalat__Password="prod_password"

# Windows PowerShell
$env:MongoDbSettings__ConnectionString = "mongodb://mongo-prod:27017"
```

---

## Deployment

### Docker Compose

```bash
# Production deployment
docker-compose up -d --build

# View status
docker-compose ps

# View logs
docker-compose logs -f
```

### Manual Deployment

```bash
# Build for release
dotnet publish -c Release -o ./publish

# Run
cd publish
dotnet EtisalatSaasCallback.dll
```

### Azure App Service

```bash
# Create resources
az group create --name rg-etisalat --location uaenorth
az webapp create --name etisalat-callback --resource-group rg-etisalat --runtime "DOTNET|10.0"

# Deploy
az webapp deployment source config-zip --src publish.zip
```

See [docs/deployment-guide.html](docs/deployment-guide.html) for detailed instructions.

---

## Testing

### cURL Examples

```bash
# Generate Base64 credentials
echo -n "isv_user:isv_password" | base64
# Output: aXN2X3VzZXI6aXN2X3Bhc3N3b3Jk

# Test inbound callback
curl -X POST http://localhost:5000/rest/xaas/v1/isvProvisioningStatus \
  -H "Content-Type: application/json" \
  -H "Authorization: Basic aXN2X3VzZXI6aXN2X3Bhc3N3b3Jk" \
  -d '{
    "isvProvisioningStatusRequest": {
      "referenceNumber": "REF123456",
      "subscriptionId": "SUB789012",
      "action": "ACCEPTED",
      "billingDate": "20260609143000",
      "serviceAttribute": []
    }
  }'

# Test health
curl http://localhost:5000/api/health
```

### PowerShell

```powershell
$cred = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("isv_user:isv_password"))

$body = @{
    isvProvisioningStatusRequest = @{
        referenceNumber = "REF123456"
        subscriptionId = "SUB789012"
        action = "ACCEPTED"
        billingDate = "20260609143000"
        serviceAttribute = @()
    }
} | ConvertTo-Json -Depth 3

Invoke-RestMethod -Uri "http://localhost:5000/rest/xaas/v1/isvProvisioningStatus" `
    -Method POST `
    -Headers @{ Authorization = "Basic $cred"; "Content-Type" = "application/json" } `
    -Body $body
```

### Swagger UI

Access interactive API documentation at: `http://localhost:5000/swagger`

---

## Error Codes

| Code | Description | Resolution |
|------|-------------|------------|
| 0 | Successful | - |
| 1 | Authentication Failed | Check credentials |
| 2 | Authorization Failed | Contact Etisalat |
| 3 | Input validation error | Check request format |
| 4 | Reference Number Not Found | Use valid reference |
| 5 | Tenant ID not found | Verify tenant config |
| 6 | Subscription ID not found | Use valid subscription |
| 7 | Invalid Action | Use ACCEPTED/REJECTED/EXPIRED |
| 8 | Originator not whitelisted | Add IP to whitelist |
| 99 | Internal Error | Check logs |
| 100 | Account Suspended | Only REJECTED/EXPIRED allowed |
| 101 | Account Ceased | No callbacks allowed |

---

## Documentation

Open the HTML documentation in your browser:

- **Full Documentation:** [docs/index.html](docs/index.html)
- **Deployment Guide:** [docs/deployment-guide.html](docs/deployment-guide.html)
- **API Reference:** [docs/api-reference.html](docs/api-reference.html)

---

## Production Checklist

- [ ] HTTPS enabled
- [ ] Real Etisalat credentials configured
- [ ] IP whitelisting enabled (`EnableIpWhitelisting: true`)
- [ ] Etisalat IPs added to whitelist
- [ ] Your public IP shared with Etisalat
- [ ] MongoDB secured with authentication
- [ ] Logs directory configured
- [ ] Health endpoint tested
- [ ] Backup strategy for MongoDB

---

## License

Proprietary - imarouf

## Support

For issues or questions, contact the development team.
