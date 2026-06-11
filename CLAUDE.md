# Etisalat SaaS Callback Service

## Project Overview
A .NET 10 service that monitors ServiceMe tickets for subscription requests and sends provisioning status callbacks to Etisalat XaaS platform.

## Tech Stack
- **.NET 10** - ASP.NET Core MVC + Web API
- **MongoDB** - Data storage (ServiceMe database + local collections)
- **Docker** - Containerized deployment
- **Azure Container Registry** - `etisalatsaascallback.azurecr.io/etisalatsaascallback-api:latest`

## Architecture

### Databases
- **ServiceMe Database** (read-only) - Source of tickets (`Ticket` collection)
- **Local Collections** (in ServiceMe database):
  - `tracked_tickets` - Tickets being monitored
  - `provisioning_records` - Callback history
  - `subscription_states` - Subscription state tracking
  - `serviceme_users` - UI user management

### Key Services
- `TicketMonitorService` - Background service polling for subscription tickets
- `EtisalatCallbackClient` - HTTP client for Etisalat API
- `UserService` - User authentication and management
- `ProvisioningService` - Callback processing logic

## Etisalat API Integration

### Endpoint
- **UAT**: `https://contentapi-s.etisalat.ae/rest/xaas/v1/isvProvisioningStatus`
- **Auth**: Basic Authentication (credentials in appsettings)

### Callback Request Format
```json
{
  "isvProvisioningStatusRequest": {
    "referenceNumber": "8718420424646786",
    "subscriptionId": "81acb0d4-ce93-40b7-b07e-c9a0bdf69f33",
    "billingDate": "20260610235650",
    "action": "ACCEPTED|REJECTED|EXPIRED",
    "serviceAttribute": [...]
  }
}
```

### Actions
- `ACCEPTED` - Customer successfully onboarded, start billing
- `REJECTED` - Customer not onboarded, issue or declined
- `EXPIRED` - SLA breached (30 days), customer not onboarded

## Ticket Field Extraction

### From Subject (Priority for Subscription ID)
Pattern: GUID at end of subject
```
"Subscription - New Subscription Request - 81acb0d4-ce93-40b7-b07e-c9a0bdf69f33"
                                           ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
                                           Subscription ID (GUID)
```

### From Body
```
Subscripiton ID: 81acb0d4-ce93-40b7-b07e-c9a0bdf69f33  -> subscriptionId (fallback)
Reference No.: 8718420424646786                        -> referenceNumber
Account ID: 707120137                                  -> accountId
Plan ID: GSP-MSS-P2-MO                                 -> planId
Quantity: 1                                            -> quantity
Contact Name: Surbhi                                   -> customerName
Company: DNATA                                         -> companyName
Email: test@test.ae                                    -> customerEmail
Phone Number: 971565676545                             -> customerPhone
```

### Subscription ID Priority in Callbacks
```csharp
SubscriptionId = ticket.SubscriptionId ?? ticket.AccountId ?? ticket.TicketNumber
```

## Request Types (from Etisalat)
1. New Purchase Request - Creates new subscription
2. Suspension Request
3. Reactivation Request
4. Cessation Request
5. Cancellation Request
6. Modify Quantity Request
7. Change Plan Request
8. Modify Email Request
9. Modify Account ID Request

## Authentication

### API (Basic Auth)
- Configured in `Isv` settings
- Used for `/rest/xaas/v1/*` endpoints
- Controllers use `[Authorize(AuthenticationSchemes = "BasicAuth")]`

### UI (Cookie Auth)
- Users stored in `serviceme_users` collection
- Roles: Admin, User
- Default admin created on startup from `UiAuth` settings
- Password hashing: SHA256 with salt (format: `salt:hash`)

