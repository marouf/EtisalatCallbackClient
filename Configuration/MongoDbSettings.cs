namespace EtisalatSaasCallback.Configuration;

public class MongoDbSettings
{
    public const string SectionName = "MongoDbSettings";

    /// <summary>
    /// MongoDB connection string
    /// </summary>
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";

    /// <summary>
    /// Database name
    /// </summary>
    public string DatabaseName { get; set; } = "ServiceMeTest";

    /// <summary>
    /// Collection name for provisioning records
    /// </summary>
    public string ProvisioningCollection { get; set; } = "provisioning_records";

    /// <summary>
    /// Collection name for subscription states
    /// </summary>
    public string SubscriptionCollection { get; set; } = "subscription_states";
}
