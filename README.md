# ModelStore

ASP.NET Core API for querying model training runs, exposing job controls, and broadcasting live updates through SignalR.

## Highlights

- ASP.NET Core Web API targeting .NET 10
- MongoDB-backed model, task, job, log, and GridFS storage
- SignalR hubs for live metric, model, job, and job-log updates
- Swagger/OpenAPI enabled in development
- Custom JSON/BSON serialization for the Python training data shape

## Requirements

- .NET 10 SDK
- MongoDB replica set or deployment that supports change streams

## Getting started

```bash
dotnet restore
dotnet run --project ModelStoreApi
```

By default the API listens on `http://localhost:5071` and exposes REST endpoints under `/api`.
Local MongoDB and CORS settings are in `ModelStoreApi/appsettings.json`.

## Checks

```bash
dotnet build
```
