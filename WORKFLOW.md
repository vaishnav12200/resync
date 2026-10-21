# ReSync Workflow

## Goal

ReSync accepts an HTTP request from a local application, stores it durably, and delivers it when the destination is available. The original application does not need to remain open after handing off the request.

## Request Lifecycle

1. An application submits a request to the ReSync agent on `127.0.0.1:5050`.
2. The agent validates the request and creates a `DeliveryJob` in SQLite.
3. SQLite enforces idempotency with the unique `(ApplicationId, RequestId)` pair.
4. A background worker selects pending or scheduled jobs when they are due.
5. The worker delivers the stored method, payload, and destination URL with `HttpClient`.
6. A successful delivery marks the job `Delivered`; a failed attempt is scheduled for retry or marked `Failed` after its retry limit.

## Job States

`Pending` → `Processing` → `Delivered`

`Pending` → `Processing` → `RetryScheduled` → `Processing`

`Pending` → `Processing` → `Failed`

## Current Status

The domain model, SQLite context, local agent host, and durable job APIs are in place. The background worker, delivery, and retries are planned next.
