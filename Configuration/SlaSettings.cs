namespace EtisalatSaasCallback.Configuration;

public class SlaSettings
{
    public const string SectionName = "Sla";

    public bool Enabled { get; set; } = true;
    public int DefaultSlaDays { get; set; } = 30;
    public int WarningThresholdDays { get; set; } = 25;
    public List<SlaRule> Rules { get; set; } = new();
}

public class SlaRule
{
    public string Name { get; set; } = null!;
    public int SlaDays { get; set; }
    public int WarningDays { get; set; }
    public string AppliesTo { get; set; } = "default";
}
