using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace AuthorityJwtApp;

// Trusts an authority whose host never resolves and holds no key of its own, so any metadata request would fail.
public class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.Authority = "https://login.sample.invalid/tenant";
            options.Audience = "authority-api";
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    ((ClaimsIdentity)context.Principal!.Identity!).AddClaim(new Claim("checked-by", "app"));
                    return Task.CompletedTask;
                }
            };
        });
        builder.Services.AddAuthorization();

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/me", (ClaimsPrincipal user) => new
        {
            user.Identity!.Name,
            Roles = user.FindAll(ClaimTypes.Role).Select(claim => claim.Value),
            CheckedBy = user.FindFirst("checked-by")?.Value
        }).RequireAuthorization(policy => policy.RequireRole("Auditor"));
        app.Run();
    }
}
