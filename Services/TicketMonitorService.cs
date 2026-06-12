using System.Text.RegularExpressions;
using EtisalatSaasCallback.Configuration;
using EtisalatSaasCallback.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EtisalatSaasCallback.Services;

public class TicketMonitorService : BackgroundService
{
    private readonly ILogger<TicketMonitorService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TicketMonitorSettings _monitorSettings;
    private readonly IMongoCollection<Ticket> _ticketCollection;
    private readonly IMongoCollection<TrackedTicket> _trackedCollection;

    public TicketMonitorService(
        ILogger<TicketMonitorService> logger,
        IServiceProvider serviceProvider,
        IOptions<TicketMonitorSettings> monitorSettings)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _monitorSettings = monitorSettings.Value;

        var client = new MongoClient(_monitorSettings.TicketDbConnectionString);
        var database = client.GetDatabase(_monitorSettings.TicketDbName);
        _ticketCollection = database.GetCollection<Ticket>(_monitorSettings.TicketCollectionName);
        _trackedCollection = database.GetCollection<TrackedTicket>("tracked_tickets");

        _logger.LogInformation("TicketMonitorService initialized. Monitoring {Collection} for subscription tickets",
            _monitorSettings.TicketCollectionName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_monitorSettings.Enabled)
        {
            _logger.LogInformation("TicketMonitorService is disabled");
            return;
        }

        await EnsureCollectionAndIndexesAsync();

