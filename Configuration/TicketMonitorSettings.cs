namespace EtisalatSaasCallback.Configuration;

public class TicketMonitorSettings
{
    public const string SectionName = "TicketMonitor";

    public bool Enabled { get; set; } = true;

    public string TicketDbConnectionString { get; set; } = "mongodb://localhost:27017";

    public string TicketDbName { get; set; } = "ServiceMeTest";

    public string TicketCollectionName { get; set; } = "Ticket";

    public int PollingIntervalSeconds { get; set; } = 30;

    public string SubjectFilter { get; set; } = "Subscription";

    // Legacy single pair (still supported). Prefer the Categories list below.
    public string? TicketCategoryId { get; set; }

    public string? TicketSubCategoryId { get; set; }

    // List of category/sub-category pairs to monitor. A ticket matching ANY pair is tracked.
    public List<CategoryPair> Categories { get; set; } = new();
}

public class CategoryPair
{
    public string? TicketCategoryId { get; set; }

    public string? TicketSubCategoryId { get; set; }
}
