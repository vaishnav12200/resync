using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReSync.Core;
using ReSync.Storage;

namespace ReSync.Agent.Controllers;

public record SubmitJobRequest(
    string ApplicationId,
    string RequestId,
    string? Method,
    string DestinationUrl,
    JsonElement Body);

[ApiController]
[Route("api/jobs")]
public class JobsController : ControllerBase
{
    private readonly ReSyncDbContext _db;
    public JobsController(ReSyncDbContext db) => _db = db;

    [HttpPost]
    public async Task<IActionResult> Submit(SubmitJobRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ApplicationId) ||
            string.IsNullOrWhiteSpace(req.RequestId) ||
            !Uri.TryCreate(req.DestinationUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return BadRequest("applicationId, requestId and a valid destinationUrl are required.");

        var existing = await _db.Jobs.FirstOrDefaultAsync(j =>
            j.ApplicationId == req.ApplicationId && j.RequestId == req.RequestId);
        if (existing != null)
            return Ok(new { jobId = existing.Id, status = existing.Status.ToString(), message = "Duplicate request, already queued" });

        var job = new DeliveryJob
        {
            ApplicationId = req.ApplicationId,
            RequestId = req.RequestId,
            DestinationUrl = req.DestinationUrl,
            Method = req.Method ?? "POST",
            Payload = req.Body.GetRawText()
        };

        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();   // committed to SQLite before we reply

        return Accepted(new { jobId = job.Id, status = job.Status.ToString(), message = "Request safely queued" });
    }

    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await _db.Jobs.OrderByDescending(j => j.CreatedAt).Take(100).ToListAsync());

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var job = await _db.Jobs.FindAsync(id);
        return job == null ? NotFound() : Ok(job);
    }
}
