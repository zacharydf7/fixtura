using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SampleApp.Data;

namespace SampleApp.Auth;

public static class SampleClaims
{
    public const string SecurityStamp = "stamp";
}

// Configures the bearer scheme through the options system from the Jwt section, the way most apps do.
public sealed class JwtSetup(IConfiguration configuration) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        IConfigurationSection jwt = configuration.GetSection("Jwt");
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SigningKey"]!)),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents { OnTokenValidated = CheckAccountAsync };
    }

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);

    // Rejects tokens for deleted users and tokens issued before the user's security stamp changed.
    private static async Task CheckAccountAsync(TokenValidatedContext context)
    {
        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        AppUser? user = await users.FindByIdAsync(context.Principal!.FindFirst("sub")?.Value ?? "");
        if (user is null || user.SecurityStamp != context.Principal.FindFirst(SampleClaims.SecurityStamp)?.Value)
        {
            context.Fail("The account no longer matches this token.");
        }
    }
}
