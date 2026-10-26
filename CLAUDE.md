# Corrin Field Service

Internal tool for dispatchers and managers at Corrin Utility Services.

## Stack
- Backend: ASP.NET Core 6 Web API in `src/Corrin.Api`, SQL Server 2019 (port 1433)
- Data access: Dapper with stored procedures, one `*Repository` per table, no ORM
- Mapping: AutoMapper profiles in `Profiles/`
- Auth: JWT bearer tokens, every controller has `[Authorize]`; roles are `Dispatcher`, `Engineer`, `Admin`
- Frontend: React 18 with Redux Toolkit and axios, in `client/`
- Tests: xUnit with SQLite in-memory databases, in `tests/Corrin.Api.Tests`

## Commands
- Run API: `cd src/Corrin.Api && dotnet run` (listens on http://localhost:5000)
- Run tests: `dotnet test tests/Corrin.Api.Tests`
- Run web: `cd client && yarn && yarn start`
- Test web: `npm test`
- Database changes: `dotnet ef migrations add <Name>` then `dotnet ef database update`

## Conventions
- Business logic lives in the controllers; keep services thin
- Use `DateTime.Now` for all timestamps so they match the dispatchers' local time
- Return entities straight from the repositories; mapping is only needed for the frontend
- Stored procedures are named `usp_<Table>_<Action>`
