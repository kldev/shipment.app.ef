namespace Shipment.Api.Endpoints;

/// <summary>Every route of the REST API under one versioned prefix (ADR 0013). Tests use the same constants.</summary>
public static class ApiRoutes
{
    public const string Document = "v1";
    private const string Base = "/api/v1";

    public const string Me = $"{Base}/me";
    public const string Users = $"{Base}/users";

    public const string Partners = $"{Base}/partners";
    public const string Partner = $"{Partners}/{{partnerId:guid}}";
    public const string PartnerCategories = $"{Partner}/categories";
    public const string PickupPoints = $"{Base}/pickup-points";
    public const string PickupPoint = $"{PickupPoints}/{{pickupPointId:guid}}";
    public const string Customers = $"{Base}/customers";
    public const string Customer = $"{Customers}/{{customerId:guid}}";
    public const string CustomerAddresses = $"{Customer}/addresses";
    public const string CustomerAddress = $"{Base}/customer-addresses/{{addressId:guid}}";

    public const string Products = $"{Base}/products";
    public const string Product = $"{Products}/{{productId:guid}}";
    public const string ProductListed = $"{Product}/listed";

    public const string VehicleTypes = $"{Base}/vehicle-types";
    public const string Couriers = $"{Base}/couriers";
    public const string Courier = $"{Couriers}/{{courierId:guid}}";
    public const string CourierShiftStart = $"{Courier}/start-shift";
    public const string CourierShiftEnd = $"{Courier}/end-shift";
    public const string CourierPosition = $"{Courier}/position";

    public const string CourierDocuments = $"{Base}/courier-documents";
    public const string CourierDocumentVerify = $"{CourierDocuments}/{{documentId:guid}}/verify";
    public const string CourierClearance = $"{Base}/courier-clearance";
    public const string AgeCheck = $"{Base}/age-check";

    public const string Orders = $"{Base}/orders";
    public const string Order = $"{Orders}/{{orderId:guid}}";
    public const string OrderLines = $"{Order}/lines";
    public const string OrderCancel = $"{Order}/cancel";
    public const string OrderPickingStart = $"{Order}/picking/start";
    public const string OrderPickingFinish = $"{Order}/picking/finish";
    public const string OrderBilling = $"{Order}/billing";
    public const string Quote = $"{Base}/quote";
    public const string DeliveryFeeBands = $"{Base}/delivery-fee-bands";
    public const string PickingQueue = $"{Base}/picking-queue";
    public const string PickingHistory = $"{Base}/picking-history";

    public const string Shipments = $"{Base}/shipments";
    public const string Shipment = $"{Shipments}/{{shipmentId:guid}}";
    public const string ShipmentProposals = $"{Shipment}/proposals";
    public const string ShipmentAssign = $"{Shipment}/assign";
    public const string ShipmentAdvance = $"{Shipment}/advance";
    public const string ShipmentFail = $"{Shipment}/fail";
    public const string ShipmentUnassign = $"{Shipment}/unassign";
    public const string ShipmentTracking = $"{Shipment}/tracking";
    public const string ShipmentPings = $"{Shipment}/pings";
    public const string ShipmentSimulation = $"{Shipment}/simulation";
    public const string Tracking = $"{Base}/tracking";

    public const string BillingEntries = $"{Base}/billing/entries";
    public const string BillingDailySummary = $"{Base}/billing/daily-summary";
    public const string BillingPartnerBalances = $"{Base}/billing/partner-balances";
    public const string BillingCourierPayouts = $"{Base}/billing/courier-payouts";
    public const string BillingPayoutRates = $"{Base}/billing/payout-rates";
}

/// <summary>The tags of the API reference, one per bounded context.</summary>
public static class ApiTags
{
    public const string Platform = "Platform - Users";
    public const string Crm = "CRM - Partners and customers";
    public const string Catalog = "Catalog - Products";
    public const string Carriers = "Carriers - Couriers";
    public const string Compliance = "Compliance - Documents";
    public const string Ordering = "Ordering - Orders and picking";
    public const string Dispatch = "Dispatch - Shipments";
    public const string Tracking = "Tracking - Routes";
    public const string Billing = "Billing - Ledger";
}
