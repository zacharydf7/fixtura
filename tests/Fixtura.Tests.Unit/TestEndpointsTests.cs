using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Fixtura.Tests.Unit;

public class TestEndpointsTests
{
    [Fact]
    public async Task AllowingAnonymousAsync_ShouldListEndpointsWithoutAuthorization_WhenTheAppHasNoFallbackPolicy()
    {
        Endpoint[] endpoints =
        [
            Route("/ping", ["GET"]),
            Route("/me", ["GET"], new AuthorizeAttribute()),
            Route("/audit", ["GET"], new AuthorizationPolicyBuilder().RequireRole("Auditor").Build()),
            Route("/claims", ["GET"], new RoleRequirementData())
        ];

        IReadOnlyList<string> anonymous = await AllowingAnonymousAsync(fallback: null, endpoints);

        Assert.Equal(["GET /ping"], anonymous);
    }

    [Fact]
    public async Task AllowingAnonymousAsync_ShouldListOnlyAllowAnonymous_WhenTheAppHasAFallbackPolicy()
    {
        Endpoint[] endpoints =
        [
            Route("/ping", ["GET"]),
            Route("/login", ["POST"], new AllowAnonymousAttribute()),
            Route("/signup", ["POST"], new AuthorizeAttribute(), new AllowAnonymousAttribute())
        ];
        AuthorizationPolicy fallback = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        IReadOnlyList<string> anonymous = await AllowingAnonymousAsync(fallback, endpoints);

        Assert.Equal(["POST /login", "POST /signup"], anonymous);
    }

    [Fact]
    public async Task AllowingAnonymousAsync_ShouldNameEachMethodOrAnyMethod_WhenGivenEndpointsWithSeveralOrNone()
    {
        Endpoint[] endpoints = [Route("items", ["POST", "GET"]), Route("/health", methods: null)];

        IReadOnlyList<string> anonymous = await AllowingAnonymousAsync(fallback: null, endpoints);

        Assert.Equal(["* /health", "GET /items", "POST /items"], anonymous);
    }

    // MVC makes an endpoint per action from one conventional route, each keeping the route's own text.
    [Fact]
    public async Task AllowingAnonymousAsync_ShouldNameEachAction_WhenActionsShareAConventionalRoute()
    {
        Endpoint[] endpoints = [Action("Home", "About"), Action("Home", "Contact")];

        IReadOnlyList<string> anonymous = await AllowingAnonymousAsync(fallback: null, endpoints);

        Assert.Equal(["* /Home/About/{id?}", "* /Home/Contact/{id?}"], anonymous);
    }

    private static async Task<IReadOnlyList<string>> AllowingAnonymousAsync(AuthorizationPolicy? fallback, Endpoint[] endpoints)
    {
        var services = new ServiceCollection();
        services.AddSingleton<EndpointDataSource>(new DefaultEndpointDataSource(endpoints));
        services.AddAuthorizationCore(options => options.FallbackPolicy = fallback);
        await using ServiceProvider provider = services.BuildServiceProvider();
        return await new TestEndpoints(() => Task.FromResult<IServiceProvider>(provider)).AllowingAnonymousAsync();
    }

    private static RouteEndpoint Route(string route, string[]? methods, params object[] metadata) =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse(route), 0,
            new EndpointMetadataCollection([.. methods is null ? [] : new[] { new HttpMethodMetadata(methods) }, .. metadata]), route);

    private static RouteEndpoint Action(string controller, string action) =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse("{controller=Home}/{action=Index}/{id?}", defaults: null,
            parameterPolicies: null, requiredValues: new { controller, action }), 0, EndpointMetadataCollection.Empty, action);

    // Authorization asked for through a requirement, as RequireAuthorization(new ...) adds it.
    private sealed class RoleRequirementData : IAuthorizationRequirementData
    {
        public IEnumerable<IAuthorizationRequirement> GetRequirements() => [new RolesAuthorizationRequirement(["Auditor"])];
    }
}
