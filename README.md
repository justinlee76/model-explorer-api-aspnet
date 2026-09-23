# Model Explorer API — ASP.NET Core

ASP.NET Core backend for querying stored model runs, submitting and controlling training jobs, and streaming live model, metric, job, and log updates through SignalR.

This is one of two interchangeable APIs used by the Model Explorer frontend. Choose this service for .NET and SignalR; use `model-explorer-api-fastapi` for Python and native WebSockets.

## Features

- REST endpoints for models, metrics, training tasks, and jobs.
- SignalR subscriptions for model tags, metric histories, and job logs.
- MongoDB queries, mutations, GridFS cleanup, and change-stream processing.
- Camel-case JSON responses compatible with the frontend and Python training data.
- Swagger UI and OpenAPI documents in the Development environment.

## Requirements

- .NET 10 SDK
- MongoDB 6.0 or later, deployed as a replica set, sharded cluster, Atlas deployment, or another deployment that supports change streams
- The Model Explorer training service if submitted jobs should be executed

The API and training service must point to the same MongoDB database. The API does not seed training tasks; register at least one task as described in the [training service README](https://github.com/justinlee76/model-explorer-training-service#register-the-built-in-task) before submitting jobs.

## Quick start

Start MongoDB and complete the [MongoDB setup](#mongodb-setup), then run from the repository root:

```bash
dotnet restore ModelStore.slnx
dotnet run --project ModelStoreApi
```

The checked-in `http` launch profile uses the Development environment and listens on `http://localhost:5071`. It exposes:

- REST API: `http://localhost:5071/api`
- Swagger UI: `http://localhost:5071/swagger`
- OpenAPI document: `http://localhost:5071/openapi/v1.json`

Swagger and OpenAPI are only mapped in the Development environment. The launch-profile address does not apply to a published application; set its listening address with `ASPNETCORE_URLS` or your deployment configuration.

## Configuration

ASP.NET Core loads the checked-in `ModelStoreApi/appsettings.json`, environment-specific settings, environment variables, and command-line arguments. Environment variables use double underscores for nested keys. This application does not load `.env` files automatically.

| Setting | Environment variable | Checked-in default | Description |
| --- | --- | --- | --- |
| `ModelStore:Uri` | `ModelStore__Uri` | `mongodb://localhost:27017` | MongoDB connection URI. |
| `ModelStore:Database` | `ModelStore__Database` | `model_store` | Database shared with the training service. |
| `ModelStore:ModelCollection` | `ModelStore__ModelCollection` | `models` | Model metadata and metric collection. |
| `ModelStore:TaskCollection` | `ModelStore__TaskCollection` | `tasks` | Training-task registry collection. |
| `ModelStore:JobCollection` | `ModelStore__JobCollection` | `jobs` | Training jobs and embedded logs collection. |
| `AllowOrigins` | `AllowOrigins__0`, `AllowOrigins__1`, ... | `http://localhost:5173`, `http://localhost:5174` | Exact browser origins allowed by CORS. |

For example, override the MongoDB connection and add another local frontend origin without editing a tracked settings file:

```bash
ModelStore__Uri='mongodb://localhost:27017/?replicaSet=rs0' \
ModelStore__Database='model_store' \
AllowOrigins__2='http://localhost:5175' \
dotnet run --project ModelStoreApi
```

Keep credential-bearing MongoDB URIs out of tracked files and provide them through your deployment's environment or secret manager. The current training service uses the fixed collection names `models`, `tasks`, and `jobs`, so keep those defaults unless the worker is updated to match.

To connect the Model Explorer frontend to this API, use:

```dotenv
VITE_API_URL=http://localhost:5071/api/
VITE_MESSAGING_TRANSPORT=signalr
```

Keep the trailing slash in `VITE_API_URL`. If Vite selects a different port, add that exact origin to `AllowOrigins` and restart the API. CORS allows credentials, so wildcard origins are not supported by this configuration.

## MongoDB setup

The API watches both the configured model and job collections with MongoDB change streams. Model deletion notifications need the deleted document's tag, so the model stream also requires pre-images.

If your local MongoDB server is a standalone deployment, first follow MongoDB's guide to [convert it to a replica set](https://www.mongodb.com/docs/manual/tutorial/convert-standalone-to-replica-set/), including the one-time `rs.initiate()` step. [Pre/post images require MongoDB 6.0 or later](https://www.mongodb.com/docs/manual/reference/command/collmod/#change-streams-with-document-pre--and-post-images).

In `mongosh`, select the configured database, create the model collection if necessary, and enable pre/post images:

```javascript
db = db.getSiblingDB("model_store")

if (!db.getCollectionNames().includes("models")) {
  db.createCollection("models")
}

db.runCommand({
  collMod: "models",
  changeStreamPreAndPostImages: { enabled: true }
})
```

Use your configured database and model-collection names if they differ from `model_store` and `models`. The job collection also requires a change-stream-capable deployment, but it does not require pre/post images. A standalone `mongod` without replica-set support is not sufficient.

## HTTP API

The REST API has a fixed `/api` prefix. JSON fields use camel case, including `taskId`, `metricName`, `className`, and `modelId`; the timestamp field is serialized as lowercase `datetime`. Model, task, and job IDs are MongoDB ObjectId strings.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/tags` | List model tags. |
| `GET` | `/api/models?tag=...` | List model runs for a tag. |
| `GET` | `/api/metric-names` | List stored metric names. |
| `POST` | `/api/metric-history` | Return histories for model/metric key objects. |
| `POST` | `/api/delete-models` | Attempt to delete trained-model IDs supplied as a JSON string array and return one `error` message if deletion fails. |
| `GET` | `/api/tasks` | List registered training tasks. |
| `GET` | `/api/job-defaults` | Return inputs from the latest job, or the first registered task. |
| `POST` | `/api/add-job` | Add a submitted training job. |
| `GET` | `/api/jobs` | List jobs, newest first. |
| `POST` | `/api/stop-job` | Move a job to the stopping state. |
| `DELETE` | `/api/delete-job/{id}` | Delete a job. |
| `GET` | `/api/jobs/{id}/messages` | Return a job's log messages. |

For example, list the registered tasks and submit a job using one of their IDs:

```bash
curl http://localhost:5071/api/tasks

curl --request POST http://localhost:5071/api/add-job \
  --header 'Content-Type: application/json' \
  --data '{"taskId":"<task ObjectId>","args":[],"kwargs":{}}'
```

Request metric history for one or more model/metric pairs:

```bash
curl --request POST http://localhost:5071/api/metric-history \
  --header 'Content-Type: application/json' \
  --data '[{"id":"<model ObjectId>","metricName":"val_loss"}]'
```

The training service executes submitted jobs and records their status, logs, metrics, and model state. Without a running worker, new jobs remain in the `Submitted` state.

`POST /api/delete-models` processes IDs in order and only deletes models whose status is `Trained`; models still marked `Training` and nonexistent IDs are ignored. The response has an `error` string (or `null` on success) and no deletion count. If deletion fails, processing stops, so earlier models may already be deleted. Deleting a trained model also attempts to remove its same-ID GridFS checkpoint.

## SignalR

The SignalR hubs are mounted at the application root, not under `/api`. Arguments and event payloads use the same camel-case JSON policy as the REST API.

| Hub | Client methods | Server events |
| --- | --- | --- |
| `/ModelHub` | `SubscribeTag(tag)`, `UnsubscribeTag(tag)`, `Subscribe(key)`, `Unsubscribe(key)`, `SubscribeAll(keys)`, `UnsubscribeAll(keys)` | `AddModel(model)`, `UpdateModel(model)`, `RemoveModel(id)`, `AddMetricData({ id, metricName, index, value })` |
| `/JobHub` | `Subscribe(jobId)`, `Unsubscribe(jobId)` | `AddJob(job)`, `UpdateJob(job)`, `RemoveJob(id)`, `AddJobMessage(id, index, message)` |

A model metric key has the shape `{ "id": "<model ObjectId>", "metricName": "val_loss" }`. Tag subscribers receive model add/remove events and updates affecting training history, metric summaries, or status for that tag; changes only to other model fields are not broadcast. Metric-key subscribers receive new points for that exact model and metric.

Every `/JobHub` connection receives job add, update, and remove events. Subscribe to a job ID to additionally receive that job's live log messages.

For example, subscribe with the JavaScript SignalR client:

```javascript
import * as signalR from "@microsoft/signalr"

const models = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5071/ModelHub")
  .withAutomaticReconnect()
  .build()

const keys = [
  { id: "<model ObjectId>", metricName: "val_loss" }
]

models.on("AddMetricData", update => console.log(update))
models.onreconnected(() => models.invoke("SubscribeAll", keys))

await models.start()
await models.invoke("SubscribeAll", keys)
```

SignalR publishes changes that happen after the connection is established; it does not send an initial snapshot. Fetch current models, metric histories, jobs, and messages through the HTTP API before subscribing. A reconnect creates a new server connection, so invoke the desired subscriptions again after reconnecting.

## Storage

| MongoDB data | Purpose |
| --- | --- |
| `models` | Model metadata, status, metric summaries, and metric history. |
| `tasks` | Importable training task module/class registrations. |
| `jobs` | Submitted inputs, status, linked model ID, errors, and embedded logs. |
| GridFS | Serialized model state written by the training service. |

## End-to-end startup order

1. Start a change-stream-capable MongoDB deployment and enable model pre-images.
2. Register a training task and point the training service at the same MongoDB URI and database.
3. Start this API.
4. Start `model-explorer-training-service` to execute submitted jobs.
5. Start `model-explorer-frontend` with the SignalR transport.

The worker is not required to browse existing runs, but it is required to process jobs.

## Project layout

| Path | Purpose |
| --- | --- |
| `ModelStore.slnx` | .NET solution file. |
| `ModelStoreApi/Program.cs` | Dependency registration, middleware, routes, and hub mapping. |
| `ModelStoreApi/Controllers/` | REST API controller. |
| `ModelStoreApi/MongoDB/` | MongoDB/GridFS access, BSON conventions, and change streams. |
| `ModelStoreApi/Hubs/` | SignalR hubs and subscription methods. |
| `ModelStoreApi/Services/` | Background model/job monitors and subscription tracking. |
| `ModelStoreApi/Domain/` | MongoDB domain types and change records. |
| `ModelStoreApi/Dtos/` | REST and SignalR response/request shapes. |
| `ModelStoreApi/Serialization/` | JSON naming policy. |
| `ModelStoreApi/appsettings.json` | Checked-in local defaults. |
| `ModelStoreApi/ModelStoreApi.http` | Sample IDE HTTP requests. |

## Security

Authentication is not configured, and CORS is not access control. Do not expose the API directly to untrusted networks or include secrets in persisted job inputs and logs. Use HTTPS and authentication for shared deployments.

## Companion projects

| Repository | Role |
| --- | --- |
| [`model-explorer-frontend`](https://github.com/justinlee76/model-explorer-frontend) | React browser interface. |
| [`model-explorer-api-fastapi`](https://github.com/justinlee76/model-explorer-api-fastapi) | Alternative FastAPI and native WebSocket API. |
| [`model-explorer-training-service`](https://github.com/justinlee76/model-explorer-training-service) | PyTorch worker that executes queued jobs. |
