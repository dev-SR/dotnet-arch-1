using Microsoft.OpenApi;

namespace MyApp.OrdersApi.Presentation;

public static class OpenApiAuthExtensions
{
    public static void AddAuthSecuritySchemes(this OpenApiDocument document, string description)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = description,
        };
    }
}
