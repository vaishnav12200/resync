using Microsoft.EntityFrameworkCore;
using ReSync.Agent.Delivery;
using ReSync.Storage;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddDbContext<ReSyncDbContext>(o =>
    o.UseSqlite("Data Source=resync.db"));
builder.Services.Configure<DeliveryWorkerOptions>(
    builder.Configuration.GetSection(DeliveryWorkerOptions.SectionName));
builder.Services.AddHttpClient();
builder.Services.AddHostedService<DeliveryWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ReSyncDbContext>();
    dbContext.Database.EnsureCreated();
}

app.MapControllers();
app.Run();
