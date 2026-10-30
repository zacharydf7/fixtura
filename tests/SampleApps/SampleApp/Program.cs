using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SampleApp;
using SampleApp.Auth;
using SampleApp.Data;
using SampleApp.Inventory;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Read before Build(), so a test can only change it through settings passed as arguments.
bool reportsEnabled = builder.Configuration.GetValue<bool>("Features:Reports");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AuditInterceptor>();

builder.Services.AddDbContext<AppIdentityDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Identity")));
// appsettings.json has no Orders connection string on purpose: it comes from the environment, so this lambda throws
// without one.
builder.Services.AddDbContext<OrdersDbContext>((services, options) => options
    .UseSqlServer(services.GetRequiredService<IConfiguration>().GetConnectionString("Orders")
        ?? throw new InvalidOperationException("ConnectionStrings:Orders is not set."))
    .AddInterceptors(services.GetRequiredService<AuditInterceptor>()));
builder.Services.AddDbContextPool<CatalogDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Catalog")));
builder.Services.AddDbContextFactory<ReportsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Reports")));
builder.Services.AddPooledDbContextFactory<AnalyticsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Analytics")));
builder.Services.AddInventory(builder.Configuration.GetConnectionString("Inventory")!);

builder.Services.AddScoped<IOrderPublisher, BusOrderPublisher>();
builder.Services.AddKeyedSingleton<IPaymentGateway, CardGateway>(PaymentMethods.Card);
builder.Services.AddKeyedSingleton<IPaymentGateway, InvoiceGateway>(PaymentMethods.Invoice);
builder.Services.Configure<ShippingOptions>(builder.Configuration.GetSection("Shipping"));
builder.Services.AddHttpClient("rates", client => client.BaseAddress = new Uri("https://rates.sample.test/"))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    .AddHttpMessageHandler(() => new ApiKeyHandler());
builder.Services.AddHttpClient<TrackingClient>(client => client.BaseAddress = new Uri("https://tracking.sample.test/"));
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService(_ => new ReportScheduler());

builder.Services.AddIdentityCore<AppUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<AppIdentityDbContext>();
builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, JwtSetup>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddAuthorization();

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/me", (HttpContext context) => new
{
    context.User.Identity!.Name,
    Roles = context.User.FindAll("role").Select(claim => claim.Value)
}).RequireAuthorization();

app.MapPost("/orders", async (PlaceOrderRequest request, OrdersDbContext db, IOrderPublisher publisher) =>
{
    var order = new Order { Product = request.Product, Quantity = request.Quantity };
    db.Orders.Add(order);
    await db.SaveChangesAsync();
    await publisher.PublishAsync(new OrderPlaced(order.Id, order.Product, order.Quantity));
    return Results.Created($"/orders/{order.Id}", order);
}).RequireAuthorization(policy => policy.RequireRole("Manager"));

app.MapGet("/ping", (ILogger<Program> logger) =>
{
    logger.LogInformation("Pinged.");
    return "pong";
});

app.MapGet("/carriers", (IOptions<ShippingOptions> shipping) => shipping.Value.Carriers.Select(carrier => carrier.Name));

app.MapGet("/payments/{method}", (string method, IServiceProvider services) =>
    services.GetRequiredKeyedService<IPaymentGateway>(method).Name);

app.MapGet("/rates/{currency}", (string currency, IHttpClientFactory http) =>
    http.CreateClient("rates").GetFromJsonAsync<Rate>($"today/{currency}"));

app.MapPost("/parcels/{number}/track", (string number, TrackingClient tracking) => tracking.TrackAsync(number));

if (reportsEnabled)
{
    app.MapGet("/reports", () => "Reports are enabled.");
}

app.Run();

public sealed record PlaceOrderRequest(string Product, int Quantity);

public partial class Program;
