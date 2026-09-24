using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Identity.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MyWorkplace.Identity.Tests;

/// <summary>
/// EN: The AddRolesAndPermissions data migration (T-025): users created before roles existed become Owner. Integration
///     tests start from an empty database and can't see this; only an upgrade of an existing database can.
/// TR: AddRolesAndPermissions veri göçü (T-025): roller gelmeden önce oluşturulan kullanıcılar Sahip olur. Entegrasyon testleri
///     boş veritabanıyla başladığı için bunu göremez; sadece mevcut bir veritabanının yükseltilmesi görebilir.
/// </summary>
public sealed class RolesMigrationTests : IAsyncLifetime
{
    /// <summary>EN: The last migration before roles. TR: Rollerden önceki son migration.</summary>
    private const string BeforeRoles = "20260923211527_AddSigningKeys";

    /// <summary>EN: Throw-away database. TR: Geçici veritabanı.</summary>
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresImage.Reference).Build();

    /// <inheritdoc />
    public ValueTask InitializeAsync() => new(_container.StartAsync());

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _container.DisposeAsync();

    [Fact]
    public async Task ExistingUsers_BecomeOwner_WhenRolesAreIntroduced()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var context = CreateContext();
        var migrator = context.GetService<IMigrator>();

        // EN: 1) A database as it was before T-025, with a user who has no roles column yet.
        // TR: 1) T-025'ten önceki haliyle bir veritabanı ve henüz rol sütunu olmayan bir kullanıcı.
        await migrator.MigrateAsync(BeforeRoles, ct);
        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        await ExecuteAsync(
            "insert into tenants (id, name, plan, created_at) values (@tenant, 'Old Co', 'Basic', now());" +
            "insert into users (id, email, normalized_email, password_hash, tenant_id, created_at) " +
            "values (@user, 'old@example.com', 'old@example.com', 'hash', @tenant, now());",
            tenantId, userId, ct);

        // EN: 2) Upgrade to the latest schema. TR: 2) En son şemaya yükselt.
        await migrator.MigrateAsync(cancellationToken: ct);

        // EN: 3) The pre-existing user owns their company. TR: 3) Önceden var olan kullanıcı firmasının sahibidir.
        var roles = await QueryRolesAsync(userId, ct);
        Assert.Equal(["Owner"], roles);
    }

    /// <summary>
    /// EN: A context configured exactly like the service's, against the test container.
    /// TR: Test konteynerine karşı, servisinkiyle birebir aynı yapılandırılmış bir context.
    /// </summary>
    /// <returns>EN: The context. TR: Context.</returns>
    private IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>();
        options.UseServiceConventions(_container.GetConnectionString());
        return new IdentityDbContext(options.Options, new AnonymousCurrentUser());
    }

    /// <summary>
    /// EN: Runs SQL with the @tenant and @user parameters.
    /// TR: @tenant ve @user parametreleriyle SQL çalıştırır.
    /// </summary>
    /// <param name="sql">EN: The SQL. TR: SQL.</param>
    /// <param name="tenantId">EN: Tenant id. TR: Firma kimliği.</param>
    /// <param name="userId">EN: User id. TR: Kullanıcı kimliği.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: A task. TR: Görev.</returns>
    private async Task ExecuteAsync(string sql, Guid tenantId, Guid userId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("user", userId);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// EN: Reads a user's roles straight from the table.
    /// TR: Bir kullanıcının rollerini doğrudan tablodan okur.
    /// </summary>
    /// <param name="userId">EN: User id. TR: Kullanıcı kimliği.</param>
    /// <param name="ct">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The roles. TR: Roller.</returns>
    private async Task<string[]> QueryRolesAsync(Guid userId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("select roles from users where id = @user", connection);
        command.Parameters.AddWithValue("user", userId);
        return (string[])(await command.ExecuteScalarAsync(ct))!;
    }
}
