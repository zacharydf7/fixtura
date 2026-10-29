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
