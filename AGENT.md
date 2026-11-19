# Agent guide for this repository

This file tells any agent (or human) working on this codebase how to write code here.
The patterns below are **strict** — they are observed with zero exceptions across every
file in the repo today. Do not introduce an alternative style "because it's valid C#" or
"because it's more idiomatic elsewhere." If you believe a rule should change, say so and
ask before deviating — don't silently mix styles.

## Architecture — one-way dependency flow

```
Rockets.Domain        <- pure business rules, zero dependencies on anything else
       ^
Rockets.Application   <- interfaces + orchestration services, depends only on Domain
       ^
Rockets.Infrastructure<- EF Core / Redis implementations, depends on Application + Domain
       ^
Rockets.Api            <- minimal API endpoints, wires everything together
```

- `Rockets.Domain` never references EF Core, Redis, ASP.NET Core, or anything outside the
  BCL. Business invariants (e.g. "a rocket can't launch twice") live here, expressed via
  the `Result`/`Result<T>` pattern (see below) — not exceptions.
- Interfaces are defined in `Rockets.Application`, named `I{Name}` (`IRocketRepository`,
  `IRocketEventSequencer`, `IRocketChannelLock`). Concrete implementations live in
  `Rockets.Infrastructure`, named `{ProviderPrefix}{Name}` (`SqlRocketRepository`,
  `RedisRocketChannelLock`, `RedisRocketMessageConsumer`). Never put an implementation in
  `Rockets.Application`, and never put interfaces in `Rockets.Infrastructure`.
- `Rockets.Api` contains **no business logic** — route handlers in `Program.cs` call into
  an Application-layer service (`RocketMessageGateway`, `RocketQueryService`) and translate
  a `Result`/`Result<T>` into an HTTP response. If you find yourself writing `if` branches
  on domain state inside `Program.cs`, that logic belongs in the Application layer instead.

## Error handling — `Result` / `Result<T>`, not exceptions, for expected outcomes

- Every method that can fail for a *business* reason (invalid state transition, not found,
  save conflict, rejected event) returns `Result` or `Result<T>` (see `Rockets.Domain/Result.cs`).
  Check `.IsFailure` / `.IsSuccess`; never throw-and-catch for something that is a normal,
  expected outcome of the domain.
- Exceptions are reserved for genuinely exceptional, infrastructure-level conditions only
  (e.g. `RocketChannelLockTimeoutException` when a distributed lock can't be acquired at
  all). There are exactly two exception types in this codebase outside the ASP.NET Core
  `GlobalExceptionHandler` catch-all at the API boundary — keep it that way. Don't add a new
  custom exception type for something that `Result.Failure(...)` can express instead.

## LINQ — fluent method syntax only, no query syntax, no raw SQL

- All database access goes through `RocketsDbContext` via LINQ **method-chain syntax**:
  `.Where(...)`, `.Select(...)`, `.GroupBy(...)`, `.GroupJoin(...)`, `.SelectMany(...)`.
- Never use query-expression syntax (`from x in ... where ... select ...`). If a query needs
  a left join, use `.GroupJoin(...).SelectMany(x => x.inner.DefaultIfEmpty(), ...)` — see
  `SqlRocketEventSequencer.GetStaleChannelsWithBacklogAsync` for the canonical example.
- Never use `FromSqlRaw`/`ExecuteSqlRaw`/stored procedures. Bulk mutations (delete/update
  across many rows) use EF Core's LINQ-based bulk extensions — `ExecuteDeleteAsync` /
  `ExecuteUpdateAsync` — chained onto a normal `Where(...)`, never a hand-written DELETE/UPDATE
  string.
- Never load a full table into memory and filter/group in C# when the same result can be
  expressed as a LINQ query and pushed to the server. If you're calling `.ToListAsync()`
  before you've finished filtering, stop and push the filter into the query first.

## EF Core entities — `required`, not `= string.Empty`

- EF-mapped persistence classes (suffixed `Record`: `RocketEventRecord`, `RocketSnapshotRecord`,
  `RocketChannelCursorRecord`, `RejectedRocketEventRecord`) are `sealed class`, never `record`,
  with mutable `{ get; set; }` properties — EF needs to materialize and track them.
- Every non-nullable `string` property that is always populated before use is declared
  `required string X { get; set; }`. Never default a non-nullable string to `string.Empty`
  just to satisfy the compiler — that silently masks a property nobody actually set.
  `required` makes the compiler enforce it at every construction site (EF Core's own
  materialization bypasses this check safely, so round-tripping through the database is
  unaffected). If adding `required` breaks a construction call site, fix the call site to
  set the field in the object initializer — don't revert to a dummy default.
- A property that can genuinely be absent (`Type`, `Mission`, `ExplosionReason` on
  `RocketSnapshotRecord`) stays `string?`, not `required`, and not defaulted to empty string.

## Immutable data vs. mutable entities — `record` vs `sealed class`

