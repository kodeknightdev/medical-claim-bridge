using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClaimBridge.ServiceDefaults.Data;

public static class DatabaseStartup
{
    /// <summary>Applies pending migrations (and optional seeding) with a retry, since SQL Server is often still booting in compose.</summary>
    public static async Task MigrateAsync<TContext>(this WebApplication app,
        Func<IServiceProvider, TContext, Task>? seed = null, int attempts = 20) where TContext : DbContext
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TContext>();
                if (db.Database.IsRelational())
                    await db.Database.MigrateAsync();
                else
                    await db.Database.EnsureCreatedAsync();
                if (seed is not null) await seed(scope.ServiceProvider, db);
                logger.LogInformation("Database {Context} ready", typeof(TContext).Name);
                return;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                logger.LogWarning("Database {Context} not ready (attempt {Attempt}): {Error}", typeof(TContext).Name, attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(3 * attempt, 15)));
            }
        }
    }
}
