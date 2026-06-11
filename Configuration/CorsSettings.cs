namespace EtisalatSaasCallback.Configuration;

public class CorsSettings
{
    public const string SectionName = "Cors";

    public bool Enabled { get; set; } = false;
    public string[] AllowedOrigins { get; set; } = ["*"];
    public string[] AllowedMethods { get; set; } = ["GET", "POST", "PUT", "DELETE", "OPTIONS"];
    public string[] AllowedHeaders { get; set; } = ["*"];
}
