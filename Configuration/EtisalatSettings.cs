namespace EtisalatSaasCallback.Configuration;

public class EtisalatSettings
{
    public const string SectionName = "Etisalat";

    /// <summary>
    /// UAT: https://contentapi-s.etisalat.ae/rest/xaas/v1
    /// Production: Provided after successful UAT testing
    /// </summary>
    public string BaseUrl { get; set; } = "https://contentapi-s.etisalat.ae/rest/xaas/v1/";

    /// <summary>
    /// ISV Provisioning Status endpoint path
    /// Full URL: {BaseUrl}{IsvProvisioningStatusEndpoint}
    /// </summary>
    public string IsvProvisioningStatusEndpoint { get; set; } = "isvProvisioningStatus";

    /// <summary>
    /// Username provided by Etisalat for Basic Auth
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Password provided by Etisalat for Basic Auth
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Timeout in seconds for API calls
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Number of retry attempts for transient failures
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// SLA in days (default 30 as per specification)
    /// </summary>
    public int SlaDays { get; set; } = 30;

    /// <summary>
    /// Service attributes sent with every callback. Empty by default — configure later in appsettings.
    /// </summary>
    public List<ServiceAttributeSetting> ServiceAttributes { get; set; } = new();
}

public class ServiceAttributeSetting
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

public class IsvSettings
{
    public const string SectionName = "Isv";

    /// <summary>
    /// Whitelisted IP addresses allowed to call the callback API
    /// </summary>
    public List<string> WhitelistedIps { get; set; } = [];

    /// <summary>
    /// Username for Basic Auth validation
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Password for Basic Auth validation
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Enable IP whitelisting validation
    /// </summary>
    public bool EnableIpWhitelisting { get; set; } = true;
}
