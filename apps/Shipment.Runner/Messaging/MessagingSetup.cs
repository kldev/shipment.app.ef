using System.ComponentModel.DataAnnotations;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Shipment.Messaging.Contracts;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace Shipment.Runner.Messaging;

public static class MessagingSetup
{
    /// <summary>Wolverine's message storage: the schema wolverine, owned by shipment_runner (migration AccessRoles).</summary>
    private const string Schema = "wolverine";

    /// <summary>
    /// Wolverine on RabbitMQ (ADR 0007, as Ambit's worker): the relay publishes the outbox's messages to the runner's
    /// own queue, the handlers of this assembly take them from there. The inbox, outbox and dead letters live in
    /// PostgreSQL; Wolverine creates and upgrades its own tables in its schema at start.
    /// </summary>
    public static WebApplicationBuilder AddRunnerMessaging(
        this WebApplicationBuilder builder,
        string connectionString
    )
    {
        var rabbitMq =
            builder.Configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()
            ?? throw new InvalidOperationException($"{RabbitMqOptions.SectionName} is not set.");
        Validator.ValidateObject(rabbitMq, new ValidationContext(rabbitMq), true);

        builder.Host.UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(MessagingSetup).Assembly;
            options.ServiceName = "shipment-runner";
            // The handlers take the application services, which take EF Core's IDbContextFactory - a factory
            // registration Wolverine cannot generate code for. It resolves them from the message's scope instead.
            options.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed;
            options.PersistMessagesWithPostgresql(connectionString, Schema);
            options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;

            // The queue belongs to the code that uses it: declared at start, in every environment.
            options.UseRabbitMq(rabbitMq.AmqpUri()).AutoProvision();
            // Every integration event of the outbox goes to the runner's own queue; its handlers call the
            // application service of the module that reacts (ADR 0007).
            options.PublishAllMessages().ToRabbitQueue(Queues.Runner).UseDurableOutbox();
            // One message at a time: the runner's work is small, and order keeps it predictable.
            options.ListenToRabbitQueue(Queues.Runner).UseDurableInbox().Sequential();
            // An exception is the infrastructure (the broker away, the database busy): try again later, then the dead
            // letters (wolverine.wolverine_dead_letters) for a person to look at.
            options
                .Policies.OnException<Exception>()
                .RetryWithCooldown(
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromMinutes(1)
                )
                .Then.MoveToErrorQueue();
        });

        return builder;
    }
}
