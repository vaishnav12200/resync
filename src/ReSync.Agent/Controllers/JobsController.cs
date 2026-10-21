using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReSync.Agent.Contracts;
using ReSync.Core;
using ReSync.Storage;

namespace ReSync.Agent.Controllers;

[ApiController]
[Route("api/jobs")]
public sealed class JobsController : ControllerBase
{
    private readonly ReSyncDbContext _dbContext;

    public JobsController(ReSyncDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeliveryJob>>> GetAll(CancellationToken cancellationToken)
    {
        var jobs = await _dbContext.Jobs
            .AsNoTracking()
            .OrderByDescending(job => job.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(jobs);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DeliveryJob>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var job = await _dbContext.Jobs
            .AsNoTracking()
            .SingleOrDefaultAsync(job => job.Id == id, cancellationToken);

        return job is null ? NotFound() : Ok(job);
    }

    [HttpPost]
    public async Task<ActionResult<DeliveryJob>> Queue(
        QueueJobRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryValidateRequest(request, out var destinationUri, out var validationError))
        {
            return BadRequest(new ValidationProblemDetails(validationError));
        }

        var existingJob = await _dbContext.Jobs
            .SingleOrDefaultAsync(
                job => job.ApplicationId == request.ApplicationId && job.RequestId == request.RequestId,
                cancellationToken);

        if (existingJob is not null)
        {
            return AcceptedAtAction(nameof(GetById), new { existingJob.Id }, existingJob);
        }

        var now = DateTime.UtcNow;
        var job = new DeliveryJob
        {
            ApplicationId = request.ApplicationId!,
            RequestId = request.RequestId!,
            DestinationUrl = destinationUri.AbsoluteUri,
            Method = request.Method!.ToUpperInvariant(),
            Payload = request.Payload ?? string.Empty,
            Status = JobStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now
        };

        _dbContext.Jobs.Add(job);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var concurrentJob = await _dbContext.Jobs
                .SingleOrDefaultAsync(
                    existing => existing.ApplicationId == request.ApplicationId && existing.RequestId == request.RequestId,
                    cancellationToken);

            if (concurrentJob is not null)
            {
                return AcceptedAtAction(nameof(GetById), new { concurrentJob.Id }, concurrentJob);
            }

            throw;
        }

        return AcceptedAtAction(nameof(GetById), new { job.Id }, job);
    }

    private static bool TryValidateRequest(
        QueueJobRequest request,
        out Uri destinationUri,
        out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.ApplicationId))
        {
            errors[nameof(request.ApplicationId)] = ["Application ID is required."];
        }

        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            errors[nameof(request.RequestId)] = ["Request ID is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Method))
        {
            errors[nameof(request.Method)] = ["HTTP method is required."];
        }

        if (!Uri.TryCreate(request.DestinationUrl, UriKind.Absolute, out destinationUri!) ||
            (destinationUri.Scheme != Uri.UriSchemeHttp && destinationUri.Scheme != Uri.UriSchemeHttps))
        {
            errors[nameof(request.DestinationUrl)] = ["Destination URL must be an absolute HTTP or HTTPS URL."];
        }

        return errors.Count == 0;
    }
}
