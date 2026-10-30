using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Fixtura;

// Puts one fake clock everywhere the app reads the time: its TimeProvider and the clocks its JWT and cookie handlers keep.
internal static class FakeClock
{
    public static void AddTo(IServiceCollection services, FakeTimeProvider clock)
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(clock);
        services.PostConfigureAll<CookieAuthenticationOptions>(options => options.TimeProvider = clock);
        services.PostConfigureAll<JwtBearerOptions>(options =>
        {
            options.TimeProvider = clock;
            UseForLifetimes(options.TokenValidationParameters, clock);
        });
    }

    // IdentityModel checks a token's lifetime against the system clock unless the app validates lifetimes itself. It calls
    // this check even when lifetimes aren't validated, so the check keeps the app's own rules.
    private static void UseForLifetimes(TokenValidationParameters parameters, FakeTimeProvider clock)
    {
        if (parameters.LifetimeValidator is not null)
        {
            return;
        }

        parameters.LifetimeValidator = (notBefore, expires, _, validation) =>
        {
            if (!validation.ValidateLifetime)
            {
                return true;
            }

            DateTime now = clock.GetUtcNow().UtcDateTime;
            return (expires is not null || !validation.RequireExpirationTime)
                && (notBefore is null || expires is null || notBefore <= expires)
                && (notBefore is null || notBefore <= now + validation.ClockSkew)
                && (expires is null || expires >= now - validation.ClockSkew);
        };
    }
}
