using System.Text.Json.Serialization;
using Rockets.Api;
using Rockets.Application;
using Rockets.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRocketsInfrastructure();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.Services.EnsureRocketsInfrastructureReady();

app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }));

app.MapPost("/messages", async (
    RocketMessageEnvelope envelope, RocketMessageGateway gateway, CancellationToken cancellationToken) =>
{
    var result = await gateway.SubmitAsync(
        envelope.Metadata.Channel,
        envelope.Metadata.MessageNumber,
        envelope.Metadata.MessageTime,
        envelope.Metadata.MessageType,
        envelope.Message.GetRawText(),
        cancellationToken);

    return result.IsFailure
        ? Results.BadRequest(new { error = result.Error })
        : Results.Accepted();
});

app.MapGet("/rockets", async (RocketQueryService queries, CancellationToken cancellationToken) =>
{
    var result = await queries.GetAllAsync(cancellationToken);
    return result.IsFailure
        ? Results.Problem(result.Error)
        : Results.Ok(result.Value);
});

app.MapGet("/rockets/{channel}", async (
    string channel, RocketQueryService queries, CancellationToken cancellationToken) =>
{
    var result = await queries.FindAsync(channel, cancellationToken);
    if (result.IsFailure)
    {
        return Results.Problem(result.Error);
    }

    return result.Value is { } snapshot ? Results.Ok(snapshot) : Results.NotFound();
});

app.Run();

public partial class Program
{
}
