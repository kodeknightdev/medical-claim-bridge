using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimBridge.ServiceDefaults;

public static class HealthCheckExtensions
{
    public static IHealthChecksBuilder AddSqlServerReadiness(this IHealthChecksBuilder builder, IConfiguration config, string connectionName)
    {
        var cs = config.GetConnectionString(connectionName);
        return string.IsNullOrWhiteSpace(cs)
            ? builder
            : builder.AddSqlServer(cs, name: "sqlserver", tags: [ServiceDefaultsExtensions.ReadyTag]);
    }
}
