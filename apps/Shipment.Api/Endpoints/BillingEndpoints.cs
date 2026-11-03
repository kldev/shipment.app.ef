using Shipment.Application.Billing;
using Shipment.Domain.Billing;

namespace Shipment.Api.Endpoints;

internal sealed class BillingEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags(ApiTags.Billing);

        group
            .MapGet(
                ApiRoutes.BillingEntries,
                (
                    Guid? orderId,
                    Guid? shipmentId,
                    Guid? partnerId,
                    Guid? courierId,
                    Wire<EntryKind>? kind,
                    BillingService billing,
                    CancellationToken ct
                ) =>
                    billing.ListEntriesAsync(
                        new EntryFilter(orderId, shipmentId, partnerId, courierId, kind?.Value),
                        ct
                    )
            )
            .WithName("Get billing entries")
            .WithSummary("The ledger as booked");
        group
            .MapGet(
                ApiRoutes.OrderBilling,
                async (Guid orderId, BillingService billing, CancellationToken ct) =>
                    ApiResults.Found(await billing.GetOrderBillingAsync(orderId, ct))
            )
            .WithName("Get order billing")
            .WithSummary("The money of one order");
        group
            .MapGet(
                ApiRoutes.BillingDailySummary,
                (DateOnly from, DateOnly to, BillingService billing, CancellationToken ct) =>
                    billing.DailySummaryAsync(from, to, ct)
            )
            .WithName("Get daily summary")
            .WithSummary("Per day of the operator (Europe/Warsaw), both days included");
        group
            .MapGet(
                ApiRoutes.BillingPartnerBalances,
                (DateOnly from, DateOnly to, BillingService billing, CancellationToken ct) =>
                    billing.PartnerBalancesAsync(from, to, ct)
            )
            .WithName("Get partner balances")
            .WithSummary("What each partner earned by delivery in the period");
        group
            .MapGet(
                ApiRoutes.BillingCourierPayouts,
                (DateOnly from, DateOnly to, BillingService billing, CancellationToken ct) =>
                    billing.CourierPayoutsAsync(from, to, ct)
            )
            .WithName("Get courier payouts")
            .WithSummary("What each courier earned in the period");
        group
            .MapGet(
                ApiRoutes.BillingPayoutRates,
                (BillingService billing, CancellationToken ct) => billing.ListPayoutRatesAsync(ct)
            )
            .WithName("Get payout rates")
            .WithSummary("What a courier earns per delivery by vehicle");
    }
}
