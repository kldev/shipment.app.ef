using JetBrains.Annotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Shipment.Domain.Common;

namespace Shipment.Api.Endpoints;

/// <summary>A <see cref="Result"/> as HTTP: the value, or ProblemDetails with 400, 404 or 409 and the message as detail.</summary>
public static class ApiResults
{
    public static Results<Ok<T>, ProblemHttpResult> Ok<T>(Result<T> result) =>
        result.Error is { } error ? Problem(error) : TypedResults.Ok(result.Value);

    public static Results<Created<T>, ProblemHttpResult> Created<T>(
        Result<T> result,
        string location
    ) => result.Error is { } error ? Problem(error) : TypedResults.Created(location, result.Value);

    public static Results<Ok<T>, NotFound> Found<T>(T? value)
        where T : class => value is null ? TypedResults.NotFound() : TypedResults.Ok(value);

    private static ProblemHttpResult Problem(Error error) =>
        TypedResults.Problem(
            detail: error.Message,
            statusCode: error.Kind switch
            {
                ErrorKind.Validation => StatusCodes.Status400BadRequest,
                ErrorKind.NotFound => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status409Conflict,
            }
        );

    /// <summary>The refusals every write may answer with.</summary>
    public static RouteHandlerBuilder ProducesRefusals(this RouteHandlerBuilder builder) =>
        builder
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    /// <summary>The row version a write answers with (the new xmin; send it with the next write).</summary>
    [UsedImplicitly(ImplicitUseTargetFlags.Members)] // Serialized into the API response.
    public sealed record Saved(uint RowVersion);

    public static Result<Saved> AsSaved(this Result<uint> result) =>
        result.IsSuccess ? new Saved(result.Value) : result.Error!;
}
