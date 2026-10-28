# Corrin Field Service

Field-service system for Corrin Utility Services (boiler and meter install, service and repair across six English regions). Holds customers, sites, assets, engineers (skills, regions, shifts), jobs with notes and parts, parts stock, a monthly engineer utilisation report, and an importer that turns the contact centre's CSV of job requests into jobs. On top of that sits the work-order layer (a work order is a `Job` row): allocation, scheduling, visits and outcomes, a first-time-fix report, a dispatcher board and manual parts reordering. The spec is `docs/specs/work-orders.md` (read its Delivery status section first).

## Stack
- Backend: ASP.NET Core Web API on .NET 8, controllers, EF Core 8 with Pomelo MySQL, FluentValidation, Swashbuckle, DbUp
- Database: MySQL 8.4 on port 3306, database `corrin`, app user `corrin_app`
- Frontend: React 19, react-router-dom 7, Vite 6, TypeScript, plain `fetch` wrapper (no Redux, no axios)
- Tests: xUnit and Moq in `backend/Corrin.Tests`

## Layout
- `backend/Corrin.Api/` `Controllers`, `Services`, `Repositories`, `Domain` (entities), `Dtos`, `Mappers`, `Validators`, `Infrastructure`, `Data` (`CorrinDbContext`)
- `backend/db/migrations/` numbered SQL scripts, embedded in the API assembly
- `backend/Corrin.Tests/` service tests (mocked repositories) and API tests (`WebApplicationFactory<Program>`)
- `frontend/src/` `api` (typed fetch clients), `pages`, `components`
- `db-setup/` one-off MySQL admin scripts; `tools/mysql-mcp/` read-only MySQL MCP server; `docs/` specs, review checklist, notes

