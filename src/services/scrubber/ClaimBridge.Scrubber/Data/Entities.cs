namespace ClaimBridge.Scrubber.Data;

public class IcdCode
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Billable { get; set; }
}

public class ProcedureCode
{
    public string Code { get; set; } = "";
    // our own short wording, not the AMA descriptors
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal TypicalCharge { get; set; }
}

public class Modifier
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string AllowedCategories { get; set; } = "";
}

public class MedicalNecessityEntry
{
    public int Id { get; set; }
    public string ProcedureCode { get; set; } = "";
    public string DxPrefix { get; set; } = "";
}

public class PayerSetting
{
    public string PayerId { get; set; } = "";
    public string Name { get; set; } = "";
    public int TimelyFilingDays { get; set; }
}
