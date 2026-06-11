using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EtisalatSaasCallback.Models;

public class TrackedTicket
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("ticketId")]
    public string TicketId { get; set; } = null!;

    [BsonElement("ticketNumber")]
    public string TicketNumber { get; set; } = null!;

    [BsonElement("subject")]
    public string Subject { get; set; } = null!;

    [BsonElement("referenceNumber")]
    public string? ReferenceNumber { get; set; }

    [BsonElement("subscriptionId")]
    public string? SubscriptionId { get; set; }

    [BsonElement("accountId")]
    public string? AccountId { get; set; }

    [BsonElement("planId")]
    public string? PlanId { get; set; }

    [BsonElement("companyName")]
    public string? CompanyName { get; set; }

    [BsonElement("customerName")]
    public string? CustomerName { get; set; }

    [BsonElement("customerEmail")]
    public string? CustomerEmail { get; set; }

    [BsonElement("customerPhone")]
    public string? CustomerPhone { get; set; }

    [BsonElement("product")]
    public string? Product { get; set; }

    [BsonElement("quantity")]
    public int? Quantity { get; set; }

    [BsonElement("currentStatus")]
    public TicketStatus CurrentStatus { get; set; }

    [BsonElement("previousStatus")]
    public TicketStatus? PreviousStatus { get; set; }

    [BsonElement("trackedAt")]
    public DateTime TrackedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("lastCheckedAt")]
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("statusChangedAt")]
    public DateTime? StatusChangedAt { get; set; }

    [BsonElement("callbackSent")]
    public bool CallbackSent { get; set; }

    [BsonElement("callbackSentAt")]
    public DateTime? CallbackSentAt { get; set; }

    [BsonElement("callbackAction")]
    public string? CallbackAction { get; set; }

    [BsonElement("callbackResponseCode")]
    public string? CallbackResponseCode { get; set; }

    [BsonElement("callbackResponseDescription")]
    public string? CallbackResponseDescription { get; set; }

    [BsonElement("callbackReason")]
    public string? CallbackReason { get; set; }

    [BsonElement("ticketCreatedDate")]
    public DateTime TicketCreatedDate { get; set; }

    [BsonElement("ticketModifiedDate")]
    public DateTime? TicketModifiedDate { get; set; }

    [BsonElement("isValidPayload")]
    public bool IsValidPayload { get; set; } = true;

    [BsonElement("validationErrors")]
    public List<string>? ValidationErrors { get; set; }

    [BsonElement("rejectionReason")]
    public string? RejectionReason { get; set; }
}