        _logger.LogInformation("TicketMonitorService started. Polling interval: {Interval} seconds",
            _monitorSettings.PollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DiscoverNewTicketsAsync(stoppingToken);
                await MonitorTrackedTicketsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ticket monitor cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(_monitorSettings.PollingIntervalSeconds), stoppingToken);
        }
    }

    private async Task EnsureCollectionAndIndexesAsync()
    {
        _logger.LogInformation("Ensuring tracked_tickets collection and indexes exist...");

        var indexKeys = Builders<TrackedTicket>.IndexKeys;
        var indexes = new[]
        {
            new CreateIndexModel<TrackedTicket>(indexKeys.Ascending(t => t.TicketId), new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<TrackedTicket>(indexKeys.Ascending(t => t.TicketNumber)),
            new CreateIndexModel<TrackedTicket>(indexKeys.Ascending(t => t.CallbackSent)),
            new CreateIndexModel<TrackedTicket>(indexKeys.Ascending(t => t.CurrentStatus))
        };

        await _trackedCollection.Indexes.CreateManyAsync(indexes);
        _logger.LogInformation("tracked_tickets collection indexes ensured");
    }

    private static string GetTicketIdString(BsonValue id)
    {
        if (id.IsBsonBinaryData)
        {
            var binary = id.AsBsonBinaryData;
            if (binary.SubType == BsonBinarySubType.UuidStandard || binary.SubType == BsonBinarySubType.UuidLegacy)
            {
                return binary.ToGuid().ToString();
            }
        }
        return id.ToString()!;
    }

    private async Task DiscoverNewTicketsAsync(CancellationToken cancellationToken)
    {
        var filterBuilder = Builders<Ticket>.Filter;
        var filter = filterBuilder.Eq(t => t.IsArchived, false);

        // Collect all configured category/sub-category pairs: the legacy single pair plus the Categories list.
        var pairs = new List<CategoryPair>();
        if (!string.IsNullOrEmpty(_monitorSettings.TicketCategoryId) ||
            !string.IsNullOrEmpty(_monitorSettings.TicketSubCategoryId))
        {
            pairs.Add(new CategoryPair
            {
                TicketCategoryId = _monitorSettings.TicketCategoryId,
                TicketSubCategoryId = _monitorSettings.TicketSubCategoryId
            });
        }
        pairs.AddRange(_monitorSettings.Categories);

        var pairFilters = new List<FilterDefinition<Ticket>>();
        foreach (var pair in pairs)
        {
            var parts = new List<FilterDefinition<Ticket>>();

            if (!string.IsNullOrEmpty(pair.TicketCategoryId) &&
                Guid.TryParse(pair.TicketCategoryId, out var categoryGuid))
            {
                var categoryBinary = new BsonBinaryData(categoryGuid, GuidRepresentation.Standard);
                parts.Add(filterBuilder.Eq("TicketCategoryId", categoryBinary));
            }

            if (!string.IsNullOrEmpty(pair.TicketSubCategoryId) &&
                Guid.TryParse(pair.TicketSubCategoryId, out var subCategoryGuid))
            {
                var subCategoryBinary = new BsonBinaryData(subCategoryGuid, GuidRepresentation.Standard);
                parts.Add(filterBuilder.Eq("TicketSubCategoryId", subCategoryBinary));
            }

            if (parts.Count > 0)
                pairFilters.Add(filterBuilder.And(parts));
        }

        // Match a ticket if it satisfies ANY configured pair.
        if (pairFilters.Count > 0)
            filter &= filterBuilder.Or(pairFilters);

        var subscriptionTickets = await _ticketCollection
            .Find(filter)
            .ToListAsync(cancellationToken);

        foreach (var ticket in subscriptionTickets)
        {
            var ticketIdString = GetTicketIdString(ticket.Id);
            var existingTracked = await _trackedCollection
                .Find(t => t.TicketId == ticketIdString)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingTracked == null)
            {
                var (referenceNumber, subscriptionIdFromBody, accountId, planId, companyName, customerName, customerEmail, customerPhone, product, quantity) =
                    ExtractTicketDetails(ticket.Body);

                var subscriptionIdFromSubject = ExtractSubscriptionIdFromSubject(ticket.Subject);
                var subscriptionId = subscriptionIdFromSubject ?? subscriptionIdFromBody;

                var (isValid, validationErrors) = ValidateTicketPayload(referenceNumber, subscriptionId, accountId);

                var trackedTicket = new TrackedTicket
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    TicketId = ticketIdString,
                    TicketNumber = ticket.TicketNumber,
                    Subject = ticket.Subject,
                    ReferenceNumber = referenceNumber,
                    SubscriptionId = subscriptionId,
                    AccountId = accountId,
                    PlanId = planId,
                    CompanyName = companyName,
                    CustomerName = customerName,
                    CustomerEmail = customerEmail,
                    CustomerPhone = customerPhone,
                    Product = product ?? planId,
                    Quantity = quantity,
                    CurrentStatus = ticket.Status,
                    TrackedAt = DateTime.UtcNow,
                    LastCheckedAt = DateTime.UtcNow,
                    TicketCreatedDate = ticket.CreatedDate,
                    TicketModifiedDate = ticket.ModifiedDate,
                    IsValidPayload = isValid,
                    ValidationErrors = validationErrors
                };

                await _trackedCollection.InsertOneAsync(trackedTicket, cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "New ticket tracked: {TicketNumber} - {Subject}, Status: {Status}, Reference: {Reference}, Valid: {IsValid}",
                    ticket.TicketNumber, ticket.Subject, ticket.Status, referenceNumber ?? "N/A", isValid);

                if (!isValid)
                {
                    await SendRejectedCallbackAsync(trackedTicket, "Request is invalid", cancellationToken);
                }
            }
        }
    }

    private (bool isValid, List<string>? errors) ValidateTicketPayload(
        string? referenceNumber, string? subscriptionId, string? accountId)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(referenceNumber))
            errors.Add("Missing Reference Number");

        if (string.IsNullOrWhiteSpace(subscriptionId) && string.IsNullOrWhiteSpace(accountId))
            errors.Add("Missing Subscription ID and Account ID");

        return (errors.Count == 0, errors.Count > 0 ? errors : null);
    }

    private async Task MonitorTrackedTicketsAsync(CancellationToken cancellationToken)
    {
        var pendingTickets = await _trackedCollection
            .Find(t => !t.CallbackSent)
            .ToListAsync(cancellationToken);

        _logger.LogDebug("Monitoring {Count} tracked tickets for status changes", pendingTickets.Count);

        foreach (var trackedTicket in pendingTickets)
        {
            Ticket? currentTicket = null;
            if (Guid.TryParse(trackedTicket.TicketId, out var ticketGuid))
            {
                var binaryGuid = new BsonBinaryData(ticketGuid, GuidRepresentation.Standard);
                var filter = Builders<Ticket>.Filter.Eq("_id", binaryGuid);
                currentTicket = await _ticketCollection.Find(filter).FirstOrDefaultAsync(cancellationToken);
            }

            if (currentTicket == null)
            {
                _logger.LogWarning("Tracked ticket {TicketNumber} no longer exists", trackedTicket.TicketNumber);
                continue;
            }

            var statusChanged = currentTicket.Status != trackedTicket.CurrentStatus;
            var updateDef = Builders<TrackedTicket>.Update
                .Set(t => t.LastCheckedAt, DateTime.UtcNow)
                .Set(t => t.TicketModifiedDate, currentTicket.ModifiedDate);

            if (statusChanged)
            {
                updateDef = updateDef
                    .Set(t => t.PreviousStatus, trackedTicket.CurrentStatus)
                    .Set(t => t.CurrentStatus, currentTicket.Status)
                    .Set(t => t.StatusChangedAt, DateTime.UtcNow);

                _logger.LogInformation(
                    "Ticket {TicketNumber} status changed: {OldStatus} -> {NewStatus}",
                    trackedTicket.TicketNumber, trackedTicket.CurrentStatus, currentTicket.Status);
            }

            await _trackedCollection.UpdateOneAsync(
                t => t.Id == trackedTicket.Id,
                updateDef,
                cancellationToken: cancellationToken);
        }
    }

    private string? ExtractSubscriptionIdFromSubject(string subject)
    {
        var match = Regex.Match(subject, @"[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}");
        return match.Success ? match.Value : null;
    }

    private (string? referenceNumber, string? subscriptionId, string? accountId, string? planId,
             string? companyName, string? customerName, string? customerEmail, string? customerPhone,
             string? product, int? quantity)
        ExtractTicketDetails(string body)
    {
        string? referenceNumber = null;
        string? subscriptionId = null;
        string? accountId = null;
        string? planId = null;
        string? companyName = null;
        string? customerName = null;
        string? customerEmail = null;
        string? customerPhone = null;
        string? product = null;
        int? quantity = null;

        var patterns = new Dictionary<string, string>
        {
            { "referenceNumber", @"Reference\s*(?:No\.?|Number):\s*(.+?)(?:\r?\n|$)" },
            { "subscriptionId", @"Subscri[bp]tion\s*ID:\s*(.+?)(?:\r?\n|$)" },
            { "accountId", @"Account\s*ID:\s*(.+?)(?:\r?\n|$)" },
            { "planId", @"Plan\s*ID:\s*(.+?)(?:\r?\n|$)" },
            { "companyName", @"(?:Company|Organization)\s*(?:Name)?:\s*(.+?)(?:\r?\n|$)" },
            { "customerName", @"(?:Contact\s*Name|Customer\s*Name|Name):\s*(.+?)(?:\r?\n|$)" },
            { "customerEmail", @"(?:Email|E-?mail):\s*(.+?)(?:\r?\n|$)" },
            { "customerPhone", @"(?:Phone\s*Number|Phone|Mobile|Contact\s*Number):\s*(.+?)(?:\r?\n|$)" },
            { "product", @"(?:Product|Service):\s*(.+?)(?:\r?\n|$)" },
            { "quantity", @"(?:Quantity|Qty|Licenses?):\s*(\d+)" }
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(body, pattern.Value, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var value = match.Groups[1].Value.Trim();
                switch (pattern.Key)
                {
                    case "referenceNumber": referenceNumber = value; break;
                    case "subscriptionId": subscriptionId = value; break;
                    case "accountId": accountId = value; break;
                    case "planId": planId = value; break;
                    case "companyName": companyName = value; break;
                    case "customerName": customerName = value; break;
                    case "customerEmail": customerEmail = value; break;
                    case "customerPhone": customerPhone = value; break;
                    case "product": product = value; break;
                    case "quantity": quantity = int.TryParse(value, out var q) ? q : null; break;
                }
            }
        }

        return (referenceNumber, subscriptionId, accountId, planId, companyName, customerName, customerEmail, customerPhone, product, quantity);
    }

    private async Task SendEtisalatCallbackAsync(
        TrackedTicket trackedTicket,
        Ticket currentTicket,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(trackedTicket.ReferenceNumber))
        {
            _logger.LogWarning(
                "Cannot send callback for ticket {TicketNumber} - missing reference number",
                trackedTicket.TicketNumber);
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var etisalatClient = scope.ServiceProvider.GetRequiredService<IEtisalatCallbackClient>();

        var request = new IsvProvisioningStatusRequest
        {
            ReferenceNumber = trackedTicket.ReferenceNumber,
            SubscriptionId = trackedTicket.SubscriptionId ?? trackedTicket.AccountId ?? trackedTicket.TicketNumber,
            Action = ProvisioningAction.Accepted,
            BillingDate = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            ServiceAttribute = new List<ServiceAttribute>
            {
                new() { Name = "ticketNumber", Value = trackedTicket.TicketNumber },
                new() { Name = "closedDate", Value = (currentTicket.ModifiedDate ?? DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss") },
                new() { Name = "customerName", Value = trackedTicket.CustomerName ?? "" },
                new() { Name = "companyName", Value = trackedTicket.CompanyName ?? "" },
                new() { Name = "product", Value = trackedTicket.Product ?? "" },
                new() { Name = "planId", Value = trackedTicket.PlanId ?? "" },
                new() { Name = "accountId", Value = trackedTicket.AccountId ?? "" }
            }
        };

        _logger.LogInformation(
            "Sending ACCEPTED callback to Etisalat. Reference: {Reference}, SubscriptionId: {SubscriptionId}",
            trackedTicket.ReferenceNumber, request.SubscriptionId);

        var response = await etisalatClient.SendProvisioningStatusAsync(request);

        var updateDef = Builders<TrackedTicket>.Update
            .Set(t => t.CallbackSent, true)
            .Set(t => t.CallbackSentAt, DateTime.UtcNow)
            .Set(t => t.CallbackAction, ProvisioningAction.Accepted)
            .Set(t => t.CallbackResponseCode, response.ResponseCode)
            .Set(t => t.CallbackResponseDescription, response.Description);

        await _trackedCollection.UpdateOneAsync(
            t => t.Id == trackedTicket.Id,
            updateDef,
            cancellationToken: cancellationToken);

        if (response.ResponseCode == ErrorCodes.Success)
        {
            _logger.LogInformation(
                "Successfully sent callback for ticket {TicketNumber}. Response: {Description}",
                trackedTicket.TicketNumber, response.Description);
        }
        else
        {
            _logger.LogWarning(
                "Callback response for ticket {TicketNumber}. Code: {Code}, Description: {Description}",
                trackedTicket.TicketNumber, response.ResponseCode, response.Description);
        }
    }

    private async Task SendRejectedCallbackAsync(
        TrackedTicket trackedTicket,
        string rejectionReason,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var etisalatClient = scope.ServiceProvider.GetRequiredService<IEtisalatCallbackClient>();

        var request = new IsvProvisioningStatusRequest
        {
            ReferenceNumber = trackedTicket.ReferenceNumber ?? trackedTicket.TicketNumber,
            SubscriptionId = trackedTicket.SubscriptionId ?? trackedTicket.AccountId ?? trackedTicket.TicketNumber,
            Action = ProvisioningAction.Rejected,
            BillingDate = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            ServiceAttribute = new List<ServiceAttribute>
            {
                new() { Name = "ticketNumber", Value = trackedTicket.TicketNumber },
                new() { Name = "rejectionReason", Value = rejectionReason },
                new() { Name = "validationErrors", Value = string.Join("; ", trackedTicket.ValidationErrors ?? new List<string>()) }
            }
        };

        _logger.LogInformation(
            "Sending REJECTED callback to Etisalat. Ticket: {TicketNumber}, Reason: {Reason}",
            trackedTicket.TicketNumber, rejectionReason);

        var response = await etisalatClient.SendProvisioningStatusAsync(request);

        var updateDef = Builders<TrackedTicket>.Update
            .Set(t => t.CallbackSent, true)
            .Set(t => t.CallbackSentAt, DateTime.UtcNow)
            .Set(t => t.CallbackAction, ProvisioningAction.Rejected)
            .Set(t => t.RejectionReason, rejectionReason)
            .Set(t => t.CallbackResponseCode, response.ResponseCode)
            .Set(t => t.CallbackResponseDescription, response.Description);

        await _trackedCollection.UpdateOneAsync(
            t => t.Id == trackedTicket.Id,
            updateDef,
            cancellationToken: cancellationToken);

        if (response.ResponseCode == ErrorCodes.Success)
        {
            _logger.LogInformation(
                "Successfully sent REJECTED callback for ticket {TicketNumber}",
                trackedTicket.TicketNumber);
        }
        else
        {
            _logger.LogWarning(
                "REJECTED callback response for ticket {TicketNumber}. Code: {Code}, Description: {Description}",
                trackedTicket.TicketNumber, response.ResponseCode, response.Description);
        }
    }
}
