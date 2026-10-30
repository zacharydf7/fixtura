namespace Fixtura;

// What IdentityUserSeeder added to each stored user, by user id, so a later sign-in can take back what it no longer asks
// for. Seeding runs one user at a time per fixture, so the sets need no lock.
internal sealed class SeederAdditions
{
    private readonly Dictionary<string, HashSet<string>> _roles = [];
    private readonly Dictionary<string, HashSet<(string Type, string Value)>> _claims = [];

    public HashSet<string> RolesOf(string userId) => SetFor(_roles, userId, StringComparer.OrdinalIgnoreCase);

    public HashSet<(string Type, string Value)> ClaimsOf(string userId) => SetFor(_claims, userId);

    private static HashSet<T> SetFor<T>(Dictionary<string, HashSet<T>> sets, string userId,
        IEqualityComparer<T>? comparer = null)
    {
        if (!sets.TryGetValue(userId, out HashSet<T>? set))
        {
            set = new HashSet<T>(comparer);
            sets[userId] = set;
        }

        return set;
    }
}
