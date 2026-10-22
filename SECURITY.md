# Security model and deployment boundaries

Avenor is a portfolio implementation with an explicitly enabled local demonstration. Do not expose seeded accounts or the local HTTP Compose environment publicly.

## Implemented boundaries

- Authentication cookies are HttpOnly and SameSite; production uses secure cookies.
- ASP.NET antiforgery tokens protect mutations, including sign-in/sign-out.
- Passwords use ASP.NET's password hasher; hashes do not appear in API responses.
- Authenticated tenant claims, query filters, ownership/role checks, write validation and composite tenant foreign keys enforce isolation.
- Ticket versions detect stale mutations. Audit history is append-only through the application.
- Customer responses exclude internal messages. SSE notifications are tenant-scoped invalidations, not unrestricted ticket payloads.
- Login has rate limiting; input sizes and enum values are validated; errors use ProblemDetails.

## Production configuration

1. Use `ASPNETCORE_ENVIRONMENT=Production` and `SeedDemo=false`. Provision organizations/accounts through a controlled operational process; there is no public onboarding endpoint.
2. Terminate HTTPS at a trusted reverse proxy. Configure forwarded headers for only the proxies you control. Never disable secure cookies to support an Internet-facing HTTP deployment.
3. Store connection strings in an appropriate secret store. Use a dedicated least-privileged database account; the local Compose `sa` account is for development only. Replace development certificate trust with valid SQL Server TLS validation.
4. Persist/protect ASP.NET data-protection keys and share them only among trusted replicas of this app. Treat the SQL database and backups as private.
5. Configure logging, retention, backups, restore drills, resource limits and dependency updates for the deployment. An audit table is not tamper-proof against a database administrator.
6. Before multiple API replicas, provide shared event delivery. Current live invalidations use an in-process broadcaster.

Read [docs/VERIFICATION.md](docs/VERIFICATION.md) for exact verification and limits. Repository code and test results are not a third-party penetration test or a production certification.

Report security issues privately to the repository maintainer; do not post credentials or private customer data in public issues.
