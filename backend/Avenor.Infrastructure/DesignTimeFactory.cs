using Avenor.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Avenor.Infrastructure;

// Design-time schema generation never connects to a database or reads real credentials.
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AvenorDbContext>
{
    public AvenorDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AvenorDbContext>().UseSqlServer("Server=localhost;Database=AvenorDesignTime;Integrated Security=true;TrustServerCertificate=true").Options, new DesignTenant());
    private sealed class DesignTenant : ITenantContext
    {
        public Guid? TenantId => null; public Guid? UserId => null; public string Role => ""; public string UserName => "Migration"; public bool IsCustomer => false;
    }
}
