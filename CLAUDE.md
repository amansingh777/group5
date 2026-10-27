# Corrin Field Service

Field-service system for Corrin Utility Services (boiler and meter install, service and repair across six English regions). Holds customers, sites, assets, engineers (skills, regions, shifts), jobs with notes and parts, parts stock, a monthly engineer utilisation report, and an importer that turns the contact centre's CSV of job requests into jobs.

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
- Tests: API tests use `[Collection("api")]` with `TestApiFactory`; service tests mock repositories with Moq.

## Known gaps
- No authentication or authorization anywhere.
- `UserDto` exposes `PasswordHash`.
- `compose.yaml` builds `./backend` for the `api` service but there is no Dockerfile.
- Passwords are committed in `compose.yaml` and `db-setup/01-create-users.sql`.
- `JobService.ToDtoAsync` does per-row lookups (N+1 on list); `AddPartAsync` checks and decrements stock without concurrency protection; `JobRef` is random with no collision handling.

## Claude Code setup
- `.mcp.json` registers the read-only `mysql` MCP server from `tools/mysql-mcp`.
- `.claude/settings.json` hooks log edits and commits to `docs/prompt-journal.md` and, after a commit, prompt a review of this file.
