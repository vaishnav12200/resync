using Microsoft.EntityFrameworkCore;
using ReSync.Core;
using ReSync.Storage;

namespace ReSync.Tests;

public sealed class ReSyncDbContextTests
{
    [Fact]
    public async Task Job_persists_when_a_new_context_opens_the_database()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"resync-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ReSyncDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var jobId = Guid.NewGuid();

        try
        {
            await using (var firstContext = new ReSyncDbContext(options))
            {
                await firstContext.Database.EnsureCreatedAsync();
                firstContext.Jobs.Add(new DeliveryJob
                {
                    Id = jobId,
                    ApplicationId = "sample-app",
                    RequestId = "form-123",
                    DestinationUrl = "https://api.example.test/forms",
                    Payload = "{\"name\":\"Ada\"}"
                });
                await firstContext.SaveChangesAsync();
            }

            await using (var secondContext = new ReSyncDbContext(options))
            {
                var job = await secondContext.Jobs.SingleAsync(item => item.Id == jobId);

                Assert.Equal("sample-app", job.ApplicationId);
                Assert.Equal("form-123", job.RequestId);
                Assert.Equal(JobStatus.Pending, job.Status);
            }
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Application_and_request_id_must_be_unique()
    {
        var options = new DbContextOptionsBuilder<ReSyncDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        await using var context = new ReSyncDbContext(options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();

        context.Jobs.Add(CreateJob());
        await context.SaveChangesAsync();

        context.Jobs.Add(CreateJob());

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static DeliveryJob CreateJob() => new()
    {
        ApplicationId = "sample-app",
        RequestId = "request-123",
        DestinationUrl = "https://api.example.test/forms",
        Payload = "{}"
    };
}
