namespace Fixtura;

/// <summary>How a test user reaches the app. Both kinds go through the app's own authentication handler and events.</summary>
public enum UserSignIn
{
    /// <summary>
    /// A bearer token signed with the app's own key, issuer and audience, or with a test key the app trusts too when it
    /// has no key a test can sign with: it trusts an Authority, or pins only public keys.
    /// </summary>
    AppJwtBearer,

    /// <summary>
    /// An authentication cookie protected with the app's own cookie options, the same one its login page would issue.
    /// </summary>
    AppCookie
}
