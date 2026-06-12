using System.Globalization;
using System.Text;
using ClosedXML.Excel;
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
        var callbackSuccess = await _trackedCollection.CountDocumentsAsync(t => t.CallbackSent && t.CallbackResponseCode == "0");
        var callbackFailed = await _trackedCollection.CountDocumentsAsync(t => t.CallbackSent && t.CallbackResponseCode != "0");
        var closed = await _trackedCollection.CountDocumentsAsync(t => t.CurrentStatus == TicketStatus.Closed);

        ViewBag.Total = total;
        ViewBag.Pending = pending;
        ViewBag.CallbackSuccess = callbackSuccess;
        ViewBag.CallbackFailed = callbackFailed;
        ViewBag.Closed = closed;
        ViewBag.MonitorEnabled = _monitorSettings.Enabled;
        ViewBag.PollingInterval = _monitorSettings.PollingIntervalSeconds;

        var recentTickets = await _trackedCollection
            .Find(FilterDefinition<TrackedTicket>.Empty)
            .SortByDescending(t => t.TicketCreatedDate)
            .Limit(10)
            .ToListAsync();
        ViewBag.RecentTickets = recentTickets;

        return View();
    }

    public async Task<IActionResult> Monitor(int page = 1, string? status = null, string? callback = null, string? search = null,
        string? dateRange = null, string? dateFrom = null, string? dateTo = null, string? sort = null, string? dir = null)
    {
        const int pageSize = 20;
        var filter = BuildTicketFilter(status, callback, search, dateRange, dateFrom, dateTo);

        var sortDef = BuildSort(sort, dir);

        var totalCount = await _trackedCollection.CountDocumentsAsync(filter);
        var tickets = await _trackedCollection
            .Find(filter)
            .Sort(sortDef)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        ViewBag.TotalCount = totalCount;
        ViewBag.StatusFilter = status;
        ViewBag.CallbackFilter = callback;
        ViewBag.SearchFilter = search;
        ViewBag.DateRange = dateRange;
        ViewBag.DateFrom = dateFrom;
        ViewBag.DateTo = dateTo;
        ViewBag.Sort = sort;
        ViewBag.Dir = dir;

        return View(tickets);
    }

    private static SortDefinition<TrackedTicket> BuildSort(string? sort, string? dir)
    {
        var sortBuilder = Builders<TrackedTicket>.Sort;
        var ascending = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);

        return sort switch
        {
            "ticket" => ascending ? sortBuilder.Ascending(t => t.TicketNumber) : sortBuilder.Descending(t => t.TicketNumber),
            "subject" => ascending ? sortBuilder.Ascending(t => t.Subject) : sortBuilder.Descending(t => t.Subject),
            "reference" => ascending ? sortBuilder.Ascending(t => t.ReferenceNumber) : sortBuilder.Descending(t => t.ReferenceNumber),
            "status" => ascending ? sortBuilder.Ascending(t => t.CurrentStatus) : sortBuilder.Descending(t => t.CurrentStatus),
            "callback" => ascending ? sortBuilder.Ascending(t => t.CallbackSent) : sortBuilder.Descending(t => t.CallbackSent),
            "created" => ascending ? sortBuilder.Ascending(t => t.TicketCreatedDate) : sortBuilder.Descending(t => t.TicketCreatedDate),
            _ => sortBuilder.Descending(t => t.TrackedAt)
        };
    }

    private static FilterDefinition<TrackedTicket> BuildTicketFilter(
        string? status, string? callback, string? search,
        string? dateRange = null, string? dateFrom = null, string? dateTo = null)
    {
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

        var (from, to) = ResolveDateRange(dateRange, dateFrom, dateTo);
        if (from.HasValue)
            filter &= filterBuilder.Gte(t => t.TicketCreatedDate, from.Value);
        if (to.HasValue)
            filter &= filterBuilder.Lte(t => t.TicketCreatedDate, to.Value);

        return filter;
    }

    private static (DateTime? From, DateTime? To) ResolveDateRange(string? dateRange, string? dateFrom, string? dateTo)
    {
        var now = DateTime.UtcNow;
        switch (dateRange)
        {
            case "today":
                return (now.Date, null);
            case "1day":
                return (now.AddDays(-1), null);
            case "2days":
                return (now.AddDays(-2), null);
            case "week":
                return (now.AddDays(-7), null);
            case "month":
                return (now.AddMonths(-1), null);
            case "custom":
                DateTime? from = DateTime.TryParse(dateFrom, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var df)
                    ? df.Date
                    : null;
                DateTime? to = DateTime.TryParse(dateTo, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
                    ? dt.Date.AddDays(1).AddTicks(-1)
                    : null;
                return (from, to);
            default:
                return (null, null);
        }
    }

    private static readonly (string Header, Func<TrackedTicket, object?> Value)[] ExportColumns =
    {
        ("Ticket #", t => t.TicketNumber),
        ("Subject", t => t.Subject),
        ("Reference No.", t => t.ReferenceNumber),
        ("Subscription ID", t => t.SubscriptionId),
        ("Account ID", t => t.AccountId),
        ("Plan ID", t => t.PlanId),
        ("Product", t => t.Product),
        ("Quantity", t => t.Quantity),
        ("Company", t => t.CompanyName),
        ("Customer Name", t => t.CustomerName),
        ("Customer Email", t => t.CustomerEmail),
        ("Customer Phone", t => t.CustomerPhone),
        ("Status", t => t.CurrentStatus.ToString()),
        ("Tracked At (UTC)", t => t.TrackedAt),
        ("Ticket Created (UTC)", t => t.TicketCreatedDate),
        ("Callback Sent", t => t.CallbackSent ? "Yes" : "No"),
        ("Callback Action", t => t.CallbackAction),
        ("Callback Sent At (UTC)", t => t.CallbackSentAt),
        ("Callback Response Code", t => t.CallbackResponseCode),
        ("Callback Response", t => t.CallbackResponseDescription),
        ("Reason Sent", t => !string.IsNullOrWhiteSpace(t.CallbackReason) ? t.CallbackReason : t.RejectionReason),
        ("Validation Errors", t => t.ValidationErrors != null && t.ValidationErrors.Count > 0 ? string.Join("; ", t.ValidationErrors) : null),
    };

    [HttpGet]
    public async Task<IActionResult> Export(string format = "csv", string? status = null, string? callback = null, string? search = null,
        string? dateRange = null, string? dateFrom = null, string? dateTo = null)
    {
        var filter = BuildTicketFilter(status, callback, search, dateRange, dateFrom, dateTo);
        var tickets = await _trackedCollection
            .Find(filter)
            .SortByDescending(t => t.TrackedAt)
            .ToListAsync();

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

        if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase)
            || string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = BuildExcel(tickets);
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"tickets_{timestamp}.xlsx");
        }

        var csv = BuildCsv(tickets);
        // UTF-8 BOM so Excel opens accented characters correctly.
        var csvBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return File(csvBytes, "text/csv", $"tickets_{timestamp}.csv");
    }

    private static string BuildCsv(List<TrackedTicket> tickets)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", ExportColumns.Select(c => CsvEscape(c.Header))));

        foreach (var ticket in tickets)
        {
            sb.AppendLine(string.Join(",", ExportColumns.Select(c => CsvEscape(FormatValue(c.Value(ticket))))));
        }

        return sb.ToString();
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "",
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private static byte[] BuildExcel(List<TrackedTicket> tickets)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Tickets");

        for (int col = 0; col < ExportColumns.Length; col++)
        {
            ws.Cell(1, col + 1).Value = ExportColumns[col].Header;
        }

        var headerRange = ws.Range(1, 1, 1, ExportColumns.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1d6fdb");
        headerRange.Style.Font.FontColor = XLColor.White;

        for (int row = 0; row < tickets.Count; row++)
        {
            for (int col = 0; col < ExportColumns.Length; col++)
            {
                var value = ExportColumns[col].Value(tickets[row]);
                var cell = ws.Cell(row + 2, col + 1);
                if (value is DateTime dt)
                {
                    cell.Value = dt;
                    cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                }
                else if (value is int i)
                {
                    cell.Value = i;
                }
                else
                {
                    cell.Value = value?.ToString() ?? "";
                }
            }
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
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

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> EditTicket(string id)
    {
        var ticket = await _trackedCollection.Find(t => t.Id == id).FirstOrDefaultAsync();
        if (ticket == null)
            return NotFound();

        return View(ticket);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditTicket(
        string id, string? subject, string? referenceNumber, string? subscriptionId,
        string? accountId, string? planId, string? product, int? quantity,
        string? companyName, string? customerName, string? customerEmail, string? customerPhone)
    {
        var ticket = await _trackedCollection.Find(t => t.Id == id).FirstOrDefaultAsync();
        if (ticket == null)
        {
            TempData["Error"] = "Ticket not found";
            return RedirectToAction("Monitor");
        }

        static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

        var update = Builders<TrackedTicket>.Update
            .Set(t => t.ReferenceNumber, Clean(referenceNumber))
            .Set(t => t.SubscriptionId, Clean(subscriptionId))
            .Set(t => t.AccountId, Clean(accountId))
            .Set(t => t.PlanId, Clean(planId))
            .Set(t => t.Product, Clean(product))
            .Set(t => t.Quantity, quantity)
            .Set(t => t.CompanyName, Clean(companyName))
            .Set(t => t.CustomerName, Clean(customerName))
            .Set(t => t.CustomerEmail, Clean(customerEmail))
            .Set(t => t.CustomerPhone, Clean(customerPhone));

        // Subject is required on the model — only overwrite when a value is supplied.
        if (!string.IsNullOrWhiteSpace(subject))
            update = update.Set(t => t.Subject, subject.Trim());

        await _trackedCollection.UpdateOneAsync(t => t.Id == id, update);

        _logger.LogInformation("Ticket {TicketNumber} edited by {User}", ticket.TicketNumber, User.Identity?.Name);
        TempData["Success"] = $"Ticket {ticket.TicketNumber} updated successfully";
        return RedirectToAction("EditTicket", new { id });
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
                ServiceAttribute = new List<ServiceAttribute>()
            };

            var response = await etisalatClient.SendProvisioningStatusAsync(request);

            var updateDef = Builders<TrackedTicket>.Update
                .Set(t => t.CallbackSent, true)
                .Set(t => t.CallbackSentAt, DateTime.UtcNow)
                .Set(t => t.CallbackAction, action)
                .Set(t => t.CallbackReason, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim())
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
                    ServiceAttribute = new List<ServiceAttribute>()
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
