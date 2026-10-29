using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace SymmetricJwtApp;

// A JWT API with a symmetric key, the default claim mapping and an OnTokenValidated event that adds a claim.
public class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = "https://symmetric.sample.invalid",
                ValidAudience = "symmetric-api",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("symmetric-sample-key-long-enough-for-hmac-sha256"))
            };
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
