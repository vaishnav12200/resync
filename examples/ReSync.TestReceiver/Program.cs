var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPost("/api/receive", (HttpRequest request, ILogger<Program> logger) =>
{
    logger.LogInformation(
        "Received request with idempotency key {IdempotencyKey}.",
        request.Headers["Idempotency-Key"].ToString());

    return Results.Ok(new { received = true });
});

app.Run();
