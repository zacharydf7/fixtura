using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SampleApp.Data;

// Stamps new orders, so a test can see whether a swapped context still runs the app's interceptor.
public sealed class AuditInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        foreach (var entry in eventData.Context!.ChangeTracker.Entries<Order>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.AuditedAt = clock.GetUtcNow();
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
