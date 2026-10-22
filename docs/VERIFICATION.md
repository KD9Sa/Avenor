# Verification record

Observed on **22 October 2026 (Africa/Cairo)**. These are actual local results, not intended behavior or a claim of production certification.

## Initial environment

- Windows 10 x64, .NET SDK 10.0.401 / runtime 10.0.12.
- Node 24.21.0, npm 11.19.0.
- Docker CLI 29.8.2 and Compose 5.6.0 installed; engine initially unavailable.
- npm registry requires the executor's network escalation; direct sandbox requests time out.

## Automated checks

| Check | Observed result |
|---|---|
| `dotnet build Avenor.sln --no-restore` | PASS, 0 warnings, 0 errors |
| `dotnet test Avenor.sln --no-restore` | PASS, 25 tests, none skipped |
| `./scripts/verify.ps1` | PASS end to end: Release backend build/tests, Angular production build, frontend tests |
| `./scripts/dev.ps1 -NoInstall` | PASS, API and Angular served successfully; Ctrl+C stopped both ports (5080 and 4200) |
| Angular production build | PASS, strict templates/types; current bundle 348.09 kB raw / 88.59 kB estimated transfer |
| `npm test` | PASS, 10 tests, none skipped |
| `python scripts/smoke.py http://127.0.0.1:4200` | PASS, all eight workflow groups through the actual Angular proxy/API/database |
| `npm audit` | 0 reported vulnerabilities after targeted compatible build-tool patches |
| `dotnet list Avenor.sln package --vulnerable --include-transitive` | No vulnerable dependencies reported in all five projects |
| EF SQL Server `migrations has-pending-model-changes --no-build` | PASS, no changes since tracked InitialSchema migration |
| `docker compose config --quiet` with demonstration environment | PASS |
| `dotnet format whitespace Avenor.sln --no-restore` | PASS |
| README/security/docs local links and screenshot references | PASS, all targets exist |

Backend coverage: 14 domain cases, 10 SQLite relational/API integration cases, and one tenant-scoped SSE broadcaster case. Cases exercise state transitions, reopening/closed-ticket rules, assignment, SLA risk, HttpOnly sessions, CSRF, cross-tenant read/write/assignment/reply denial, customer ownership, private notes, stale updates, last-admin protection, account deactivation, per-user notifications, reporting, audit persistence, composite foreign keys, write guards, offline bootstrap and malformed input.

Frontend coverage: validation, transitions, SLA display, chart edge cases, identity/time formatting, latest-filter response ordering, independent request scopes and rejection of delayed responses from previous sessions/tenants. The frontend helper tests use Node's real test runner; they are not Angular component tests.

HTTP smoke coverage: anonymous rejection/CSRF, organization login, ticket creation, assignment, stale-version conflict, internal/public messages, first response, resolution, customer reopening, internal-note filtering, staff-role denial, cross-tenant reads/writes, CSV, audit, settings, transactional user creation/deactivation, notifications and logout. This test intentionally adds a labeled demo ticket and an inactive demo agent.

## Running application browser checks

The connected in-app browser exercised the real application with SQLite persistence. These are assisted browser checks, not a committed automated E2E suite.

- Administrator login/logout; populated overview and real chart data.
- Created ticket #AV-1089 through the dialog, assigned Jordan Lee, added a private note and public reply, and changed status to In progress. Reload preserved the data.
- Searched the displayed `#AV-1089` identifier; result count became one. Checked next/previous pages and High priority filtering.
- Saved RoundRobin assignment in workspace settings; success feedback and persisted settings confirmed.
- Changed reporting from 7 to 30 days; ticket count changed from 43 to 49 on the then-current demo dataset.
- Exported a real browser CSV download (`avenor-report-7days.csv`); inspected its header and persisted ticket row. The HTTP smoke independently verifies 30-day export.
- Created Taylor Ellis as a real customer; marked notifications read and observed zero unread; opened the persisted audit history.
- Signed in as Morgan Chen: only the customer navigation appeared; private note text/control counts were both zero; the public reply was visible and a customer reply persisted.
- Desktop at 1440×1000 and mobile at 390×844. Mobile detail and queue had no page-level horizontal overflow; the ticket table scrolled within its container. Opened/closed the navigation drawer.
- Native dialogs expose accessible names and browser focus containment; visible focus, reduced-motion CSS and semantic status labels are implemented. This is not an exhaustive WCAG audit.

Authentic JPEG screenshots are stored in `docs/assets/` and referenced relatively in README. The logo, architecture and design-system illustration are SVG documentation assets; component specimens are labeled as such. Screenshots have not been replaced with generated mockups.

## Corrections discovered during verification

Resolved duplicate opening messages, an EF assignment navigation consistency issue, SQL retry/explicit transaction incompatibility, malformed payload errors, contrast failures, incorrect mobile heading spacing, and stale asynchronous responses after session/filter changes. Additional regression cases cover relevant business/security behavior. Closed mobile navigation is removed from keyboard/accessibility visibility.

### Sidebar and screenshot correction · 22 October 2026

The original full-page desktop captures extended beyond the browser's 1000px viewport, while the fixed sidebar correctly covered only the viewport. This produced a misleading blank area beneath the navigation in the saved images. Recaptured overview, queue, ticket detail, reports and administration directly from the running app within the actual 1440×1000 viewport bounds. No sidebar pixels were fabricated or extended.

Also fixed a real short-screen issue: navigation content exceeded its viewport without an internal scrollbar. The sidebar now scrolls vertically and contains its overscroll. Browser geometry confirmed:

- Desktop 1440×1000: sidebar top 0, bottom 1000, both at page top and after scrolling the document to its bottom.
- Short desktop 1440×768: content height 953px; scrolling the sidebar brought the profile's bottom to 768px.
- Mobile drawer 390×667: scrolling brought the profile's bottom to 666.8px, within the visible viewport.
- Angular production build passes after the CSS change. No business logic or backend changes were made.

## Limits and unverified work

- **Docker engine unavailable:** CLI reported missing `//./pipe/docker_engine`; launching installed Docker Desktop did not bring up an engine. The images, SQL Server migration execution and complete container startup have **not** been run here. Configuration/model checks and SQLite integration tests do not establish SQL Server runtime compatibility.
- GitHub Actions contains application and SQL Server container jobs; no remote CI run or GitHub publication was performed.
- Local development uses explicitly selected SQLite. SQL Server is the required Docker/deployment provider; there is no silent production database fallback.
- SSE invalidations are in-process, SLA scanning runs every 30 seconds, and CSV export is capped at 10,000 records. Reporting is designed for portfolio/demo-scale datasets.
- SLAs use elapsed UTC time. Business hours are descriptive; holidays/working-hours calendars are not calculated. Chart buckets are explicitly UTC days.
- Compose is an HTTP loopback-only demonstration with seeded credentials and a development database account. A production deployment requires the documented TLS, secrets, trusted proxy, persistent keys, least-privileged database account and operations setup. Behind the current local Nginx proxy, login clients share one IP-based rate-limit bucket.
- Email ingestion/delivery, attachments, external SSO, billing, password recovery, public signup and multiple-replica event delivery are out of scope and have no misleading UI controls.
