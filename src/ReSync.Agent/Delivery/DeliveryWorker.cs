using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReSync.Core;
using ReSync.Storage;
using System.Net;
using System.Text;

namespace ReSync.Agent.Delivery;

public sealed class DeliveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _httpClient;
    private readonly DeliveryWorkerOptions _options;
    private readonly ILogger<DeliveryWorker> _logger;

    public DeliveryWorker(
        IServiceScopeFactory scopeFactory,
        HttpClient httpClient,
        IOptions<DeliveryWorkerOptions> options,
        ILogger<DeliveryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedJobsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedJob = await ProcessNextJobAsync(stoppingToken);

                if (!processedJob)
                {
                    await Task.Delay(GetPollInterval(), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "An unexpected error occurred while processing delivery jobs.");
                await Task.Delay(GetPollInterval(), stoppingToken);
            }
        }
    }

    public async Task<bool> ProcessNextJobAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReSyncDbContext>();
        var now = DateTime.UtcNow;
        var job = await dbContext.Jobs
            .Where(item =>
                (item.Status == JobStatus.Pending || item.Status == JobStatus.RetryScheduled) &&
                item.NextAttemptAt <= now)
            .OrderBy(item => item.NextAttemptAt)
            .ThenBy(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return false;
        }

        job.Status = JobStatus.Processing;
        job.AttemptCount++;
        await dbContext.SaveChangesAsync(cancellationToken);

        await DeliverAsync(dbContext, job, cancellationToken);
        return true;
    }

    private async Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReSyncDbContext>();
        var interruptedJobs = await dbContext.Jobs
            .Where(job => job.Status == JobStatus.Processing)
            .ToListAsync(cancellationToken);

        if (interruptedJobs.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var job in interruptedJobs)
        {
            job.Status = JobStatus.RetryScheduled;
            job.NextAttemptAt = now;
            job.LastError = "Delivery was interrupted before completion.";
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task DeliverAsync(
        ReSyncDbContext dbContext,
        DeliveryJob job,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateRequest(job);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                job.Status = JobStatus.Delivered;
                job.DeliveredAt = DateTime.UtcNow;
                job.LastError = null;
            }
            else if (IsTransient(response.StatusCode))
            {
                ScheduleRetry(job, $"Destination returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }
            else
            {
                job.Status = JobStatus.Failed;
                job.LastError = $"Destination returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
            }
        }
        catch (HttpRequestException exception)
        {
            ScheduleRetry(job, exception.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ScheduleRetry(job, "The destination request timed out.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(DeliveryJob job)
    {
        var request = new HttpRequestMessage(new HttpMethod(job.Method), job.DestinationUrl);
        request.Headers.Add("Idempotency-Key", $"{job.ApplicationId}:{job.RequestId}");

        if (!string.IsNullOrEmpty(job.Payload))
        {
            request.Content = new StringContent(job.Payload, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private void ScheduleRetry(DeliveryJob job, string error)
    {
        job.LastError = error;

        if (job.AttemptCount >= Math.Max(1, _options.MaxAttempts))
        {
            job.Status = JobStatus.Failed;
            return;
        }

        job.Status = JobStatus.RetryScheduled;
        job.NextAttemptAt = DateTime.UtcNow.Add(GetRetryDelay(job.AttemptCount));
    }

    private TimeSpan GetRetryDelay(int attemptCount)
    {
        var baseDelaySeconds = Math.Max(1, _options.BaseRetryDelaySeconds);
        var maximumDelaySeconds = Math.Max(baseDelaySeconds, _options.MaxRetryDelaySeconds);
        var exponent = Math.Min(attemptCount - 1, 10);
        var delaySeconds = Math.Min(maximumDelaySeconds, baseDelaySeconds * Math.Pow(2, exponent));
        var jitterSeconds = Random.Shared.NextDouble() * delaySeconds * 0.25;

        return TimeSpan.FromSeconds(delaySeconds + jitterSeconds);
    }

    private TimeSpan GetPollInterval() => TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        (int)statusCode == 429 ||
        (int)statusCode >= 500;
}
