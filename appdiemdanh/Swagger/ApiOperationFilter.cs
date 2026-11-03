using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AppDiemDanh.Swagger;

public sealed class ApiOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        var allowAnonymous = metadata.OfType<AllowAnonymousAttribute>().Any();
        var authorize = metadata.OfType<AuthorizeAttribute>().FirstOrDefault();
        var roles = string.IsNullOrWhiteSpace(authorize?.Roles) ? "Authenticated user" : authorize!.Roles;
        operation.Summary ??= context.ApiDescription.ActionDescriptor.RouteValues.TryGetValue("action", out var action)
            ? action
            : $"{context.ApiDescription.HttpMethod} {context.ApiDescription.RelativePath}";

        operation.Description = $"Roles: {(allowAnonymous ? "Anonymous" : roles)}. " + (operation.Description ?? string.Empty);
        operation.Description ??= $"API {context.ApiDescription.HttpMethod} {context.ApiDescription.RelativePath}.";

        if (allowAnonymous)
        {
            operation.Security = new List<OpenApiSecurityRequirement>();
            RemoveImplicitOkForNoContent(operation);
            return;
        }

        operation.Security = new List<OpenApiSecurityRequirement>
        {
            new()
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                }] = Array.Empty<string>()
            }
        };

        AddResponse(operation, "401", "JWT is missing, invalid, or expired.");
        if (authorize != null && !string.IsNullOrWhiteSpace(authorize.Roles))
            AddResponse(operation, "403", "The authenticated user does not have the required role.");

        RemoveImplicitOkForNoContent(operation);
    }

    private static void RemoveImplicitOkForNoContent(OpenApiOperation operation)
    {
        if (operation.Responses.ContainsKey("204") && operation.Responses.TryGetValue("200", out var ok) && ok.Content.Count == 0)
            operation.Responses.Remove("200");
    }

    private static void AddResponse(OpenApiOperation operation, string statusCode, string description)
    {
        if (!operation.Responses.ContainsKey(statusCode))
            operation.Responses[statusCode] = new OpenApiResponse { Description = description };
    }
}
