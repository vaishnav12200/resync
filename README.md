# ReSync

ReSync is an offline-first request-delivery engine for developers. Applications hand HTTP requests to a local ASP.NET Core agent, which stores them durably in SQLite before delivering them when the destination is available.

The agent is designed to run locally on `127.0.0.1:5050`, so an application can safely hand off a request and exit. ReSync is a single local process: no microservices, Docker, Redis, RabbitMQ, or PostgreSQL.

The current foundation includes the `DeliveryJob` domain model, the EF Core SQLite context, and durable job APIs. `POST /api/jobs` queues a request only after the SQLite commit; `GET /api/jobs` and `GET /api/jobs/{id}` expose its stored status. Delivery workers and retries are the next milestone.

## Build and Run

```bash
dotnet restore ReSync.sln
dotnet build ReSync.sln
dotnet run --project src/ReSync.Agent
```

See [WORKFLOW.md](WORKFLOW.md) for the planned request lifecycle and job states.
