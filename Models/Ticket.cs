using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EtisalatSaasCallback.Models;

public class Ticket
{
    [BsonId]
    public BsonValue Id { get; set; } = null!;

    [BsonElement("CreatedDate")]
    public DateTime CreatedDate { get; set; }

    [BsonElement("ModifiedDate")]
    [BsonIgnoreIfNull]
    public DateTime? ModifiedDate { get; set; }

    [BsonElement("CreatedBy")]
    [BsonIgnoreIfNull]
    public BsonValue? CreatedBy { get; set; }

    [BsonElement("ModifiedBy")]
    [BsonIgnoreIfNull]
    public BsonValue? ModifiedBy { get; set; }

    [BsonElement("IsArchived")]
    public bool IsArchived { get; set; }

    [BsonElement("All")]
    public string? All { get; set; }

    [BsonElement("TicketNumber")]
    public string TicketNumber { get; set; } = null!;

    [BsonElement("Subject")]
    public string Subject { get; set; } = null!;

    [BsonElement("Body")]
    public string Body { get; set; } = null!;

    [BsonElement("TicketCategoryId")]
    [BsonIgnoreIfNull]
    public BsonValue? TicketCategoryId { get; set; }

    [BsonElement("TicketSubCategoryId")]
    [BsonIgnoreIfNull]
    public BsonValue? TicketSubCategoryId { get; set; }

    [BsonElement("SubmitterUserId")]
    [BsonIgnoreIfNull]
    public BsonValue? SubmitterUserId { get; set; }

    [BsonElement("SubmitterUserFullName")]
    public string? SubmitterUserFullName { get; set; }

    [BsonElement("RaisingForUserId")]
    [BsonIgnoreIfNull]
    public BsonValue? RaisingForUserId { get; set; }

    [BsonElement("RaisingForCompany")]
    public string? RaisingForCompany { get; set; }

    [BsonElement("RaisingForCompanyId")]
    [BsonIgnoreIfNull]
    public BsonValue? RaisingForCompanyId { get; set; }

    [BsonElement("AssignedTeamId")]
    [BsonIgnoreIfNull]
    public BsonValue? AssignedTeamId { get; set; }

    [BsonElement("CompanyId")]
    [BsonIgnoreIfNull]
    public BsonValue? CompanyId { get; set; }

    [BsonElement("AssignedTeamName")]
    public string? AssignedTeamName { get; set; }

    [BsonElement("Status")]
    public TicketStatus Status { get; set; }

    [BsonElement("Severity")]
    public int Severity { get; set; }

    [BsonElement("ExistingIssue")]
    public bool ExistingIssue { get; set; }

    [BsonElement("RaisingFor")]
    public int RaisingFor { get; set; }

    [BsonElement("NewRequest")]
    public bool NewRequest { get; set; }

    [BsonElement("FunctionType")]
    public int FunctionType { get; set; }

    [BsonElement("ImportanceType")]
    public int ImportanceType { get; set; }

    [BsonElement("Comments")]
    public List<TicketComment>? Comments { get; set; }

    [BsonElement("Files")]
    public List<BsonDocument>? Files { get; set; }

    [BsonExtraElements]
    public BsonDocument? ExtraElements { get; set; }
}

public class TicketComment
{
    [BsonElement("Text")]
    public string? Text { get; set; }

    [BsonElement("UserId")]
    [BsonIgnoreIfNull]
    public BsonValue? UserId { get; set; }

    [BsonElement("TicketId")]
    [BsonIgnoreIfNull]
    public BsonValue? TicketId { get; set; }

    [BsonElement("UserFullName")]
    public string? UserFullName { get; set; }

    [BsonElement("Type")]
    public int Type { get; set; }

    [BsonElement("DateOfSubmission")]
    public DateTime DateOfSubmission { get; set; }
}

public enum TicketStatus
{
    Open = 0,
    InProgress = 1,
    WaitingForUserInput = 2,
    Resolved = 3,
    Closed = 4,
    Reassigned = 5,
    Draft = 6,
    WaitingForVendor = 7,
    WaitingForApproval = 8,
    Approved = 9,
    Rejected = 10
}

public enum TicketSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}
