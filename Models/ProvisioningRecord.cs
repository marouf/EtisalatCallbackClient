using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EtisalatSaasCallback.Models;

/// <summary>
/// MongoDB document for storing provisioning callback records
/// </summary>
public class ProvisioningRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("referenceNumber")]
    public string ReferenceNumber { get; set; } = null!;

    [BsonElement("subscriptionId")]
    public string SubscriptionId { get; set; } = null!;

    [BsonElement("billingDate")]
    public string BillingDate { get; set; } = null!;

    [BsonElement("action")]
    public string Action { get; set; } = null!;

    [BsonElement("status")]
    public ProvisioningStatus Status { get; set; }

    [BsonElement("serviceAttributes")]
    public List<ServiceAttribute>? ServiceAttributes { get; set; }

    [BsonElement("callbackDirection")]
    public CallbackDirection Direction { get; set; }

    [BsonElement("responseCode")]
    public string? ResponseCode { get; set; }

    [BsonElement("responseDescription")]
    public string? ResponseDescription { get; set; }

    [BsonElement("sourceIp")]
    public string? SourceIp { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("retryCount")]
    public int RetryCount { get; set; }

    [BsonElement("lastError")]
    public string? LastError { get; set; }

    [BsonElement("requestBody")]
    public string? RequestBody { get; set; }

    [BsonElement("responseBody")]
    public string? ResponseBody { get; set; }

    [BsonElement("requestUrl")]
    public string? RequestUrl { get; set; }
}

public enum ProvisioningStatus
{
    Pending = 0,
    Success = 1,
    Failed = 2,
    Expired = 3
}

public enum CallbackDirection
{
    Inbound = 0,  // Received from Etisalat/ISV
    Outbound = 1  // Sent to Etisalat
}

/// <summary>
/// MongoDB document for storing subscription state
/// </summary>
public class SubscriptionState
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("subscriptionId")]
    public string SubscriptionId { get; set; } = null!;

    [BsonElement("tenantId")]
    public string? TenantId { get; set; }

    [BsonElement("state")]
    public SubscriptionStateType State { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("activatedAt")]
    public DateTime? ActivatedAt { get; set; }

    [BsonElement("suspendedAt")]
    public DateTime? SuspendedAt { get; set; }

    [BsonElement("ceasedAt")]
    public DateTime? CeasedAt { get; set; }
}

public enum SubscriptionStateType
{
    InProgress = 0,
    Active = 1,
    Suspended = 2,
    Ceased = 3,
    Cancelled = 4,
    Expired = 5,
    Rejected = 6
}
