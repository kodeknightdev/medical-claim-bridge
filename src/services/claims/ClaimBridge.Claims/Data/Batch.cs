namespace ClaimBridge.Claims.Data;

public enum BatchStatus { Uploaded, Processing, Completed, Failed }

public class Batch
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string FileName { get; set; } = "";
    public string Format { get; set; } = "csv";
    public string BlobContainer { get; set; } = "";
    public string BlobName { get; set; } = "";
    public BatchStatus Status { get; set; }
    public int TotalClaims { get; set; }
    public int Accepted { get; set; }
    public int Rejected { get; set; }
    public string? ErrorsJson { get; set; }
    public string UploadedBy { get; set; } = "";
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
