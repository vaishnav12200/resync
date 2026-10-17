namespace ReSync.Core;

public enum JobStatus
{
    Pending,
    Processing,
    RetryScheduled,
    Delivered,
    Failed
}
