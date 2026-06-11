using System.Text.Json.Serialization;

namespace EtisalatSaasCallback.Models;

public class IsvProvisioningStatusResponseWrapper
{
    [JsonPropertyName("isvProvisioningStatusResponse")]
    public IsvProvisioningStatusResponse IsvProvisioningStatusResponse { get; set; } = null!;
}

public class IsvProvisioningStatusResponse
{
    /// <summary>
    /// Same referenceNumber from the request
    /// </summary>
    [JsonPropertyName("referenceNumber")]
    public string ReferenceNumber { get; set; } = null!;

    /// <summary>
    /// Response code: 0 for success, non-zero for error/warning
    /// </summary>
    [JsonPropertyName("responseCode")]
    public string ResponseCode { get; set; } = null!;

    /// <summary>
    /// Status: 0 for success/warning, 1 for error
    /// </summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }

    /// <summary>
    /// Description of the response or error
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Optional response attributes for future use
    /// </summary>
    [JsonPropertyName("responseAttributes")]
    public List<ResponseAttribute>? ResponseAttributes { get; set; }
}

public class ResponseAttribute
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("value")]
    public string Value { get; set; } = null!;
}

/// <summary>
/// Error codes as per Etisalat specification
/// </summary>
public static class ErrorCodes
{
    public const string Success = "0";
    public const string AuthenticationFailed = "1";
    public const string AuthorizationFailed = "2";
    public const string InputValidationError = "3";
    public const string ReferenceNumberNotFound = "4";
    public const string TenantIdNotFound = "5";
    public const string SubscriptionIdNotFound = "6";
    public const string InvalidAction = "7";
    public const string OriginatorNotWhitelisted = "8";
    public const string InternalError = "99";
    public const string AccountSuspendedCannotAccept = "100";
    public const string AccountCeasedNoCallback = "101";

    public static string GetDescription(string code) => code switch
    {
        Success => "Successful",
        AuthenticationFailed => "Authentication Failed",
        AuthorizationFailed => "Authorization Failed",
        InputValidationError => "Input validation error",
        ReferenceNumberNotFound => "Reference Number Not Found",
        TenantIdNotFound => "Tenant ID not found",
        SubscriptionIdNotFound => "Subscription ID not found",
        InvalidAction => "Invalid Action",
        OriginatorNotWhitelisted => "Originator not whitelisted",
        InternalError => "Internal Error",
        AccountSuspendedCannotAccept => "Account is in Suspended state hence ACCEPT call back cannot be accepted at this time",
        AccountCeasedNoCallback => "Account is in Ceased state hence any call back cannot be accepted",
        _ => "Unknown error"
    };
}
