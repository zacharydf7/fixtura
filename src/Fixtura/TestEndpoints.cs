using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Fixtura;

/// <summary>The app's endpoints, as routing has them once the host has started.</summary>
public sealed class TestEndpoints
{
    private readonly Func<Task<IServiceProvider>> _services;

    internal TestEndpoints(Func<Task<IServiceProvider>> services)
    {
        _services = services;
    }

    /// <summary>
    /// Lists the endpoints a request reaches without signing in, sorted, each as its method and route, such as
    /// <c>GET /ping</c>, or <c>* /health</c> when it takes any method. That is an endpoint with <c>AllowAnonymous</c>, and,
    /// unless the app sets a fallback policy, one with no authorization of its own. One test that compares the list to an
    /// allowlist fails when a new endpoint forgets <c>RequireAuthorization</c>. It reads the endpoints' metadata, so a
    /// check made inside a handler doesn't count.
    /// </summary>
    public async Task<IReadOnlyList<string>> AllowingAnonymousAsync()
    {
        IServiceProvider services = await _services();
        IAuthorizationPolicyProvider? policies = services.GetService<IAuthorizationPolicyProvider>();
        AuthorizationPolicy? fallback = policies is null ? null : await policies.GetFallbackPolicyAsync();
        List<string> anonymous = [];
        foreach (RouteEndpoint endpoint in services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            if (AllowsAnonymous(endpoint.Metadata, fallback))
            {
                string route = RouteOf(endpoint.RoutePattern);
                IReadOnlyList<string> methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
                anonymous.AddRange(methods.Count == 0 ? [$"* {route}"] : methods.Select(method => $"{method} {route}"));
            }
        }

        return [.. anonymous.Distinct().Order(StringComparer.Ordinal)];
    }

    // MVC gives each action of a conventional route the route's own text, so those are written with the action's values,
    // such as /Home/About/{id?}, or every action would read the same.
    private static string RouteOf(RoutePattern pattern)
    {
        if (!pattern.Parameters.Any(parameter => pattern.RequiredValues.GetValueOrDefault(parameter.Name) is string))
        {
            return "/" + pattern.RawText?.TrimStart('/');
        }

        List<string> segments = [];
        foreach (RoutePatternPathSegment segment in pattern.PathSegments)
        {
            string text = "";
            foreach (RoutePatternPart part in segment.Parts)
            {
                text += TextOf(part, pattern);
            }

            segments.Add(text);
        }

        return "/" + string.Join("/", segments);
    }

    private static string TextOf(RoutePatternPart part, RoutePattern pattern) => part switch
    {
        RoutePatternParameterPart parameter when pattern.RequiredValues.GetValueOrDefault(parameter.Name) is string value =>
            value,
        RoutePatternParameterPart parameter => "{" + parameter.Name + (parameter.IsOptional ? "?" : "") + "}",
        RoutePatternLiteralPart literal => literal.Content,
        RoutePatternSeparatorPart separator => separator.Content,
        _ => ""
    };

    // As the authorization middleware decides: AllowAnonymous wins, and otherwise the fallback policy covers an endpoint
    // that asks for no authorization of its own.
    private static bool AllowsAnonymous(EndpointMetadataCollection metadata, AuthorizationPolicy? fallback) =>
        metadata.GetMetadata<IAllowAnonymous>() is not null
        || (fallback is null
            && metadata.GetMetadata<IAuthorizeData>() is null
            && metadata.GetMetadata<AuthorizationPolicy>() is null
            && metadata.GetMetadata<IAuthorizationRequirementData>() is null);
}
