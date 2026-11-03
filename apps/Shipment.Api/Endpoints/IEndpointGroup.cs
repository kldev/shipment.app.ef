namespace Shipment.Api.Endpoints;

/// <summary>A group of endpoints of one area; <see cref="EndpointExtensions.MapEndpoints"/> finds and maps every one.</summary>
public interface IEndpointGroup
{
    void Map(IEndpointRouteBuilder app);
}
