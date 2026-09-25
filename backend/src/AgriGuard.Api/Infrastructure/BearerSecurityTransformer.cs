using AgriGuard.Api.Authentication;
using AgriGuard.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AgriGuard.Api.Infrastructure;

/// <summary>
/// Adds the JWT "Authorize" button to Swagger UI and marks only endpoints that actually require
/// authorization with a padlock, so anonymous endpoints like login don't look protected. The
/// /internal/* endpoints are marked with the agent key instead, since a JWT does not open them.
/// </summary>
public sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    private const string SchemeId = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste the access token returned by POST /api/auth/login (without the 'Bearer ' prefix)."
        };
        document.Components.SecuritySchemes[AgentKeyDefaults.Scheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = AgentKeyDefaults.HeaderName,
            Description = "The agent service's shared key (AgentService:ApiKey). Opens /internal/* only."
        };
        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var authorize = metadata.OfType<IAuthorizeData>().ToList();
        if (authorize.Count == 0 || metadata.OfType<IAllowAnonymous>().Any())
            return Task.CompletedTask;

        var scheme = authorize.Any(a => a.Policy == AuthPolicies.AgentService) ? AgentKeyDefaults.Scheme : SchemeId;

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(scheme, context.Document)] = []
        });
        return Task.CompletedTask;
    }
}
