using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CookieIdentityApp;

// A cookie app on ASP.NET Identity, whose security-stamp validator checks every cookie against the user store.
public class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Services.AddDbContext<AccountsDbContext>(options =>
            options.UseSqlServer("Server=sql.sample.invalid;Database=SampleAccounts;Integrated Security=true"));
        builder.Services.AddIdentity<IdentityUser, IdentityRole>().AddEntityFrameworkStores<AccountsDbContext>();
        builder.Services.AddAuthorization();

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/me", (ClaimsPrincipal user) => new
        {
            user.Identity!.Name,
            Roles = user.FindAll(ClaimTypes.Role).Select(claim => claim.Value)
        }).RequireAuthorization(policy => policy.RequireRole("Auditor"));
        app.MapPost("/logout", async (SignInManager<IdentityUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
        app.Run();
    }
}

public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : IdentityDbContext(options);
