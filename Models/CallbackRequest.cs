using System.ComponentModel.DataAnnotations;

namespace EtisalatSaasCallback.Models;

public class ManualCallbackRequest
{
    [Required]
    public string TicketId { get; set; } = null!;

    [Required]
    [RegularExpression("^(ACCEPTED|REJECTED|EXPIRED)$", ErrorMessage = "Action must be ACCEPTED, REJECTED, or EXPIRED")]
    public string Action { get; set; } = null!;

    public string? Reason { get; set; }
}

public class BulkCallbackRequest
{
    [Required]
    [MinLength(1)]
    public List<string> TicketIds { get; set; } = new();

    [Required]
    [RegularExpression("^(ACCEPTED|REJECTED|EXPIRED)$", ErrorMessage = "Action must be ACCEPTED, REJECTED, or EXPIRED")]
    public string Action { get; set; } = null!;

    public string? Reason { get; set; }
}

public class CallbackResult
{
    public string TicketId { get; set; } = null!;
    public string TicketNumber { get; set; } = null!;
    public bool Success { get; set; }
    public string? ResponseCode { get; set; }
    public string? ResponseDescription { get; set; }
    public string? Error { get; set; }
}