- `record` (always `sealed record`) is for immutable, pure data carriers with no behavior:
  domain events and payloads (`RocketEvent`, `RocketLaunched`, `RocketSpeedIncreased`, ...),
  delivery envelopes (`RocketMessageDelivery`), DTOs/snapshots returned across a boundary
  (`RocketSnapshot`).
- `sealed class` is for anything with behavior or mutable, tracked state: the `Rocket`
  aggregate (private setters, methods that enforce invariants), every EF entity, every
  service/repository/sequencer implementation.
- **Every class in this codebase is `sealed`.** There is no inheritance hierarchy among
  classes anywhere except the one deliberate domain polymorphism (`RocketEventPayload` as
  an `abstract record` base for the payload union). Don't design a new feature around
  inheritance; use composition, interfaces, or a payload-style closed union of sealed
  records instead.

## Dependency injection and mapping — no extra libraries

- Constructor injection only, with `private readonly` fields prefixed `_` (`_db`, `_repository`,
  `_sequencer`). All DI registration is centralized in one place:
  `ServiceCollectionExtensions.AddRocketsInfrastructure`. Don't scatter `services.Add...` calls
  elsewhere.
- No AutoMapper, no MediatR, no FluentValidation, no third-party DI container. Mapping between
  layers (domain <-> persistence) is hand-written via static factory methods — `FromDomain`,
  `ToDomain`, `CopyFrom` — directly on the persistence record class. If you need a new mapping,
  write the method; don't reach for a mapping library.
- Keep the NuGet dependency list as small as it already is (EF Core SqlServer, StackExchange.Redis,
  ASP.NET Core, xUnit — that's it). Adding a new package is a decision to flag explicitly, not
  something to do quietly while implementing a feature.

## Transactions — one explicit transaction per atomic unit of work

- When a feature requires multiple writes to succeed or fail together (e.g. advancing a
  cursor, buffering an event, and saving an aggregate), wrap the *entire* unit in one
  `await using var transaction = await _db.Database.BeginTransactionAsync(...)`, and commit
  only after every step has succeeded — `transaction.RollbackAsync()` (or let an exception
  propagate through the `await using` block) on any failure. See
  `SqlRocketEventSequencer.SubmitOnceAsync` for the canonical pattern: the sequencer's cursor
  advance and the caller's aggregate save share one transaction, passed through as a callback,
  so they always commit or roll back together.
- Never rely on several independent `SaveChangesAsync()` calls to approximate atomicity.
- Services that share one `DbContext` instance within a DI scope automatically share the
  ambient transaction — that's intentional. Don't open a second `DbContext` or a second
  connection mid-flow to "simplify" a dependency.

## API layer — minimal APIs only

- `Rockets.Api/Program.cs` uses minimal API route handlers (`app.MapGet`, `app.MapPost`).
  There are no MVC controllers in this project and none should be added. A new endpoint is a
  new `app.Map...` call that delegates to an Application-layer service and translates its
  `Result` into `Results.Ok/BadRequest/NotFound/Accepted/Problem`.

## Schema — no migrations

- The database schema is defined purely by `RocketsDbContext.OnModelCreating` and created via
  `EnsureCreated()` (`ServiceCollectionExtensions.EnsureRocketsInfrastructureReady`). There is
  no `Migrations` folder and none should be added. A schema change means editing the model —
  don't introduce `dotnet ef migrations add`.

## Comments

- Default to no comments. Code should read clearly from naming alone. Only add a comment when
  it documents a genuinely non-obvious **why** — a hidden invariant, a subtle ordering
  requirement, a workaround for a specific bug. Never add a comment that restates what the
  next line of code already says.

## Testing — real infrastructure, no mocks

- xUnit only. Tests in `Rockets.Api.Tests` run against the **real** SQL Server and Redis
  containers defined in `docker-compose.yml` via `WebApplicationFactory<Program>`
  (`RocketsApiFactory`) — there is no mocking framework and no in-memory fake anywhere in this
  repo. If a new test needs infrastructure, it talks to the real thing, scoped with a unique
  channel name / stream key so tests don't collide.
- Test method names follow `Scenario_ExpectedOutcome` (e.g. `DuplicateMessage_IsAppliedOnlyOnce`,
  `GapExceedingTimeout_SkipsAheadAndRecordsSkippedCount`) — PascalCase, underscore separating
  the setup from the assertion being made.
- A bug fix is not done until there is a regression test for it, and that test must be proven
  to actually fail against the pre-fix code (not just pass against the fix) before it's trusted.
  Reverting the fix locally and watching the test fail — then restoring it and watching the
  test pass — is the standard of proof here, not just "the test is green."

## When something doesn't fit these rules

If a task seems to need a different pattern (a new exception type, query syntax, a mocking
library, a non-sealed class, a migration), that's a signal to stop and ask rather than
introduce it quietly. Consistency across the whole codebase is the point — a single file
written in a different style is worse than slightly awkward code written in the existing one.
