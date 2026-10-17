using Microsoft.EntityFrameworkCore;
using ReSync.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://127.0.0.1:5050");

builder.Services.AddControllers();
builder.Services.AddDbContext<ReSyncDbContext>(o =>
    o.UseSqlite("Data Source=resync.db"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ReSyncDbContext>().Database.EnsureCreated();
}

app.MapControllers();
app.Run();
