using System.Reflection;

namespace Shipment.Api.Endpoints;

public static class EndpointExtensions
{
    /// <summary>Maps every <see cref="IEndpointGroup"/> of this assembly - a new group never changes Program.cs.</summary>
    public static WebApplication MapEndpoints(this WebApplication app)
    {
        var groups = Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .Where(t =>
                t.IsAssignableTo(typeof(IEndpointGroup))
                && t is { IsInterface: false, IsAbstract: false }
            )
            // Stable order: it decides the order of operations and tags in the OpenAPI document.
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(Activator.CreateInstance)
            .Cast<IEndpointGroup>();
        foreach (var group in groups)
            group.Map(app);
        return app;
    }
}
