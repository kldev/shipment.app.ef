using System.ComponentModel;
using Shipment.Application.Compliance;
using Shipment.Domain.Compliance;

namespace Shipment.Api.Endpoints;

internal sealed class ComplianceEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags(ApiTags.Compliance);

        group
            .MapGet(
                ApiRoutes.CourierDocuments,
                (
                    Guid? courierId,
                    Wire<DocumentKind>? kind,
                    Wire<DocumentState>? state,
                    ComplianceService compliance,
                    CancellationToken ct
                ) => compliance.ListDocumentsAsync(courierId, kind?.Value, state?.Value, ct)
            )
            .WithName("Get courier documents")
            .WithSummary("Documents with their state on today's date (Europe/Warsaw)");
        // POST /api/v1/courier-documents - one per kind: a renewal replaces the courier's current one
        group
            .MapPost(
                ApiRoutes.CourierDocuments,
                async (SetDocument request, ComplianceService compliance, CancellationToken ct) =>
                    ApiResults.Ok((await compliance.SetDocumentAsync(request, ct)).AsSaved())
            )
            .WithName("Set courier document")
            .WithSummary(
                "Record a courier's document; another number or date needs verifying again"
            )
            .ProducesRefusals();
        group
            .MapPost(
                ApiRoutes.CourierDocumentVerify,
                async (
                    Guid documentId,
                    VersionOf request,
                    ComplianceService compliance,
                    CancellationToken ct
                ) =>
                    ApiResults.Ok(
                        (
                            await compliance.VerifyDocumentAsync(
                                new VerifyDocument(documentId, request.RowVersion),
                                ct
                            )
                        ).AsSaved()
                    )
            )
            .WithName("Verify courier document")
            .WithSummary("Confirm a document was seen; it then counts while valid")
            .ProducesRefusals();
        group
            .MapGet(
                ApiRoutes.CourierClearance,
                (Guid? courierId, ComplianceService compliance, CancellationToken ct) =>
                    compliance.ListClearanceAsync(courierId, ct)
            )
            .WithName("Get courier clearance")
            .WithSummary("Whether each courier may take orders, with the reasons when not");
        group
            .MapGet(
                ApiRoutes.AgeCheck,
                (
                    [Description("Product ids, the parameter repeated.")] Guid[] productIds,
                    ComplianceService compliance,
                    CancellationToken ct
                ) => compliance.AgeCheckAsync(productIds, ct)
            )
            .WithName("Get age check")
            .WithSummary("Whether the products need an ID check at the door, and which");
    }
}
