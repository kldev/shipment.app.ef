using Shipment.Application.Crm;
using Shipment.Domain.Crm;

namespace Shipment.Api.Endpoints;

internal sealed class CrmEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags(ApiTags.Crm);

        group
            .MapGet(
                ApiRoutes.Partners,
                (CrmService crm, CancellationToken ct) => crm.ListPartnersAsync(ct)
            )
            .WithName("Get partners")
            .WithSummary("Partners with their active pickup points");
        group
            .MapGet(
                ApiRoutes.Partner,
                async (Guid partnerId, CrmService crm, CancellationToken ct) =>
                    ApiResults.Found(await crm.GetPartnerAsync(partnerId, ct))
            )
            .WithName("Get partner")
            .WithSummary("A partner");
        // POST /api/v1/partners - the id comes from the client (ADR 0009); the same request again answers the same
        group
            .MapPost(
                ApiRoutes.Partners,
                async (CreatePartner request, CrmService crm, CancellationToken ct) =>
                    ApiResults.Created(
                        (await crm.CreatePartnerAsync(request, ct)).AsSaved(),
                        $"{ApiRoutes.Partners}/{request.Id}"
                    )
            )
            .WithName("Create partner")
            .WithSummary("Add a partner")
            .ProducesRefusals();
        group
            .MapPut(
                ApiRoutes.Partner,
                async (
                    Guid partnerId,
                    PartnerChange request,
                    CrmService crm,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await crm.UpdatePartnerAsync(
                                new UpdatePartner(
                                    partnerId,
                                    request.Code,
                                    request.Name,
                                    request.Kind,
                                    request.CommissionBps,
                                    request.Status,
                                    request.RowVersion
                                ),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Update partner")
            .WithSummary("Edit a partner; the same values are a no-op")
            .ProducesRefusals();

        group
            .MapGet(
                ApiRoutes.PickupPoints,
                (
                    Guid? partnerId,
                    Wire<PickupPointStatus>? status,
                    CrmService crm,
                    CancellationToken ct
                ) => crm.ListPickupPointsAsync(partnerId, status?.Value, ct)
            )
            .WithName("Get pickup points")
            .WithSummary("Pickup points with whether they are open now (Europe/Warsaw)");
        group
            .MapGet(
                ApiRoutes.PickupPoint,
                async (Guid pickupPointId, CrmService crm, CancellationToken ct) =>
                    ApiResults.Found(await crm.GetPickupPointAsync(pickupPointId, ct))
            )
            .WithName("Get pickup point")
            .WithSummary("A pickup point");
        group
            .MapPost(
                ApiRoutes.PickupPoints,
                async (CreatePickupPoint request, CrmService crm, CancellationToken ct) =>
                    ApiResults.Created(
                        (await crm.CreatePickupPointAsync(request, ct)).AsSaved(),
                        $"{ApiRoutes.PickupPoints}/{request.Id}"
                    )
            )
            .WithName("Create pickup point")
            .WithSummary("Add a pickup point to a partner")
            .ProducesRefusals();
        group
            .MapPut(
                ApiRoutes.PickupPoint,
                async (
                    Guid pickupPointId,
                    PickupPointChange request,
                    CrmService crm,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await crm.UpdatePickupPointAsync(
                                new UpdatePickupPoint(
                                    pickupPointId,
                                    request.Name,
                                    request.AddressLine,
                                    request.City,
                                    request.Postcode,
                                    request.Latitude,
                                    request.Longitude,
                                    request.OpensMinute,
                                    request.ClosesMinute,
                                    request.Status,
                                    request.RowVersion
                                ),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Update pickup point")
            .WithSummary("Edit a pickup point; a closed one takes no new orders")
            .ProducesRefusals();

        group
            .MapGet(
                ApiRoutes.Customers,
                (CrmService crm, CancellationToken ct) => crm.ListCustomersAsync(ct)
            )
            .WithName("Get customers")
            .WithSummary("Customers with their default city");
        group
            .MapGet(
                ApiRoutes.Customer,
                async (Guid customerId, CrmService crm, CancellationToken ct) =>
                    ApiResults.Found(await crm.GetCustomerAsync(customerId, ct))
            )
            .WithName("Get customer")
            .WithSummary("A customer");
        group
            .MapPost(
                ApiRoutes.Customers,
                async (CreateCustomer request, CrmService crm, CancellationToken ct) =>
                    ApiResults.Created(
                        (await crm.CreateCustomerAsync(request, ct)).AsSaved(),
                        $"{ApiRoutes.Customers}/{request.Id}"
                    )
            )
            .WithName("Create customer")
            .WithSummary("Add a customer; the e-mail is unique")
            .ProducesRefusals();
        group
            .MapPut(
                ApiRoutes.Customer,
                async (
                    Guid customerId,
                    CustomerChange request,
                    CrmService crm,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await crm.UpdateCustomerAsync(
                                new UpdateCustomer(
                                    customerId,
                                    request.DisplayName,
                                    request.Email,
                                    request.Phone,
                                    request.RowVersion
                                ),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Update customer")
            .WithSummary("Edit a customer")
            .ProducesRefusals();

        group
            .MapGet(
                ApiRoutes.CustomerAddresses,
                (Guid customerId, bool? isDefault, CrmService crm, CancellationToken ct) =>
                    crm.ListAddressesAsync(customerId, isDefault, ct)
            )
            .WithName("Get customer addresses")
            .WithSummary("A customer's delivery addresses, the default first");
        group
            .MapPost(
                ApiRoutes.CustomerAddresses,
                async (Guid customerId, AddressNew request, CrmService crm, CancellationToken ct) =>
                    ApiResults.Created(
                        (
                            await crm.CreateAddressAsync(
                                new CreateCustomerAddress(
                                    request.Id,
                                    customerId,
                                    request.Label,
                                    request.AddressLine,
                                    request.City,
                                    request.Postcode,
                                    request.Latitude,
                                    request.Longitude,
                                    request.Notes,
                                    request.IsDefault
                                ),
                                ct
                            )
                        ).AsSaved(),
                        ApiRoutes.CustomerAddress.Replace("{addressId:guid}", request.Id.ToString())
                    )
            )
            .WithName("Create customer address")
            .WithSummary("Add a delivery address; the first one becomes the default")
            .ProducesRefusals();
        group
            .MapPut(
                ApiRoutes.CustomerAddress,
                async (Guid addressId, AddressEdit request, CrmService crm, CancellationToken ct) =>
                    ApiResults.Ok(
                        (
                            await crm.UpdateAddressAsync(
                                new UpdateCustomerAddress(
                                    addressId,
                                    request.Label,
                                    request.AddressLine,
                                    request.City,
                                    request.Postcode,
                                    request.Latitude,
                                    request.Longitude,
                                    request.Notes,
                                    request.IsDefault,
                                    request.RowVersion
                                ),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Update customer address")
            .WithSummary(
                "Edit a delivery address; making it the default takes the flag from the old one"
            )
            .ProducesRefusals();
    }

    internal sealed record PartnerChange(
        string Code,
        string Name,
        PartnerKind Kind,
        int CommissionBps,
        PartnerStatus Status,
        uint RowVersion
    );

    internal sealed record PickupPointChange(
        string Name,
        string AddressLine,
        string City,
        string Postcode,
        decimal Latitude,
        decimal Longitude,
        int OpensMinute,
        int ClosesMinute,
        PickupPointStatus Status,
        uint RowVersion
    );

    internal sealed record CustomerChange(
        string DisplayName,
        string Email,
        string? Phone,
        uint RowVersion
    );

    /// <summary>A new address; the id comes from the client (ADR 0009).</summary>
    internal sealed record AddressNew(
        Guid Id,
        string Label,
        string AddressLine,
        string City,
        string Postcode,
        decimal Latitude,
        decimal Longitude,
        string? Notes = null,
        bool IsDefault = false
    );

    /// <summary>An edit of an address, with the row version read.</summary>
    internal sealed record AddressEdit(
        string Label,
        string AddressLine,
        string City,
        string Postcode,
        decimal Latitude,
        decimal Longitude,
        uint RowVersion,
        string? Notes = null,
        bool IsDefault = false
    );
}