### User Management (`/Users`)
- **Admin-only** access (`[Authorize(Roles = "Admin")]`)
- CRUD operations for UI users
- Features:
  - Create new users with role assignment
  - Edit user details (email, display name, role, active status)
  - Change password (admin can reset any user's password)
  - Delete users (cannot delete self)
  - Self-protection: cannot deactivate or demote own account
- User model fields (all with `serviceme_` prefix in MongoDB):
  - `serviceme_username` - unique login name
  - `serviceme_passwordHash` - salted SHA256 hash
  - `serviceme_email` - optional email
  - `serviceme_displayName` - display name
  - `serviceme_role` - Admin (1) or User (0)
  - `serviceme_isActive` - account active flag
  - `serviceme_createdAt`, `serviceme_lastLoginAt`, `serviceme_createdBy`

### My Profile (`/Account/ChangePassword`)
- Any authenticated user can change their own password
- Requires current password verification

## Key Configuration (appsettings.json)

```json
{
  "MongoDB": {
    "ConnectionString": "mongodb://...",
    "DatabaseName": "ServiceMeTest"
  },
  "Etisalat": {
    "BaseUrl": "https://contentapi-s.etisalat.ae/rest/xaas/v1/",
    "IsvProvisioningStatusEndpoint": "isvProvisioningStatus",
    "Username": "YOUR_ETISALAT_USERNAME",
    "Password": "YOUR_ETISALAT_PASSWORD"
  },
  "TicketMonitor": {
    "Enabled": true,
    "PollingIntervalSeconds": 30,
    "TicketCategoryId": "GUID for Subscription category"
  },
  "UiAuth": {
    "Username": "admin",
    "Password": "admin123"
  }
}
```

## API Endpoints

### Inbound (from Etisalat/ISV)
- `POST /rest/xaas/v1/isvProvisioningStatus` - Receive callbacks

### Outbound (to Etisalat)
- `POST /rest/xaas/v1/sendCallback` - Send callbacks manually

### Monitor API
- `GET /api/monitor/tickets` - List tracked tickets
- `POST /api/monitor/callback` - Send manual callback
- `POST /api/monitor/callback/bulk` - Bulk callbacks

### Health
- `GET /api/health` - Basic health check
- `GET /api/health/detailed` - Detailed with MongoDB status

## UI Routes
- `/Account/Login` - Login page
- `/Account/ChangePassword` - Change own password (authenticated users)
- `/Account/AccessDenied` - Access denied page
- `/Dashboard` - Main dashboard
- `/Dashboard/Monitor` - Ticket monitor
- `/Dashboard/TicketDetails/{id}` - Ticket details
- `/Dashboard/SlaViolations` - SLA tracking
- `/Users` - User list (Admin only)
- `/Users/Create` - Create new user (Admin only)
- `/Users/Edit/{id}` - Edit user (Admin only)
- `/Users/ChangePassword/{id}` - Reset user password (Admin only)

## Deployment

### Build & Push
```bash
docker build -t etisalatsaascallback.azurecr.io/etisalatsaascallback-api:latest .
docker push etisalatsaascallback.azurecr.io/etisalatsaascallback-api:latest
```

### Environment Variables (override appsettings)
- `MongoDB__ConnectionString`
- `Etisalat__Username`
- `Etisalat__Password`

## MongoDB Naming Convention
- Collections/fields use `serviceme_` prefix for local data
- Example: `serviceme_users`, `serviceme_username`

## Key Files

### Models
- `Models/User.cs` - User entity + view models (CreateUser, EditUser, ChangePassword, MyProfile)
- `Models/TrackedTicket.cs` - Tracked ticket with extracted fields
- `Models/ProvisioningRecord.cs` - Callback history
- `Models/SubscriptionState.cs` - Subscription state tracking

### Services
- `Services/UserService.cs` - User CRUD, password hashing, authentication
- `Services/TicketMonitorService.cs` - Background polling, field extraction
- `Services/EtisalatCallbackClient.cs` - HTTP client for Etisalat API
- `Services/ProvisioningService.cs` - Callback business logic
- `Services/MongoDbService.cs` - MongoDB operations

### Controllers
- `Controllers/AccountController.cs` - Login, logout, change own password
- `Controllers/UsersController.cs` - User management (Admin only)
- `Controllers/DashboardController.cs` - UI dashboard, ticket details
- `Controllers/MonitorController.cs` - API for manual callbacks
- `Controllers/CallbackController.cs` - Inbound callback endpoint
- `Controllers/HealthController.cs` - Health checks

## Important Notes
- URL construction: BaseUrl must have trailing `/`, endpoint must NOT have leading `/`
- Ticket status `Closed` (enum value 4) triggers automatic ACCEPTED callback
- IP whitelisting required by Etisalat for production access
- SLA: 30 days to send callback before marking as EXPIRED
- API endpoints use `[Authorize(AuthenticationSchemes = "BasicAuth")]` to avoid Cookie auth conflicts
