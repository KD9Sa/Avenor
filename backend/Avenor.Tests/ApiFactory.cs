using Avenor.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Avenor.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string database = Path.Combine(Path.GetTempPath(), "avenor-test-" + Guid.NewGuid().ToString("N") + ".db");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "Sqlite", ["ConnectionStrings:DefaultConnection"] = $"Data Source={database};Pooling=False", ["SeedDemo"] = "true", ["DisableBackgroundWorkers"] = "true", ["DataProtection:KeyPath"] = Path.Combine(Path.GetTempPath(), "avenor-test-keys") }));
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(database))
            File.Delete(database);
    }
}
