namespace ReSync.Core;

public class DeliveryJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ApplicationId { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string DestinationUrl { get; set; } = "";
    public string Method { get; set; } = "POST";
    public string Payload { get; set; } = "";
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAt { get; set; }
    public string? LastError { get; set; }
}
