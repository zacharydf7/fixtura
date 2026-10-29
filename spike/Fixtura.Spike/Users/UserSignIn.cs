namespace Fixtura.Spike.Users;

// How a test user reaches the app. Both go through the app's own authentication handler and its events.
public enum UserSignIn
{
    // A bearer token signed with the app's own key, issuer and audience, or a test key for apps that trust an Authority.
    AppJwtBearer,

    // An authentication cookie protected with the app's own cookie options, the same one its login page would issue.
    AppCookie
}
