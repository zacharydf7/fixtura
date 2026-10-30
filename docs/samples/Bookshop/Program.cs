using System.Security.Claims;
using System.Text;
using Bookshop;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ShopDb>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("Shop")));
builder.Services.AddHttpClient<ShippingClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Shipping:Url"]!));
builder.Services.Configure<ShopOptions>(builder.Configuration.GetSection("Shop"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<RestockReminder>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
    };
});
builder.Services.AddAuthorization();

WebApplication app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ShopDb>().Database.EnsureCreated();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/books", (ShopDb db) => db.Books.OrderBy(book => book.Id).ToListAsync());

app.MapPost("/orders", async (PlaceOrder request, ClaimsPrincipal user, ShopDb db, ShippingClient shipping,
    IOptions<ShopOptions> shop, TimeProvider clock, ILogger<Program> logger) =>
{
    Book? book = await db.Books.FindAsync(request.BookId);
    if (book is null)
    {
        return Results.NotFound();
    }

    decimal cost = book.Price >= shop.Value.FreeShippingOver ? 0 : await shipping.QuoteAsync(book.Id);
    var order = new Order { Customer = user.Identity!.Name!, BookId = book.Id, Shipping = cost, PlacedAt = clock.GetUtcNow() };
    db.Orders.Add(order);
    await db.SaveChangesAsync();
    logger.LogInformation("Order {OrderId} placed for {Customer}.", order.Id, order.Customer);
    return Results.Created($"/orders/{order.Id}", order);
}).RequireAuthorization(policy => policy.RequireRole("Customer"));

app.MapGet("/orders/mine", (ClaimsPrincipal user, ShopDb db) =>
    db.Orders.Where(order => order.Customer == user.Identity!.Name).ToListAsync()).RequireAuthorization();

app.Run();
