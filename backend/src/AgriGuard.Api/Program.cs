using AgriGuard.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Connection string comes from user-secrets locally and environment variables in the cloud —
// never from a committed appsettings file.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Connection string 'Default' is not configured. Locally run: " +
        "dotnet user-secrets set \"ConnectionStrings:Default\" \"<connection string>\" --project backend/src/AgriGuard.Api");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

// Exposes Program to WebApplicationFactory in the integration tests.
public partial class Program;
