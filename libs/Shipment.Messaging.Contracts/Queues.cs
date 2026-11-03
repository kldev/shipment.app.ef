namespace Shipment.Messaging.Contracts;

/// <summary>RabbitMQ queues (vhost shipment): each background process listens only to its own.</summary>
public static class Queues
{
    /// <summary>What the runner does: the integration events between modules.</summary>
    public const string Runner = "shipment-runner";
}
