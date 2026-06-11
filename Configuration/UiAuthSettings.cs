namespace EtisalatSaasCallback.Configuration;

public class UiAuthSettings
{
    public const string SectionName = "UiAuth";

    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "admin123";
    public int SessionTimeoutMinutes { get; set; } = 60;
}
