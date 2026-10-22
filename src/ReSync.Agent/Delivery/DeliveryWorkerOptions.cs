namespace ReSync.Agent.Delivery;

public sealed class DeliveryWorkerOptions
{
    public const string SectionName = "Delivery";

    public int PollIntervalSeconds { get; init; } = 1;

    public int MaxAttempts { get; init; } = 5;

    public int BaseRetryDelaySeconds { get; init; } = 2;

    public int MaxRetryDelaySeconds { get; init; } = 60;
}
