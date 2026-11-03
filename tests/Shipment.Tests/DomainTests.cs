using Shipment.Domain.Billing;
using Shipment.Domain.Carriers;
using Shipment.Domain.Common;
using Shipment.Domain.Compliance;
using Shipment.Domain.Dispatch;
using Shipment.Domain.Ordering;

namespace Shipment.Tests;

/// <summary>
/// The domain's rules without a database (ADR 0013: "the domain is testable without a database"), written from the
/// specification, through the public API only.
/// </summary>
[Trait("verify", "domain")]
public sealed class DomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    // ---- OperatorTime: Europe/Warsaw ----

    [Theory]
    // Winter (UTC+1): 22:59 UTC is still the same day, 23:00 UTC the next one.
    [InlineData("2026-01-15T22:59:00Z", "2026-01-15")]
    [InlineData("2026-01-15T23:00:00Z", "2026-01-16")]
    // Summer (UTC+2): 21:59 UTC is still the same day, 22:00 UTC the next one.
    [InlineData("2026-07-15T21:59:00Z", "2026-07-15")]
    [InlineData("2026-07-15T22:00:00Z", "2026-07-16")]
    // The nights of the clock changes (29 March and 25 October 2026).
    [InlineData("2026-03-28T23:00:00Z", "2026-03-29")]
    [InlineData("2026-03-29T21:59:00Z", "2026-03-29")]
    [InlineData("2026-03-29T22:00:00Z", "2026-03-30")]
    [InlineData("2026-10-24T22:00:00Z", "2026-10-25")]
    [InlineData("2026-10-25T22:59:00Z", "2026-10-25")]
    [InlineData("2026-10-25T23:00:00Z", "2026-10-26")]
    public void The_operators_day_follows_warsaw_including_the_clock_changes(
        string instant,
        string day
    ) =>
        Assert.Equal(
            DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture),
            OperatorTime.Today(
                DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture)
            )
        );

    [Fact]
    public void A_day_starts_at_warsaw_midnight_and_the_dst_days_are_23_and_25_hours_long()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 1, 14, 23, 0, 0, TimeSpan.Zero),
            OperatorTime.StartOf(new DateOnly(2026, 1, 15))
        );
        Assert.Equal(
            new DateTimeOffset(2026, 7, 14, 22, 0, 0, TimeSpan.Zero),
            OperatorTime.StartOf(new DateOnly(2026, 7, 15))
        );
        // Spring forward: 29 March has 23 hours.
        Assert.Equal(
            TimeSpan.FromHours(23),
            OperatorTime.StartOf(new DateOnly(2026, 3, 30))
                - OperatorTime.StartOf(new DateOnly(2026, 3, 29))
        );
        // Fall back: 25 October has 25 hours.
        Assert.Equal(
            TimeSpan.FromHours(25),
            OperatorTime.StartOf(new DateOnly(2026, 10, 26))
                - OperatorTime.StartOf(new DateOnly(2026, 10, 25))
        );
        // The start of a day is that day.
        foreach (var day in new[] { new DateOnly(2026, 3, 29), new DateOnly(2026, 10, 25) })
            Assert.Equal(day, OperatorTime.Today(OperatorTime.StartOf(day)));
    }

    [Theory]
    [InlineData("2026-01-15T23:00:00Z", 0)]
    [InlineData("2026-01-15T07:30:00Z", 510)]
    [InlineData("2026-07-15T06:30:00Z", 510)]
    // 01:30 UTC on 29 March is 03:30 summer time.
    [InlineData("2026-03-29T01:30:00Z", 210)]
    // 02:30 happens twice on 25 October.
    [InlineData("2026-10-25T00:30:00Z", 150)]
    [InlineData("2026-10-25T01:30:00Z", 150)]
    public void The_minute_of_the_day_is_the_warsaw_wall_clock(string instant, int minute) =>
        Assert.Equal(
            minute,
            OperatorTime.MinuteOfDay(
                DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture)
            )
        );

    // ---- Clearance ----

    private static readonly DateOnly Today = new(2026, 11, 1);

    private static DocumentFacts Doc(DocumentKind kind, int daysLeft, bool verified = true) =>
        new(kind, Today.AddDays(daysLeft), verified);

    [Fact]
    public void A_bike_needs_only_a_verified_valid_id_card()
    {
        var cleared = Clearance.Of(false, [Doc(DocumentKind.IdCard, 365)], Today);
        Assert.True(cleared.Cleared);
        Assert.Empty(cleared.Reasons);
        Assert.False(cleared.ExpiringSoon);
        Assert.Equal(Today.AddDays(365), cleared.NextExpiry);

        var none = Clearance.Of(false, [], Today);
        Assert.False(none.Cleared);
        Assert.Equal(["ID card missing"], none.Reasons);
        Assert.Null(none.NextExpiry);

        // A document no vehicle needs changes nothing.
        Assert.True(
            Clearance
                .Of(
                    false,
                    [Doc(DocumentKind.IdCard, 365), Doc(DocumentKind.FoodHygiene, 365, false)],
                    Today
                )
                .Cleared
        );
    }

    [Fact]
    public void A_car_or_van_needs_the_id_a_driving_licence_and_vehicle_insurance()
    {
        var idOnly = Clearance.Of(true, [Doc(DocumentKind.IdCard, 365)], Today);
        Assert.False(idOnly.Cleared);
        Assert.Equal(
            ["Driving licence missing", "Vehicle insurance missing"],
            idOnly.Reasons.Order()
        );

        var all = Clearance.Of(
            true,
            [
                Doc(DocumentKind.IdCard, 365),
                Doc(DocumentKind.DrivingLicence, 365),
                Doc(DocumentKind.VehicleInsurance, 365),
            ],
            Today
        );
        Assert.True(all.Cleared);
        Assert.Empty(all.Reasons);
    }

    [Fact]
    public void Expired_wins_over_not_verified_and_each_document_says_what_is_wrong()
    {
        var result = Clearance.Of(
            true,
            [
                Doc(DocumentKind.IdCard, -1, verified: false),
                Doc(DocumentKind.DrivingLicence, 100, verified: false),
            ],
            Today
        );
        Assert.False(result.Cleared);
        Assert.Equal(
            ["Driving licence not verified", "ID card expired", "Vehicle insurance missing"],
            result.Reasons.Order()
        );
    }

    [Fact]
    public void A_document_valid_until_today_still_counts()
    {
        var result = Clearance.Of(false, [Doc(DocumentKind.IdCard, 0)], Today);
        Assert.True(result.Cleared);
        Assert.True(result.ExpiringSoon);
        Assert.Equal(Today, result.NextExpiry);
        Assert.Equal(DocumentState.Expiring, CourierDocument.StateOf(Today, verified: true, Today));
        Assert.Equal(
            DocumentState.Expired,
            CourierDocument.StateOf(Today.AddDays(-1), verified: true, Today)
        );
    }

    [Fact]
    public void Expiring_soon_is_a_document_valid_for_less_than_30_more_days()
    {
        Assert.True(Clearance.Of(false, [Doc(DocumentKind.IdCard, 29)], Today).ExpiringSoon);
        Assert.False(Clearance.Of(false, [Doc(DocumentKind.IdCard, 31)], Today).ExpiringSoon);
        Assert.Equal(
            DocumentState.Expiring,
            CourierDocument.StateOf(Today.AddDays(29), true, Today)
        );
        Assert.Equal(DocumentState.Valid, CourierDocument.StateOf(Today.AddDays(31), true, Today));
        // Expired wins over unverified; unverified over expiring.
        Assert.Equal(
            DocumentState.Expired,
            CourierDocument.StateOf(Today.AddDays(-5), false, Today)
        );
        Assert.Equal(
            DocumentState.Unverified,
            CourierDocument.StateOf(Today.AddDays(5), false, Today)
        );
        // An expired document is no "next expiry": that one is behind us.
        var mixed = Clearance.Of(
            false,
            [Doc(DocumentKind.IdCard, 200), Doc(DocumentKind.FoodHygiene, -3)],
            Today
        );
        Assert.Equal(Today.AddDays(200), mixed.NextExpiry);
    }

    [Fact]
    public void The_30th_day_is_no_longer_expiring_as_the_former_view_counted_it()
    {
        // compliance_app.courier_documents: valid_until < today + 30 -> expiring.
        Assert.Equal(DocumentState.Valid, CourierDocument.StateOf(Today.AddDays(30), true, Today));
        Assert.False(Clearance.Of(false, [Doc(DocumentKind.IdCard, 30)], Today).ExpiringSoon);
    }

    // ---- Delivery fee bands ----

    [Theory]
    [InlineData(0, 599L)]
    [InlineData(2000, 599L)]
    [InlineData(2001, 899L)]
    [InlineData(5000, 899L)]
    [InlineData(5001, 1299L)]
    [InlineData(10000, 1299L)]
    [InlineData(10001, 1999L)]
    [InlineData(40000, 1999L)]
    [InlineData(40001, null)]
    [InlineData(400000, null)]
    public void The_fee_is_the_first_band_that_reaches_the_distance(int metres, long? fee) =>
        Assert.Equal(fee, DeliveryFeeBand.FeeFor(DeliveryFeeBand.All, metres));

    [Fact]
    public void The_fee_bands_do_not_depend_on_their_order()
    {
        var reversed = DeliveryFeeBand.All.Reverse().ToList();
        Assert.Equal(599L, DeliveryFeeBand.FeeFor(reversed, 1500));
        Assert.Equal(1299L, DeliveryFeeBand.FeeFor(reversed, 7000));
    }

    // ---- Payout ----

    private static PayoutRate Rate(string vehicle) =>
        PayoutRate.All.Single(r => r.VehicleType == vehicle);

    [Theory]
    [InlineData("bike", 0L, 600L)]
    [InlineData("bike", 1000L, 720L)]
    [InlineData("bike", 3000L, 960L)]
    [InlineData("bike", 1004L, 720L)] // 120.48
    [InlineData("bike", 1005L, 721L)] // 120.6
    [InlineData("car", 2500L, 1200L)]
    [InlineData("car", 1003L, 960L)] // 160.48
    [InlineData("van", 1000L, 1420L)]
    [InlineData("van", 25L, 1206L)] // 5.5 -> 6
    [InlineData("van", 75L, 1217L)] // 16.5 -> 17: half away from zero, as round(numeric) did
    public void The_payout_is_the_base_plus_the_rounded_rate_per_km(
        string vehicle,
        long metres,
        long payout
    ) => Assert.Equal(payout, Rate(vehicle).PayoutFor(metres));

    // ---- Order: tip ----

    private static Order OrderOf(long unitPriceMinor, int quantity, int tipPercent) =>
        new(
            Guid.CreateVersion7(),
            "ORD-1",
            new OrderDropoff(
                Guid.CreateVersion7(),
                "Ola",
                Guid.CreateVersion7(),
                "Home",
                "ul. Długa 15",
                "Kraków",
                "31-147",
                null,
                50.0706m,
                19.9377m
            ),
            new OrderPickup(
                Guid.CreateVersion7(),
                "Grocer",
                Guid.CreateVersion7(),
                "Kazimierz",
                "ul. Miodowa 22",
                50.0513m,
                19.9446m
            ),
            [
                (
                    new OrderProduct(
                        Guid.CreateVersion7(),
                        "SKU",
                        "Thing",
                        unitPriceMinor,
                        100,
                        SizeClass.S,
                        false
                    ),
                    quantity
                ),
            ],
            2300,
            899,
            tipPercent,
            Now
        );

    [Theory]
    [InlineData(899L, 1, 0, 0L)]
    [InlineData(400L, 5, 10, 200L)]
    [InlineData(899L, 1, 5, 45L)] // 44.95
    [InlineData(905L, 1, 10, 91L)] // 90.5 -> 91, half away from zero
    [InlineData(50L, 1, 5, 3L)] // 2.5 -> 3, half away from zero
    [InlineData(10L, 1, 5, 1L)] // 0.5 -> 1
    [InlineData(9L, 1, 5, 0L)] // 0.45 -> 0
    public void The_tip_is_a_rounded_percent_of_the_goods_as_ordered(
        long unitPriceMinor,
        int quantity,
        int tipPercent,
        long tipMinor
    )
    {
        var order = OrderOf(unitPriceMinor, quantity, tipPercent);
        Assert.Equal(tipMinor, order.TipMinor);
        Assert.Equal(unitPriceMinor * quantity, order.GoodsOrderedMinor);
        Assert.Equal(OrderStatus.Placed, order.Status);
    }

    [Fact]
    public void Picking_decides_every_line_once_and_nothing_packed_cancels()
    {
        var order = OrderOf(500, 3, 10);
        Assert.Equal(
            ErrorKind.Conflict,
            order.FinishPicking([new LinePick(1, 3, false)], Now).Error?.Kind
        );
        Assert.True(order.StartPicking(null, Now).IsSuccess);
        Assert.Equal(ErrorKind.Validation, order.FinishPicking([], Now).Error?.Kind);
        Assert.Equal(
            ErrorKind.Validation,
            order.FinishPicking([new LinePick(1, 4, false)], Now).Error?.Kind
        );
        Assert.Equal(
            ErrorKind.Validation,
            order.FinishPicking([new LinePick(1, -1, false)], Now).Error?.Kind
        );
        Assert.Equal(
            ErrorKind.Validation,
            order
                .FinishPicking([new LinePick(1, 1, false), new LinePick(1, 1, false)], Now)
                .Error?.Kind
        );
        Assert.Equal(
            ErrorKind.Validation,
            order.FinishPicking([new LinePick(2, 1, false)], Now).Error?.Kind
        );
        var finished = order.FinishPicking([new LinePick(1, 0, false)], Now);
        Assert.True(finished.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, finished.Value);
        Assert.Equal(Order.NothingAvailable, order.CancelReason);
    }

    [Fact]
    public void A_partial_pick_keeps_the_tip_as_chosen()
    {
        var order = OrderOf(500, 4, 10);
        order.StartPicking(null, Now);
        var finished = order.FinishPicking([new LinePick(1, 1, false)], Now);
        Assert.Equal(OrderStatus.Ready, finished.Value);
        Assert.Equal(500, order.GoodsPickedMinor);
        Assert.Equal(200, order.TipMinor);
    }

    // ---- Delivery: the courier's steps ----

    private static Delivery DeliveryOf(bool ageCheckRequired) =>
        new(
            Guid.CreateVersion7(),
            "SHP-1",
            new DeliveryTrip(
                Guid.CreateVersion7(),
                "ORD-1",
                Guid.CreateVersion7(),
                "Ola",
                "Grocer",
                Guid.CreateVersion7(),
                "Kazimierz",
                "ul. Miodowa 22",
                50.0513m,
                19.9446m,
                "Home",
                "ul. Długa 15",
                "Kraków",
                null,
                50.0706m,
                19.9377m,
                2300,
                2,
                1200,
                SizeClass.S,
                ageCheckRequired
            ),
            VehicleType.Bike
        );

    [Fact]
    public void A_delivery_takes_its_steps_one_at_a_time_in_order()
    {
        var delivery = DeliveryOf(ageCheckRequired: false);
        Assert.Equal(ShipmentStatus.Available, delivery.Status);
        Assert.Equal(
            ErrorKind.Conflict,
            delivery.Advance(ShipmentStatus.ArrivedAtPickup, false, Now).Error?.Kind
        );
        Assert.Equal(ErrorKind.Conflict, delivery.Unassign().Error?.Kind);

        var courier = Guid.CreateVersion7();
        Assert.True(delivery.Assign(courier, Now).IsSuccess);
        Assert.Equal(ShipmentStatus.CourierAssigned, delivery.Status);
        Assert.Equal(courier, delivery.CourierId);
        Assert.Equal(ErrorKind.Conflict, delivery.Assign(Guid.CreateVersion7(), Now).Error?.Kind);

        // No skipping, no going back, no repeating.
        Assert.Equal(
            ErrorKind.Conflict,
            delivery.Advance(ShipmentStatus.PickedUp, false, Now).Error?.Kind
        );
        Assert.Equal(
            ErrorKind.Conflict,
            delivery.Advance(ShipmentStatus.Delivered, true, Now).Error?.Kind
        );
        Assert.True(delivery.Advance(ShipmentStatus.ArrivedAtPickup, false, Now).IsSuccess);
        Assert.NotNull(delivery.ArrivedAtPickupAt);
        Assert.Equal(
            ErrorKind.Conflict,
            delivery.Advance(ShipmentStatus.ArrivedAtPickup, false, Now).Error?.Kind
        );
        // Back to an earlier state is refused (assigned is not a step of Advance at all).
        Assert.False(delivery.Advance(ShipmentStatus.CourierAssigned, false, Now).IsSuccess);
        Assert.Equal(ShipmentStatus.ArrivedAtPickup, delivery.Status);
        // Failed is not a step.
        Assert.Equal(
            ErrorKind.Validation,
            delivery.Advance(ShipmentStatus.Failed, false, Now).Error?.Kind
        );
        Assert.Equal(
            ErrorKind.Validation,
            delivery.Advance(ShipmentStatus.Available, false, Now).Error?.Kind
        );
        // Before pickup: unassign yes, fail no.
        Assert.Equal(ErrorKind.Conflict, delivery.Fail("Customer absent", Now).Error?.Kind);

        Assert.True(delivery.Advance(ShipmentStatus.PickedUp, false, Now).IsSuccess);
        Assert.NotNull(delivery.PickedUpAt);
        Assert.Equal(ErrorKind.Conflict, delivery.Unassign().Error?.Kind);
        Assert.True(delivery.Advance(ShipmentStatus.ArrivedAtCustomer, false, Now).IsSuccess);
        // No 18+ in the bag: no ID check needed.
        Assert.True(delivery.Advance(ShipmentStatus.Delivered, false, Now).IsSuccess);
        Assert.Equal(ShipmentStatus.Delivered, delivery.Status);
        Assert.NotNull(delivery.DeliveredAt);
        Assert.False(delivery.IsActive);

        Assert.Equal(
            ErrorKind.Conflict,
            delivery.Advance(ShipmentStatus.Delivered, false, Now).Error?.Kind
        );
        Assert.Equal(ErrorKind.Conflict, delivery.Fail("late", Now).Error?.Kind);
        Assert.Equal(ErrorKind.Conflict, delivery.Unassign().Error?.Kind);
    }

    [Fact]
    public void An_18_plus_delivery_needs_the_id_check_and_may_fail_at_the_door()
    {
        var delivery = DeliveryOf(ageCheckRequired: true);
        delivery.Assign(Guid.CreateVersion7(), Now);
        foreach (
            var step in new[]
            {
                ShipmentStatus.ArrivedAtPickup,
                ShipmentStatus.PickedUp,
                ShipmentStatus.ArrivedAtCustomer,
            }
        )
            Assert.True(delivery.Advance(step, false, Now).IsSuccess);
        Assert.Equal(
            ErrorKind.Validation,
            delivery.Advance(ShipmentStatus.Delivered, false, Now).Error?.Kind
        );
        Assert.Equal(ShipmentStatus.ArrivedAtCustomer, delivery.Status);
        Assert.True(delivery.Fail("ID check refused", Now).IsSuccess);
        Assert.Equal(ShipmentStatus.Failed, delivery.Status);
        Assert.Equal("ID check refused", delivery.FailureReason);
        Assert.False(delivery.IsActive);
    }

    [Fact]
    public void Unassign_before_pickup_makes_the_delivery_available_again()
    {
        var delivery = DeliveryOf(ageCheckRequired: false);
        delivery.Assign(Guid.CreateVersion7(), Now);
        delivery.Advance(ShipmentStatus.ArrivedAtPickup, false, Now);
        Assert.True(delivery.Unassign().IsSuccess);
        Assert.Equal(ShipmentStatus.Available, delivery.Status);
        Assert.Null(delivery.CourierId);
        // A new courier starts from the beginning.
        Assert.True(delivery.Assign(Guid.CreateVersion7(), Now).IsSuccess);
        Assert.Equal(
            ErrorKind.Conflict,
            delivery.Advance(ShipmentStatus.PickedUp, false, Now).Error?.Kind
        );
    }

    // ---- Courier: shifts ----

    [Fact]
    public void A_busy_courier_cannot_end_the_shift_and_cannot_be_taken_twice()
    {
        var courier = new Courier(
            Guid.CreateVersion7(),
            new CourierData("Kuba Rider", "+48 700 100 200", "kuba@couriers.example", "bike", null)
        );
        Assert.Equal(ShiftStatus.Offline, courier.ShiftStatus);
        Assert.True(courier.StartShift(Now));
        Assert.False(courier.StartShift(Now));
        Assert.True(courier.TakeForShipment().IsSuccess);
        Assert.Equal(ShiftStatus.Busy, courier.ShiftStatus);
        Assert.Equal(ErrorKind.Conflict, courier.TakeForShipment().Error?.Kind);
        Assert.Equal(ErrorKind.Conflict, courier.EndShift().Error?.Kind);
        Assert.Equal(
            ErrorKind.Conflict,
            courier
                .Update(
                    new CourierData(
                        "Kuba Rider",
                        "+48 700 100 200",
                        "kuba@couriers.example",
                        "car",
                        "KR1234"
                    )
                )
                .Error?.Kind
        );
        courier.Release();
        Assert.Equal(ShiftStatus.Available, courier.ShiftStatus);
        var ended = courier.EndShift();
        Assert.True(ended.IsSuccess);
        Assert.True(ended.Value);
        Assert.Equal(ShiftStatus.Offline, courier.ShiftStatus);
        // Offline already: a no-op.
        var again = courier.EndShift();
        Assert.True(again.IsSuccess);
        Assert.False(again.Value);
    }

    [Fact]
    public void An_offline_courier_cannot_be_taken_for_a_shipment()
    {
        var courier = new Courier(
            Guid.CreateVersion7(),
            new CourierData("Kuba Rider", "+48 700 100 200", "kuba@couriers.example", "bike", null)
        );
        Assert.Equal(ErrorKind.Conflict, courier.TakeForShipment().Error?.Kind);
        Assert.Equal(ShiftStatus.Offline, courier.ShiftStatus);
    }

    [Fact]
    public void An_unchanged_courier_is_a_no_op()
    {
        var courier = new Courier(
            Guid.CreateVersion7(),
            new CourierData("Kuba Rider", "+48 700 100 200", "kuba@couriers.example", "bike", null)
        );
        // (A van without a plate is the service's validation - CarriersGapTests.)
        // The same values: no change.
        var same = courier.Update(
            new CourierData("Kuba Rider", "+48 700 100 200", "kuba@couriers.example", "bike", null)
        );
        Assert.True(same.IsSuccess);
        Assert.False(same.Value);
    }
}
