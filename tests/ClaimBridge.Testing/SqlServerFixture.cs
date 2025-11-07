using Testcontainers.MsSql;
using Xunit;

namespace ClaimBridge.Testing;

/// <summary>One SQL Server 2022 container per test collection; each test class gets its own database name.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string Image = "mcr.microsoft.com/mssql/server:2022-CU17-ubuntu-22.04";
    private MsSqlContainer? _container;

    // the last Database= in a SQL Server connection string wins
    public string ConnectionString(string database) => _container!.GetConnectionString() + $";Database={database}";

    public async Task InitializeAsync()
    {
        if (!DockerAvailability.IsAvailable) return;
        _container = new MsSqlBuilder().WithImage(Image).Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
