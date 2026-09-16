using AgriGuard.Api.Infrastructure;
using AgriGuard.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Events;

// Minimal console logger so failures during startup (bad config, unreachable DB) are still visible.
// Replaced by the fully configured logger once the host is built.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Sinks, levels and output format come from the "Serilog" config section:
    // readable text locally (appsettings.Development.json), compact JSON in the cloud (appsettings.json).
    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // Connection string comes from user-secrets locally and environment variables in the cloud —
    // never from a committed appsettings file.
    var connectionString = builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Connection string 'Default' is not configured. Locally run: " +
            "dotnet user-secrets set \"ConnectionStrings:Default\" \"<connection string>\" --project backend/src/AgriGuard.Api");

    builder.Services.AddInfrastructure(connectionString);
    builder.Services.AddControllers();

    // Every error response — thrown exceptions, empty 404/405s, model-binding 400s — is RFC 7807,
    // stamped with IDs a user can quote and we can find in the logs.
    builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
        ctx.ProblemDetails.Extensions["correlationId"] = CorrelationIdMiddleware.Get(ctx.HttpContext);
    });
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    builder.Services.AddOpenApi(options => options
        .AddDocumentTransformer<BearerSecurityTransformer>()
        .AddOperationTransformer<BearerSecurityTransformer>());

    // Browser clients (React) must be listed explicitly; mobile clients are not subject to CORS.
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));

    var app = builder.Build();

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging(options =>
    {
        // Health probes from Render/Docker would otherwise flood the logs.
        options.GetLevel = (http, _, ex) =>
            ex is not null || http.Response.StatusCode >= 500 ? LogEventLevel.Error
            : http.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
            : LogEventLevel.Information;
    });
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    // Kept on outside Development on purpose: the assignment submits a live /swagger URL.
    if (app.Configuration.GetValue("Swagger:Enabled", true))
    {
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "AgriGuard API v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "AgriGuard API";
        });
    }

    // Locally the Android emulator (10.0.2.2) and Vite call plain http://localhost:5000.
    // Behind Render's TLS proxy this also needs forwarded headers — configured with deployment.
    if (!app.Environment.IsDevelopment())
        app.UseHttpsRedirection();
    app.UseCors();
    app.UseAuthorization();

    // /health/live: the process is up (no dependencies). /health: the API can reach PostgreSQL.
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = HealthResponseWriter.WriteAsync
    });
    app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync });
    app.MapControllers();

    app.Run();
}
// HostAbortedException is how `dotnet ef` and WebApplicationFactory stop the host on purpose.
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "AgriGuard API terminated during startup");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Exposes Program to WebApplicationFactory in the integration tests.
public partial class Program;
