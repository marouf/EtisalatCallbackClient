using EtisalatSaasCallback.Configuration;
using EtisalatSaasCallback.Models;
using EtisalatSaasCallback.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace EtisalatSaasCallback.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IMongoCollection<TrackedTicket> _trackedCollection;
    private readonly TicketMonitorSettings _monitorSettings;
    private readonly SlaSettings _slaSettings;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        IOptions<TicketMonitorSettings> monitorSettings,
        IOptions<SlaSettings> slaSettings,
        IServiceProvider serviceProvider,
        ILogger<DashboardController> logger)
    {
        _monitorSettings = monitorSettings.Value;
        _slaSettings = slaSettings.Value;
        _serviceProvider = serviceProvider;
        _logger = logger;

        var client = new MongoClient(_monitorSettings.TicketDbConnectionString);
        var database = client.GetDatabase(_monitorSettings.TicketDbName);
        _trackedCollection = database.GetCollection<TrackedTicket>("tracked_tickets");
    }

    public async Task<IActionResult> Index()
    {
        var total = await _trackedCollection.CountDocumentsAsync(FilterDefinition<TrackedTicket>.Empty);
        var pending = await _trackedCollection.CountDocumentsAsync(t => !t.CallbackSent);
        var callbackSent = await _trackedCollection.CountDocumentsAsync(t => t.CallbackSent);
        var closed = await _trackedCollection.CountDocumentsAsync(t => t.CurrentStatus == TicketStatus.Closed);

        ViewBag.Total = total;
        ViewBag.Pending = pending;
        ViewBag.CallbackSent = callbackSent;
        ViewBag.Closed = closed;
        ViewBag.MonitorEnabled = _monitorSettings.Enabled;
        ViewBag.PollingInterval = _monitorSettings.PollingIntervalSeconds;

        return View();
    }

    public async Task<IActionResult> Monitor(int page = 1, string? status = null, string? callback = null, string? search = null)
    {
        const int pageSize = 20;
        var filterBuilder = Builders<TrackedTicket>.Filter;
        var filter = filterBuilder.Empty;

        if (!string.IsNullOrEmpty(search))
        {
            var searchFilter = filterBuilder.Or(
                filterBuilder.Regex(t => t.Subject, new MongoDB.Bson.BsonRegularExpression(search, "i")),
                filterBuilder.Regex(t => t.TicketNumber, new MongoDB.Bson.BsonRegularExpression(search, "i"))
            );
            filter &= searchFilter;
        }

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<TicketStatus>(status, true, out var ticketStatus))
        {
            filter &= filterBuilder.Eq(t => t.CurrentStatus, ticketStatus);
        }

        if (!string.IsNullOrEmpty(callback))
        {
            if (callback == "failed")
            {
                filter &= filterBuilder.Eq(t => t.CallbackSent, true);
                filter &= filterBuilder.Ne(t => t.CallbackResponseCode, "0");
            }
            else if (callback == "true")
            {
                filter &= filterBuilder.Eq(t => t.CallbackSent, true);
                filter &= filterBuilder.Eq(t => t.CallbackResponseCode, "0");
            }
            else
            {
                filter &= filterBuilder.Eq(t => t.CallbackSent, false);
            }
        }

        var totalCount = await _trackedCollection.CountDocumentsAsync(filter);
        var tickets = await _trackedCollection
            .Find(filter)
            .SortByDescending(t => t.TrackedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        ViewBag.TotalCount = totalCount;
        ViewBag.StatusFilter = status;
        ViewBag.CallbackFilter = callback;
        ViewBag.SearchFilter = search;

        return View(tickets);
    }

    public async Task<IActionResult> SlaViolations(int page = 1, string? type = null)
    {
        const int pageSize = 20;
        var now = DateTime.UtcNow;
        var tickets = await _trackedCollection
            .Find(t => !t.CallbackSent && t.CurrentStatus != TicketStatus.Closed && t.CurrentStatus != TicketStatus.Rejected)
            .ToListAsync();

        var violations = new List<SlaViolationViewModel>();

        foreach (var ticket in tickets)
        {
            var rule = GetApplicableSlaRule(ticket);
            var slaDeadline = ticket.TrackedAt.AddDays(rule.SlaDays);
            var warningDate = ticket.TrackedAt.AddDays(rule.WarningDays);
            var daysRemaining = (slaDeadline - now).TotalDays;
            var daysElapsed = (now - ticket.TrackedAt).TotalDays;

            string violationStatus;
            if (now > slaDeadline)
                violationStatus = "violated";
            else if (now > warningDate)
                violationStatus = "warning";
            else
                violationStatus = "ok";

            if (violationStatus == "ok") continue;

            if (!string.IsNullOrEmpty(type) && violationStatus != type.ToLower())
                continue;

            violations.Add(new SlaViolationViewModel
            {
                Ticket = ticket,
                SlaDeadline = slaDeadline,
                DaysRemaining = Math.Round(daysRemaining, 1),
                DaysElapsed = Math.Round(daysElapsed, 1),
                ViolationStatus = violationStatus,
                AppliedRule = rule.Name,
                SlaDays = rule.SlaDays
            });
        }

        var sortedViolations = violations.OrderBy(v => v.DaysRemaining).ToList();
        var totalCount = sortedViolations.Count;
        var pagedViolations = sortedViolations.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        ViewBag.TotalCount = totalCount;
        ViewBag.ViolatedCount = violations.Count(v => v.ViolationStatus == "violated");
        ViewBag.WarningCount = violations.Count(v => v.ViolationStatus == "warning");
        ViewBag.TypeFilter = type;
        ViewBag.SlaRules = _slaSettings.Rules;

        return View(pagedViolations);
    }

    [HttpGet]
    public async Task<IActionResult> TicketDetails(string id)
    {
        var ticket = await _trackedCollection.Find(t => t.Id == id).FirstOrDefaultAsync();
        if (ticket == null)
            return NotFound();

        return View(ticket);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendCallback(string ticketId, string action, string? reason, string? referenceNumber)
    {
        var ticket = await _trackedCollection.Find(t => t.Id == ticketId).FirstOrDefaultAsync();
        if (ticket == null)
        {
            TempData["Error"] = "Ticket not found";
            return RedirectToAction("Monitor");
        }

        var refNum = !string.IsNullOrEmpty(referenceNumber) ? referenceNumber : ticket.ReferenceNumber;

        if (string.IsNullOrEmpty(refNum))
        {
            TempData["Error"] = "Reference number is required";
            return RedirectToAction("TicketDetails", new { id = ticketId });
        }

        if (!string.IsNullOrEmpty(referenceNumber) && referenceNumber != ticket.ReferenceNumber)
        {
            await _trackedCollection.UpdateOneAsync(
                t => t.Id == ticketId,
                Builders<TrackedTicket>.Update.Set(t => t.ReferenceNumber, referenceNumber));
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var etisalatClient = scope.ServiceProvider.GetRequiredService<IEtisalatCallbackClient>();

            var request = new IsvProvisioningStatusRequest
            {
                ReferenceNumber = refNum,
                SubscriptionId = ticket.SubscriptionId ?? ticket.AccountId ?? ticket.TicketNumber,
                Action = action,
                BillingDate = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                ServiceAttribute = new List<ServiceAttribute>
                {
                    new() { Name = "ticketNumber", Value = ticket.TicketNumber },
                    new() { Name = "manualAction", Value = "true" },
                    new() { Name = "reason", Value = reason ?? "" },
                    new() { Name = "actionBy", Value = User.Identity?.Name ?? "unknown" }
                }
            };

            var response = await etisalatClient.SendProvisioningStatusAsync(request);

            var updateDef = Builders<TrackedTicket>.Update
                .Set(t => t.CallbackSent, true)
                .Set(t => t.CallbackSentAt, DateTime.UtcNow)
                .Set(t => t.CallbackAction, action)
                .Set(t => t.CallbackResponseCode, response.ResponseCode)
                .Set(t => t.CallbackResponseDescription, response.Description);

            await _trackedCollection.UpdateOneAsync(t => t.Id == ticket.Id, updateDef);

            if (response.ResponseCode == ErrorCodes.Success)
            {
                TempData["Success"] = $"Callback {action} sent successfully for ticket {ticket.TicketNumber}";
                return RedirectToAction("Monitor");
            }
            else
            {
                TempData["Warning"] = $"Callback sent but failed. Response: {response.ResponseCode} - {response.Description}. You can retry.";
                return RedirectToAction("TicketDetails", new { id = ticketId });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending callback for ticket {TicketNumber}", ticket.TicketNumber);
            TempData["Error"] = $"Error sending callback: {ex.Message}";
            return RedirectToAction("TicketDetails", new { id = ticketId });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkCallback(string[] ticketIds, string action)
    {
        int success = 0, failed = 0;

        foreach (var ticketId in ticketIds)
        {
            var ticket = await _trackedCollection.Find(t => t.Id == ticketId).FirstOrDefaultAsync();
            if (ticket == null || string.IsNullOrEmpty(ticket.ReferenceNumber))
            {
                failed++;
                continue;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var etisalatClient = scope.ServiceProvider.GetRequiredService<IEtisalatCallbackClient>();

                var request = new IsvProvisioningStatusRequest
                {
                    ReferenceNumber = ticket.ReferenceNumber,
                    SubscriptionId = ticket.AccountId ?? ticket.TicketNumber,
                    Action = action,
                    BillingDate = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                    ServiceAttribute = new List<ServiceAttribute>
                    {
                        new() { Name = "ticketNumber", Value = ticket.TicketNumber },
                        new() { Name = "bulkAction", Value = "true" },
                        new() { Name = "actionBy", Value = User.Identity?.Name ?? "unknown" }
                    }
                };

                var response = await etisalatClient.SendProvisioningStatusAsync(request);

                var updateDef = Builders<TrackedTicket>.Update
                    .Set(t => t.CallbackSent, true)
                    .Set(t => t.CallbackSentAt, DateTime.UtcNow)
                    .Set(t => t.CallbackAction, action)
                    .Set(t => t.CallbackResponseCode, response.ResponseCode)
                    .Set(t => t.CallbackResponseDescription, response.Description);

                await _trackedCollection.UpdateOneAsync(t => t.Id == ticket.Id, updateDef);
                success++;
            }
            catch
            {
                failed++;
            }
        }

        TempData["Success"] = $"Bulk callback completed: {success} successful, {failed} failed";
        return RedirectToAction("Monitor");
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

public class SlaViolationViewModel
{
    public TrackedTicket Ticket { get; set; } = null!;
    public DateTime SlaDeadline { get; set; }
    public double DaysRemaining { get; set; }
    public double DaysElapsed { get; set; }
    public string ViolationStatus { get; set; } = null!;
    public string AppliedRule { get; set; } = null!;
    public int SlaDays { get; set; }
}
