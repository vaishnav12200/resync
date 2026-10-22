using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ReSync.Agent.Delivery;
using ReSync.Core;
using ReSync.Storage;
using System.Net;

namespace ReSync.Tests;

public sealed class DeliveryWorkerTests
{
    [Fact]
    public async Task Successful_delivery_marks_job_delivered_and_sends_idempotency_key()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK);
        await using var fixture = await DeliveryWorkerFixture.CreateAsync(handler);

        var job = await fixture.AddJobAsync();

        var processed = await fixture.Worker.ProcessNextJobAsync();
        var updatedJob = await fixture.GetJobAsync(job.Id);

        Assert.True(processed);
        Assert.Equal(JobStatus.Delivered, updatedJob.Status);
        Assert.Equal(1, updatedJob.AttemptCount);
        Assert.NotNull(updatedJob.DeliveredAt);
        Assert.Equal("sample-app:request-123", handler.IdempotencyKey);
    }

    [Fact]
    public async Task Transient_failure_schedules_a_retry()
    {
        await using var fixture = await DeliveryWorkerFixture.CreateAsync(
            new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable));
        var job = await fixture.AddJobAsync();

        await fixture.Worker.ProcessNextJobAsync();
        var updatedJob = await fixture.GetJobAsync(job.Id);

        Assert.Equal(JobStatus.RetryScheduled, updatedJob.Status);
        Assert.Equal(1, updatedJob.AttemptCount);
        Assert.Contains("503", updatedJob.LastError);
        Assert.True(updatedJob.NextAttemptAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Permanent_failure_marks_job_failed()
    {
        await using var fixture = await DeliveryWorkerFixture.CreateAsync(
            new StubHttpMessageHandler(HttpStatusCode.BadRequest));
        var job = await fixture.AddJobAsync();

        await fixture.Worker.ProcessNextJobAsync();
        var updatedJob = await fixture.GetJobAsync(job.Id);

        Assert.Equal(JobStatus.Failed, updatedJob.Status);
        Assert.Equal(1, updatedJob.AttemptCount);
        Assert.Contains("400", updatedJob.LastError);
    }

    private sealed class DeliveryWorkerFixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly ServiceProvider _serviceProvider;

        private DeliveryWorkerFixture(string databasePath, ServiceProvider serviceProvider, DeliveryWorker worker)
        {
            _databasePath = databasePath;
            _serviceProvider = serviceProvider;
            Worker = worker;
        }

        public DeliveryWorker Worker { get; }

        public static async Task<DeliveryWorkerFixture> CreateAsync(HttpMessageHandler handler)
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"resync-worker-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            services.AddDbContext<ReSyncDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath}"));
            var serviceProvider = services.BuildServiceProvider();

            await using (var scope = serviceProvider.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ReSyncDbContext>();
                await dbContext.Database.EnsureCreatedAsync();
            }

            var worker = new DeliveryWorker(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                new HttpClient(handler),
                Options.Create(new DeliveryWorkerOptions
                {
                    BaseRetryDelaySeconds = 5,
                    MaxRetryDelaySeconds = 60
                }),
                NullLogger<DeliveryWorker>.Instance);

            return new DeliveryWorkerFixture(databasePath, serviceProvider, worker);
        }

        public async Task<DeliveryJob> AddJobAsync()
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ReSyncDbContext>();
            var job = new DeliveryJob
            {
                ApplicationId = "sample-app",
                RequestId = "request-123",
                DestinationUrl = "http://127.0.0.1:6060/api/receive",
                Payload = "{\"name\":\"Ada\"}"
            };

            dbContext.Jobs.Add(job);
            await dbContext.SaveChangesAsync();
            return job;
        }

        public async Task<DeliveryJob> GetJobAsync(Guid id)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ReSyncDbContext>();
            return await dbContext.Jobs.SingleAsync(job => job.Id == id);
        }

        public async ValueTask DisposeAsync()
        {
            await _serviceProvider.DisposeAsync();
            File.Delete(_databasePath);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StubHttpMessageHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        public string? IdempotencyKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            IdempotencyKey = request.Headers.GetValues("Idempotency-Key").Single();
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }
}
