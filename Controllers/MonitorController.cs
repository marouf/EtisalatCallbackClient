using EtisalatSaasCallback.Configuration;
using EtisalatSaasCallback.Models;
using EtisalatSaasCallback.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace EtisalatSaasCallback.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class MonitorController : ControllerBase
{
    private readonly IMongoCollection<TrackedTicket> _trackedCollection;
    private readonly TicketMonitorSettings _settings;
    private readonly SlaSettings _slaSettings;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MonitorController> _logger;

    public MonitorController(
        IOptions<TicketMonitorSettings> settings,
        IOptions<SlaSettings> slaSettings,
        IServiceProvider serviceProvider,
        ILogger<MonitorController> logger)
    {
        _settings = settings.Value;
        _slaSettings = slaSettings.Value;
        _serviceProvider = serviceProvider;
        _logger = logger;

        var client = new MongoClient(_settings.TicketDbConnectionString);
        var database = client.GetDatabase(_settings.TicketDbName);
        _trackedCollection = database.GetCollection<TrackedTicket>("tracked_tickets");
    }

    [HttpGet("tickets")]
    public async Task<IActionResult> GetTrackedTickets(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] bool? callbackSent = null)
    {
        var filterBuilder = Builders<TrackedTicket>.Filter;
        var filter = filterBuilder.Empty;

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<TicketStatus>(status, true, out var ticketStatus))
        {
            filter &= filterBuilder.Eq(t => t.CurrentStatus, ticketStatus);
        }

        if (callbackSent.HasValue)
        {
            filter &= filterBuilder.Eq(t => t.CallbackSent, callbackSent.Value);
        }

        var totalCount = await _trackedCollection.CountDocumentsAsync(filter);
        var tickets = await _trackedCollection
            .Find(filter)
            .SortByDescending(t => t.TrackedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return Ok(new
        {
            data = tickets,
            pagination = new
            {
                page,
                pageSize,
                totalCount,
                totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            }
        });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var total = await _trackedCollection.CountDocumentsAsync(FilterDefinition<TrackedTicket>.Empty);
        var pending = await _trackedCollection.CountDocumentsAsync(t => !t.CallbackSent);
        var callbackSent = await _trackedCollection.CountDocumentsAsync(t => t.CallbackSent);
        var closed = await _trackedCollection.CountDocumentsAsync(t => t.CurrentStatus == TicketStatus.Closed);

        var byStatus = new Dictionary<string, long>();
        foreach (TicketStatus status in Enum.GetValues<TicketStatus>())
        {
            var count = await _trackedCollection.CountDocumentsAsync(t => t.CurrentStatus == status);
            byStatus[status.ToString()] = count;
        }

        return Ok(new
        {
            total,
            pending,
            callbackSent,
            closed,
            byStatus,
            monitorEnabled = _settings.Enabled,
            pollingInterval = _settings.PollingIntervalSeconds
        });
    }

    [HttpGet("tickets/{id}")]
    public async Task<IActionResult> GetTicketById(string id)
    {
        var ticket = await _trackedCollection.Find(t => t.Id == id).FirstOrDefaultAsync();
        if (ticket == null)
            return NotFound(new { message = "Ticket not found" });

        return Ok(ticket);
    }

    [HttpPost("callback")]
    public async Task<IActionResult> SendManualCallback([FromBody] ManualCallbackRequest request)
    {
        var ticket = await _trackedCollection.Find(t => t.Id == request.TicketId).FirstOrDefaultAsync();
        if (ticket == null)
            return NotFound(new { message = "Ticket not found" });

        if (string.IsNullOrEmpty(ticket.ReferenceNumber))
            return BadRequest(new { message = "Ticket has no reference number" });

        var result = await SendCallbackAsync(ticket, request.Action, request.Reason);
        return Ok(result);
    }

    [HttpPost("callback/bulk")]
    public async Task<IActionResult> SendBulkCallback([FromBody] BulkCallbackRequest request)
    {
        var results = new List<CallbackResult>();

        foreach (var ticketId in request.TicketIds)
        {
            var ticket = await _trackedCollection.Find(t => t.Id == ticketId).FirstOrDefaultAsync();
            if (ticket == null)
            {
                results.Add(new CallbackResult
                {
                    TicketId = ticketId,
                    TicketNumber = "Unknown",
                    Success = false,
                    Error = "Ticket not found"
                });
                continue;
            }

            if (string.IsNullOrEmpty(ticket.ReferenceNumber))
            {
                results.Add(new CallbackResult
                {
                    TicketId = ticketId,
                    TicketNumber = ticket.TicketNumber,
                    Success = false,
                    Error = "No reference number"
                });
                continue;
            }

            var result = await SendCallbackAsync(ticket, request.Action, request.Reason);
            results.Add(result);
        }

        return Ok(new
        {
            totalProcessed = results.Count,
            successful = results.Count(r => r.Success),
            failed = results.Count(r => !r.Success),
            results
        });
    }

    private async Task<CallbackResult> SendCallbackAsync(TrackedTicket ticket, string action, string? reason)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var etisalatClient = scope.ServiceProvider.GetRequiredService<IEtisalatCallbackClient>();

            var provisioningRequest = new IsvProvisioningStatusRequest
            {
                ReferenceNumber = ticket.ReferenceNumber!,
                SubscriptionId = ticket.SubscriptionId ?? ticket.AccountId ?? ticket.TicketNumber,
                Action = action,
                BillingDate = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                ServiceAttribute = new List<ServiceAttribute>()
            };

            _logger.LogInformation(
                "Sending manual {Action} callback for ticket {TicketNumber}. Reference: {Reference}",
                action, ticket.TicketNumber, ticket.ReferenceNumber);

            var response = await etisalatClient.SendProvisioningStatusAsync(provisioningRequest);

            var updateDef = Builders<TrackedTicket>.Update
                .Set(t => t.CallbackSent, true)
                .Set(t => t.CallbackSentAt, DateTime.UtcNow)
                .Set(t => t.CallbackAction, action)
                .Set(t => t.CallbackResponseCode, response.ResponseCode)
                .Set(t => t.CallbackResponseDescription, response.Description);

            await _trackedCollection.UpdateOneAsync(t => t.Id == ticket.Id, updateDef);

            return new CallbackResult
            {
                TicketId = ticket.Id!,
                TicketNumber = ticket.TicketNumber,
                Success = response.ResponseCode == ErrorCodes.Success,
                ResponseCode = response.ResponseCode,
                ResponseDescription = response.Description
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending callback for ticket {TicketNumber}", ticket.TicketNumber);
            return new CallbackResult
            {
                TicketId = ticket.Id!,
                TicketNumber = ticket.TicketNumber,
                Success = false,
                Error = ex.Message
            };
        }
    }

    [HttpGet("sla/settings")]
    public IActionResult GetSlaSettings()
    {
        return Ok(new
        {
            enabled = _slaSettings.Enabled,
            defaultSlaDays = _slaSettings.DefaultSlaDays,
            warningThresholdDays = _slaSettings.WarningThresholdDays,
            rules = _slaSettings.Rules
        });
    }

    [HttpGet("sla/violations")]
    public async Task<IActionResult> GetSlaViolations(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? violationType = null)
    {
        var now = DateTime.UtcNow;
        var tickets = await _trackedCollection
            .Find(t => !t.CallbackSent && t.CurrentStatus != TicketStatus.Closed && t.CurrentStatus != TicketStatus.Rejected)
            .ToListAsync();

        var violations = new List<SlaViolationDto>();

        foreach (var ticket in tickets)
        {
            var rule = GetApplicableSlaRule(ticket);
            var slaDeadline = ticket.TrackedAt.AddDays(rule.SlaDays);
            var warningDate = ticket.TrackedAt.AddDays(rule.WarningDays);
            var daysRemaining = (slaDeadline - now).TotalDays;
            var daysElapsed = (now - ticket.TrackedAt).TotalDays;

            string status;
            if (now > slaDeadline)
                status = "violated";
            else if (now > warningDate)
                status = "warning";
            else
                status = "ok";

            if (status == "ok") continue;

            if (!string.IsNullOrEmpty(violationType) && status != violationType.ToLower())
                continue;

            violations.Add(new SlaViolationDto
            {
                TicketId = ticket.Id!,
                TicketNumber = ticket.TicketNumber,
                Subject = ticket.Subject,
                ReferenceNumber = ticket.ReferenceNumber,
                AccountId = ticket.AccountId,
                CurrentStatus = ticket.CurrentStatus.ToString(),
                TrackedAt = ticket.TrackedAt,
                SlaDeadline = slaDeadline,
                DaysRemaining = Math.Round(daysRemaining, 1),
                DaysElapsed = Math.Round(daysElapsed, 1),
                ViolationStatus = status,
                AppliedRule = rule.Name,
                SlaDays = rule.SlaDays
            });
        }

        var sortedViolations = violations
            .OrderBy(v => v.DaysRemaining)
            .ToList();

        var totalCount = sortedViolations.Count;
        var pagedViolations = sortedViolations
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new
        {
            data = pagedViolations,
            summary = new
            {
                totalViolations = violations.Count(v => v.ViolationStatus == "violated"),
                totalWarnings = violations.Count(v => v.ViolationStatus == "warning"),
                total = violations.Count
            },
            pagination = new
            {
                page,
                pageSize,
                totalCount,
                totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            }
        });
    }

    [HttpGet("sla/stats")]
    public async Task<IActionResult> GetSlaStats()
    {
        var now = DateTime.UtcNow;
        var tickets = await _trackedCollection
            .Find(t => !t.CallbackSent && t.CurrentStatus != TicketStatus.Closed && t.CurrentStatus != TicketStatus.Rejected)
            .ToListAsync();

        int violated = 0, warning = 0, ok = 0;

        foreach (var ticket in tickets)
        {
            var rule = GetApplicableSlaRule(ticket);
            var slaDeadline = ticket.TrackedAt.AddDays(rule.SlaDays);
            var warningDate = ticket.TrackedAt.AddDays(rule.WarningDays);

            if (now > slaDeadline)
                violated++;
            else if (now > warningDate)
                warning++;
            else
                ok++;
        }

        return Ok(new
        {
            violated,
            warning,
            ok,
            total = violated + warning + ok,
            slaEnabled = _slaSettings.Enabled
        });
    }

    private SlaRule GetApplicableSlaRule(TrackedTicket ticket)
    {
        if (_slaSettings.Rules.Count == 0)
        {
            return new SlaRule
            {
                Name = "Default",
                SlaDays = _slaSettings.DefaultSlaDays,
                WarningDays = _slaSettings.WarningThresholdDays,
                AppliesTo = "default"
            };
        }

        var subjectLower = ticket.Subject?.ToLower() ?? "";

        if (subjectLower.Contains("critical") || subjectLower.Contains("urgent"))
        {
            var critical = _slaSettings.Rules.FirstOrDefault(r => r.AppliesTo == "critical");
            if (critical != null) return critical;
        }

        if (subjectLower.Contains("priority") || subjectLower.Contains("high"))
        {
            var priority = _slaSettings.Rules.FirstOrDefault(r => r.AppliesTo == "priority");
            if (priority != null) return priority;
        }

        return _slaSettings.Rules.FirstOrDefault(r => r.AppliesTo == "default")
               ?? _slaSettings.Rules.First();
    }
}

public class SlaViolationDto
{
    public string TicketId { get; set; } = null!;
    public string TicketNumber { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public string? ReferenceNumber { get; set; }
    public string? AccountId { get; set; }
    public string CurrentStatus { get; set; } = null!;
    public DateTime TrackedAt { get; set; }
    public DateTime SlaDeadline { get; set; }
    public double DaysRemaining { get; set; }
    public double DaysElapsed { get; set; }
    public string ViolationStatus { get; set; } = null!;
    public string AppliedRule { get; set; } = null!;
    public int SlaDays { get; set; }
}
