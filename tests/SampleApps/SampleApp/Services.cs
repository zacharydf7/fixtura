using Microsoft.AspNetCore.Mvc;

namespace SampleApp;

public sealed record OrderPlaced(int OrderId, string Product, int Quantity);

public interface IOrderPublisher
{
    Task PublishAsync(OrderPlaced message);
}

// Stands in for a message-bus client: it can't work without a broker, so tests must replace it.
public sealed class BusOrderPublisher : IOrderPublisher
{
    public Task PublishAsync(OrderPlaced message) =>
        throw new InvalidOperationException("No message broker is configured.");
}

public static class PaymentMethods
{
    public const string Card = "card";
    public const string Invoice = "invoice";
}

public interface IPaymentGateway
{
    string Name { get; }
}

public sealed class CardGateway : IPaymentGateway
{
    public string Name => "Card processor";
}

public sealed class InvoiceGateway : IPaymentGateway
{
    public string Name => "Invoicing";
}

public sealed class ShippingOptions
{
    public List<Carrier> Carriers { get; set; } = [];
}

public sealed class Carrier
{
    public string Name { get; set; } = "";

    public string TrackingUrl { get; set; } = "";
}

// Registered with AddHostedService<T>().
public sealed class OutboxWorker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}

// Registered with the AddHostedService factory overload.
public sealed class ReportScheduler : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}

public enum ParcelStatus
{
    Waiting,
    InTransit,
    Delivered
}

public sealed record Parcel(string Number, ParcelStatus Status);

public sealed record ReturnRequest(string Reason);

public sealed record ParcelLabel(string ParcelNumber, string CarrierName);

public sealed class LabelRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public string? ParcelNumber { get; set; }

    [System.ComponentModel.DataAnnotations.Required]
    public string? CarrierName { get; set; }
}

[ApiController]
[Route("labels")]
public sealed class LabelsController : ControllerBase
{
    [HttpGet("{number}")]
    public ParcelLabel Get(string number) => new(number, "Ground");

    [HttpPost]
    public ParcelLabel Create(LabelRequest request) => new(request.ParcelNumber!, request.CarrierName!);
}
