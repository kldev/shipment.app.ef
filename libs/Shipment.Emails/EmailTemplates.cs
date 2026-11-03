using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using Fluid;
using Microsoft.Extensions.FileProviders;

namespace Shipment.Emails;

/// <summary>A line of an order e-mail: how many of what, and its amount as people read it ("PLN 11.98").</summary>
public sealed record OrderEmailLine(int Quantity, string Name, string Amount);

/// <summary>
/// Model of the customer's order e-mails (Templates/order-*.liquid): the caller fills what its kind shows and
/// formats every amount and time - this library knows nothing of the domain. Lines are what is (or will be)
/// delivered; MissingLines what the store did not have.
/// </summary>
public sealed record OrderEmailModel(
    string CustomerFirstName,
    string OrderCode,
    string PartnerName,
    string PickupPointName,
    string DeliveryAddress,
    IReadOnlyList<OrderEmailLine> Lines,
    IReadOnlyList<OrderEmailLine> MissingLines,
    string Goods,
    string DeliveryFee,
    string Tip,
    bool HasTip,
    string Total,
    string Refund,
    string Reason,
    string DeliveredAt,
    string CourierFirstName,
    int Year,
    string AppName = "Shipment",
    string AppMark = "S"
);

/// <summary>
/// Liquid e-mail templates embedded in the assembly (Templates): an HTML version, HTML-encoded, and a
/// plain text one. Partials ({% include 'header' %}) come from the same embedded folder. Each template is parsed once.
/// </summary>
public sealed class EmailTemplates
{
    private static readonly FluidParser Parser = new();

    private readonly IFileProvider _files = new EmbeddedFileProvider(
        typeof(EmailTemplates).Assembly,
        "Shipment.Emails.Templates"
    );

    private readonly ConcurrentDictionary<string, IFluidTemplate> _parsed = new();
    private readonly TemplateOptions _options;

    public EmailTemplates()
    {
        _options = new TemplateOptions { FileProvider = _files };
        // Only registered model types are readable from a template - nothing else leaks into an e-mail by accident.
        _options.MemberAccessStrategy.Register<OrderEmailModel>();
        _options.MemberAccessStrategy.Register<OrderEmailLine>();
    }

    public async Task<(string Html, string Text)> RenderAsync<TModel>(string name, TModel model)
        where TModel : notnull
    {
        // HTML: every value HTML-encoded (a name like "<b>" stays text). Plain text: no encoding at all.
        var html = await RenderFileAsync($"{name}.html.liquid", model, HtmlEncoder.Default);
        var text = await RenderFileAsync($"{name}.text.liquid", model, NullEncoder.Default);
        return (html, text);
    }

    private async Task<string> RenderFileAsync(string path, object model, TextEncoder encoder)
    {
        var template = _parsed.GetOrAdd(path, Parse);
        var context = new TemplateContext(model, _options);
        await using var writer = new StringWriter();
        await template.RenderAsync(writer, encoder, context);
        return writer.ToString();
    }

    private IFluidTemplate Parse(string path)
    {
        var file = _files.GetFileInfo(path);
        if (!file.Exists)
            throw new FileNotFoundException(
                $"E-mail template '{path}' is not embedded in Shipment.Emails.",
                path
            );

        using var reader = new StreamReader(file.CreateReadStream());
        return Parser.TryParse(reader.ReadToEnd(), out var template, out var error)
            ? template
            : throw new InvalidOperationException(
                $"E-mail template '{path}' does not parse: {error}"
            );
    }
}

/// <summary>The customer's e-mails: the template of each kind and its subject.</summary>
public static class OrderEmails
{
    public const string Placed = "order-placed";
    public const string PartlyAvailable = "order-partial";
    public const string Cancelled = "order-cancelled";
    public const string Delivered = "order-delivered";

    public static string Subject(string kind, string orderCode) =>
        kind switch
        {
            Placed => $"Your order {orderCode} is placed",
            PartlyAvailable => $"Your order {orderCode}: some items were not available",
            Cancelled => $"Your order {orderCode} is cancelled",
            Delivered => $"Your order {orderCode} is delivered",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown e-mail kind."),
        };
}
