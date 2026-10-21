namespace ReSync.Agent.Contracts;

public sealed class QueueJobRequest
{
    public string? ApplicationId { get; init; }

    public string? RequestId { get; init; }

    public string? DestinationUrl { get; init; }

    public string? Method { get; init; }

    public string? Payload { get; init; }
}
