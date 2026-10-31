namespace Fixtura;

/// <summary>Which of the app's JSON options a fixture reads responses with.</summary>
public enum AppJson
{
    /// <summary>The options minimal APIs write with, set by <c>ConfigureHttpJsonOptions</c>.</summary>
    MinimalApis,

    /// <summary>The options MVC controllers write with, set by <c>AddControllers().AddJsonOptions(...)</c>.</summary>
    Controllers
}