## Commands
- Create DB and user (once): `mysql -h 127.0.0.1 -P 3306 -u root -p < db-setup/01-create-users.sql`
- Or start MySQL only: `docker compose up mysql`
- Run API: `dotnet run --project backend/Corrin.Api` (http://localhost:5080, Swagger at `/swagger`)
- Run tests: `dotnet test backend/Corrin.sln` (needs MySQL running; the API tests use the real seeded database)
- Test MCP SQL guard: `cd tools/mysql-mcp && npm test` (no database needed; see `docs/mcp-security.md`)
- Run web: `cd frontend && npm install && npm run dev` (http://localhost:5173, proxies `/api` to :5080)
- Build web: `cd frontend && npm run build` (there is no frontend test script)
- Other MySQL host: set `ConnectionStrings__Default` before running the API or tests
- Review session history and suggest skills/agents/hooks: `/reflect` (writes `docs/ai-usage-reflection.md`)

## Conventions
- Flow is Controllers, then Services, then Repositories, with DTOs and mappers at the edge. Controllers stay thin; business rules live in services; repositories only query and save entities. Controllers never see entities.
- Each table has an `I*Repository`/`*Repository` and `I*Service`/`*Service` pair, registered by hand in `Program.cs`.
- Mapping is hand-written extension methods in `Mappers/Mappers.cs` (`ToDto`, `Apply`); there is no AutoMapper.
- Errors: services throw `NotFoundException` (404), `ConflictException` (409) or `BusinessRuleException` (422); `ApiExceptionHandler` returns them as ProblemDetails. Request validation is FluentValidation in `Validators/Validators.cs`, run by `ValidationFilter`, returning 400.
- Schema changes: add the next numbered SQL file in `backend/db/migrations/` (DbUp runs them at API startup). Never edit an existing migration. Keep the SQL, the EF model and the validators in agreement on length, nullability and indexes. EF maps names to snake_case in `OnModelCreating`.
- Timestamps are mostly `DateTime.UtcNow` (jobs, notes); `ShiftService` and `EngineerService` use `DateTime.Now`. Be deliberate about which you use.
- JSON is camelCase. List endpoints are paged (`page`, `pageSize` clamped to 1-100).
- Tests: API tests use `[Collection("api")]` with `TestApiFactory`; service tests mock repositories with Moq. DB-backed repository tests insert rows inside a transaction and roll back (see `FirstTimeFixRepositoryTests`); never leave data in the seeded database.

## Work orders
- Entry points: `WorkOrdersController` (list, detail, status, candidates, allocate, schedule, next-slot, visits), `DispatchController` (board), `ReorderRequestsController`, and `GET /api/reports/first-time-fix`. UI routes: `/work-orders`, `/work-orders/:id`, `/dispatch`, `/reports/first-time-fix`, `/reorder`.
- Statuses are stored as `Requested`, `Triaged`, `Allocated`, `Scheduled`, `OnSite`, `Completed`, `NotCompleted`, `Cancelled` (`Domain/WorkOrderStatus.cs`; the UI labels `Requested` as "Raised"). Allocate, schedule and the visit endpoints move a job into `Allocated`, `Scheduled`, `OnSite` and `Completed`; `POST /status` only does Requested to Triaged and cancel.
- Safety rules, enforced in one place (`Services/EngineerBookingRules.cs`) and used by allocation, scheduling and the legacy `POST/PUT /api/jobs`: an engineer without the job type's required skill (or inactive) is always refused with 422, no override; a non-P1 booking that overlaps the engineer's other jobs is refused with 409. A P1 may displace overlapping jobs only through the schedule endpoint, never another P1 or a job already on site; displaced jobs go back to `Allocated` with a note starting `WorkOrderNotes.DisplacedByP1NotePrefix`, which the board reads to flag "needs rebooking".
- Concurrency: bookings take the engineer row lock, then the job row lock (`LockForUpdateAsync`), inside `JobRepository.InTransactionAsync`, which joins an open transaction. Keep that order. Stock changes use atomic conditional updates (`PartRepository.TryDecrementStockAsync`, `IncrementStockAsync`).
- First-time-fix is computed from `visits` (visit 1 fixed and no later visit), never from `Job.FirstTimeFix`. Migration 007 backfilled one visit per historical completed job, so history equals the old outcome flag; only new work records return visits.
- Times: scheduled times are UK wall-clock values; use `UkClock.Now()` for comparisons with them (it can be overridden in tests). Notes and audit timestamps stay UTC.
- Reorder requests are created automatically when recording parts takes stock to or below the reorder level (one open or ordered request per part) and managed by hand; nothing is sent to a supplier. Migrations 006 to 008 add `visits` and `reorder_requests`, the visit backfill and the reorder audit columns.

## Known gaps
- No authentication or authorization anywhere. The new work-order, visit and reorder endpoints trust the `userName` in the request body for notes and audit columns, and the UI only asks for an "Acting as" name.
- `UserDto` exposes `PasswordHash`.
- `compose.yaml` builds `./backend` for the `api` service but there is no Dockerfile.
- Passwords are committed in `compose.yaml` and `db-setup/01-create-users.sql`.
- `JobService.ToDtoAsync` does per-row lookups (N+1 on list); `JobRef` is random with no collision handling.
- Work orders: jobs have no required-parts data, so the "never book against a part that is not coming" warning cannot be built (open question to the stakeholder); there is no supplier integration (deferred until a supplier and API are named); the "no engineer free" warning for a P1 and the high-workload warning are not built; `PUT /api/parts/{id}` overwrites stock without triggering a reorder request.
- Must not be built: recording calls between dispatchers and customers without telling the customer (unlawful in the UK, see finding N2 in the spec). Any call-recording work needs a separate, compliant piece of work with the data protection officer.
- Seed rosters (migration 004) end on 2026-10-16, so after that date next-slot returns 404 and the board shows "No shift" for every engineer. Add shifts through the shifts API or a data migration to try scheduling on current dates.
- Pre-existing and untouched: `ShiftService` and `EngineerService` use `DateTime.Now`; the parts list endpoint is unpaged.

## Claude Code setup
- `.mcp.json` registers the read-only `mysql` MCP server from `tools/mysql-mcp`.
- `.claude/agents/` has two Haiku project agents: `e2e-browser-check` (runs API and web, checks the page, does not edit files) and `test-writer` (edits only `backend/Corrin.Tests`).
- `.claude/settings.json` hooks log edits and commits to `docs/prompt-journal.md` and, after a commit, prompt a review of this file.
