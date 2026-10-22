# Provision a non-demo organization

The offline bootstrap mode creates one organization, its first administrator, default SLA policies and an audit entry in a transaction, then exits without starting an HTTP server. It refuses an existing slug. Use it with database credentials held by a trusted operator; it is not a public endpoint.

First configure `Database__Provider=SqlServer`, `ConnectionStrings__DefaultConnection` and `ASPNETCORE_ENVIRONMENT=Production` for the intended database. Keep secrets outside source control. The connection must permit applying the tracked migration and creating the initial records.

In PowerShell 7:

```powershell
$env:SeedDemo = 'false'
$env:Bootstrap__OrganizationName = 'Your organization'
$env:Bootstrap__OrganizationSlug = 'your-organization'
$env:Bootstrap__AdminName = 'Workspace administrator'
$env:Bootstrap__AdminEmail = 'admin@example.com'
$env:Bootstrap__AdminPassword = Read-Host 'Initial password (16+ characters, upper/lowercase, number, symbol)' -MaskInput
try {
    dotnet run --no-launch-profile --project backend/Avenor.Api -- --bootstrap true
    if ($LASTEXITCODE -ne 0) { throw 'Bootstrap failed; inspect the reported validation error.' }
} finally {
    Remove-Item Env:Bootstrap__AdminPassword
}
```

Start the API normally, without `--bootstrap true`, after the command succeeds. Further user accounts can be created by the administrator in the workspace. There is no email delivery or password recovery flow; use an appropriate secure out-of-band channel for initial credentials.

The local Compose file is a separate seeded demonstration and must be adapted for a non-demo deployment; see [SECURITY.md](../SECURITY.md). In a real reverse-proxy deployment, configure a trusted proxy explicitly so login rate limits can distinguish client addresses. The local Compose demo currently shares one proxy address for its login limit.
