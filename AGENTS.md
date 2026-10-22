# Avenor engineering conventions

## Scope and architecture
- Angular standalone components and signals; ASP.NET Core 10 modular monolith; EF Core 10 with SQL Server in Docker. SQLite is an explicitly selected local development/test provider, never a silent production fallback.
- Keep domain rules in Domain, shared contracts/validation/tenant abstractions in Application, persistence in Infrastructure, and HTTP/authentication/use-case orchestration in Api endpoint groups. This is a pragmatic modular monolith, not a claim of a pure interactor-based Clean Architecture. Avoid speculative interfaces.
- All tenant-owned queries and writes must be scoped to the authenticated tenant. Never accept a tenant ID from a ticket mutation body. Customers may only access their own tickets; internal notes are staff-only.
- Use HttpOnly cookie authentication, antiforgery validation on mutations, ASP.NET password hashing, explicit role checks, validated DTOs, UTC timestamps and optimistic concurrency.
- No secrets in source. Seed demonstration organizations only when explicitly configured. Label demonstration data in the UI.

## Frontend and design
- Follow docs/DESIGN_SYSTEM.md and the API contract in docs/API.md. Use semantic HTML, keyboard-accessible forms/dialogs, visible focus, text alongside color, and responsive layouts.
- Every visible control must have working behavior. Exclude unsupported actions instead of presenting disabled placeholders. Handle loading, empty, error and success states.
- Use genuine API data; do not implement a separate browser mock database. Keep Angular strict template/type checking enabled.

## Verification and documentation
- Build each affected project. Test business rules, tenant boundaries, role permissions, CSRF protection and critical user workflows.
- Capture screenshots only from the running application. Keep assets in docs/assets and use relative links in README.md.
- Record actual commands/results and unverified limitations in docs/VERIFICATION.md. Never equate compilation or SQLite tests with SQL Server/container verification.
- Keep credentials, build outputs, node_modules, database files, browser state and test artifacts out of Git.
- Agents own their assigned files; coordinate contract changes before modifying another agent's work.
