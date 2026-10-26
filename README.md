# Corrin Field Service

Corrin Utility Services installs, services and repairs domestic and commercial boilers and meters across six regions of England. Customers, their sites and the equipment at each site live in this system, together with the engineers, their skills, regions and rostered shifts, the history of every job, parts stock, a monthly engineer utilisation report, and an importer that turns the contact centre's CSV of job requests into jobs.

## Run it

Needs: .NET 8 SDK, Node 20, and a MySQL 8 server on port 3306 that you can reach as root. The tests use the same database, so keep the server running.

```
mysql -h 127.0.0.1 -P 3306 -u root -p < db-setup/01-create-users.sql
dotnet run --project backend/Corrin.Api
dotnet test backend/Corrin.sln
cd frontend && npm install && npm run dev
```

API: http://localhost:5080/swagger, web app: http://localhost:5173

MySQL on another host or port: set the environment variable `ConnectionStrings__Default` to a full connection string (server, port, database, user, password,) before starting the API or the tests. The tests need MySQL.
