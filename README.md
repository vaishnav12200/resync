# ReSync

ReSync is an offline-first local request delivery engine for developers.
It stores submitted HTTP requests durably in SQLite and will later deliver them through a local agent.
Build with `dotnet build ReSync.sln`, then run the agent with `dotnet run --project src/ReSync.Agent`.
