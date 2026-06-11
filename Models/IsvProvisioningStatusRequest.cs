using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EtisalatSaasCallback.Models;

public class IsvProvisioningStatusRequestWrapper
{
    [JsonPropertyName("isvProvisioningStatusRequest")]
    [Required]
    public IsvProvisioningStatusRequest IsvProvisioningStatusRequest { get; set; } = null!;
}

public class IsvProvisioningStatusRequest
{
    /// <summary>
    /// Reference number from the fulfillment/provisioning request (max 40 chars)
    /// </summary>
    [JsonPropertyName("referenceNumber")]
    [Required]
    [StringLength(40)]
    public string ReferenceNumber { get; set; } = null!;

    /// <summary>
    /// Subscription ID returned by ISV at activation time (max 50 chars)
    /// </summary>
    [JsonPropertyName("subscriptionId")]
    [Required]
    [StringLength(50)]
    public string SubscriptionId { get; set; } = null!;

    /// <summary>
    /// Billing Start Date in format "YYYYMMDDHH24MMSS" (e.g., "20200129234950")
    /// </summary>
    [JsonPropertyName("billingDate")]
    [Required]
    [RegularExpression(@"^\d{14}$", ErrorMessage = "billingDate must be in format YYYYMMDDHH24MMSS")]
    public string BillingDate { get; set; } = null!;

    /// <summary>
    /// Action: ACCEPTED, REJECTED, or EXPIRED
    /// </summary>
    [JsonPropertyName("action")]
    [Required]
    [StringLength(20)]
    public string Action { get; set; } = null!;

    /// <summary>
    /// Optional service attributes for future use
    /// </summary>
    [JsonPropertyName("serviceAttribute")]
    public List<ServiceAttribute>? ServiceAttribute { get; set; }
}

public class ServiceAttribute
{
    [JsonPropertyName("name")]
    [StringLength(30)]
    public string Name { get; set; } = null!;

    [JsonPropertyName("value")]
    [StringLength(100)]
    public string Value { get; set; } = null!;
}

/// <summary>
/// Valid provisioning actions
/// </summary>
public static class ProvisioningAction
{
    public const string Accepted = "ACCEPTED";
    public const string Rejected = "REJECTED";
    public const string Expired = "EXPIRED";

    public static readonly string[] ValidActions = [Accepted, Rejected, Expired];

    public static bool IsValid(string action) =>
        ValidActions.Contains(action, StringComparer.OrdinalIgnoreCase);
}
