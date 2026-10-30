# Test users

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#test-users)]

A `TestUser` has a name, roles, claims and, optionally, an id. Each `With...` call returns a copy, so a base user can be
shared and extended.

The user signs in through the app's own authentication handler, so the app's token or cookie events and account checks
still run. Choose how with `host.Users.SignIn`:

| `UserSignIn` | How |
| --- | --- |
| `AppJwtBearer` (default) | A bearer token signed with the app's own `JwtBearerOptions` key, issuer and audience. Apps that trust an `Authority` get a test signing key, so no metadata request leaves the process. |
| `AppCookie` | An authentication cookie protected with the app's own cookie options, the one its login page would issue. The app's security-stamp check runs on the first request, and its sign-out works. |

Claim types come from the app's own options, so the principal reads the same whether or not the app maps inbound
claims.

## Apps with a user store

Apps that check each request against their user store, as ASP.NET Identity's security-stamp check does, need the user
to exist there. Register a seeder:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#cookie-sign-in)]

The seeder runs before each sign-in, one at a time per fixture. `IdentityUserSeeder<TUser, TRole>`:

- creates the Identity user, or finds the existing one, by id when the test gave one and by name otherwise;
- gives it the test user's roles and claims, creating missing roles;
- removes roles and claims an earlier sign-in added that this one doesn't ask for, so a user one test made a `Manager`
  is a plain user again in the next. Roles and claims the app gave the user stay;
- returns the claims Identity signs in.

Override `NewUser` to set the app's own fields on a user it creates, such as a status the app checks. Identity users
keyed by something other than a string take their id from `TestUser.Id` there too.

## Any other store

Implement `ITestUserSeeder` and return the claims the app's own sign-in would issue:

[!code-csharp[](../samples/Bookshop.Tests/GuideSnippets.cs#custom-seeder)]

A seeder is created from the app's services for each sign-in, so its constructor can take any of them.
