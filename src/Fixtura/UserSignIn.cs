namespace Fixtura;

/// <summary>How a test user reaches the app. Both kinds go through the app's own authentication handler and events.</summary>
public enum UserSignIn
{
    /// <summary>
    /// A bearer token signed with the app's own key, issuer and audience, or with a test key for apps that trust an
    /// Authority.
    /// </summary>
    AppJwtBearer,

    /// <summary>
    /// An authentication cookie protected with the app's own cookie options, the same one its login page would issue.
    /// </summary>
    AppCookie
}
