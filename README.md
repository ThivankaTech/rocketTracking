# Rockets — Solution Notes

## Architecture

I split this into four projects. The dependency direction only ever points inward:

```
Rockets.Api            → Rockets.Application, Rockets.Infrastructure
Rockets.Infrastructure → Rockets.Application  (implements its ports)
Rockets.Application    → Rockets.Domain
Rockets.Domain                                  (depends on nothing)
```

**`Rockets.Domain`** is where the `Rocket` aggregate lives, along with `RocketEvent`/`RocketEventPayload` (the message types). This project doesn't know ASP.NET exists, or EF Core, or Redis — it's plain C#, no I/O at all. When a business rule gets broken (bad message, duplicate, whatever), I don't throw for it. Methods like `Apply` and `BeginTracking` return a `Result` instead, because an invalid message is something I expect to happen, not something that should crash the app.

**`Rockets.Application`** is the use-case layer: `RocketIngestionService`, `RocketQueryService`, `RocketMessageGateway`, and the interfaces they talk to (`IRocketEventSequencer`, `IRocketRepository`, `IRocketMessagePublisher`/`IRocketMessageConsumer`, `IRocketChannelLock`). I kept the two background workers here too (`RocketStreamProcessingService`, `RocketGapSweepService`) instead of Infrastructure — they're triggered by a queue message or a timer instead of an HTTP request, but they're calling the exact same use cases, so it felt right to keep them next to what they call rather than splitting that apart.

**`Rockets.Infrastructure`** has the actual SQL Server (EF Core) and Redis code implementing those interfaces, plus the DI wiring (`AddRocketsInfrastructure`).

**`Rockets.Api`** is the minimal API host. It only ever talks to `Rockets.Application` — I went as far as removing its project reference to `Rockets.Domain` entirely, so there's no way a domain type can accidentally leak into an HTTP response.

## How a message actually gets processed

`POST /messages` doesn't touch SQL Server at all. It just maps the incoming JSON to a `RocketEvent`, pushes it onto a **Redis Stream**, and returns `202 Accepted` right away. A background worker (`RocketStreamProcessingService`) picks it up from there and does the real work.

```
HTTP POST → RocketMessageGateway → Redis Stream (XADD)
                                         ↓
                        RocketStreamProcessingService (XREADGROUP)
                                         ↓
                              RocketIngestionService
                                         ↓
                    SqlRocketEventSequencer  →  Rocket aggregate  →  SQL snapshot
```

I did it this way so that if SQL Server is slow or briefly down, it never blocks the HTTP response. Redis's consumer group also means nothing counts as "done" until it's actually been processed and persisted — so a crash mid-way through doesn't quietly lose a message.

### Messages arriving out of order, and the same message arriving twice

Every message that gets accepted is written into a `RocketEvents` table, keyed on `(Channel, MessageNumber)`. That key is doing the heavy lifting for duplicates — if the same message comes in twice, the second insert is just a no-op. Each channel also has a cursor (`RocketChannelCursors.NextMessageNumber`) that tracks how far it's gotten. Every time a new message comes in, I check whether there's now an unbroken run of messages starting from that cursor, and if there is, I release that whole run to the aggregate in order.

### What happens if a message never shows up

If message `#2` never arrives, everything from `#3` onward would just sit there forever waiting for it — nobody ever goes back and checks that channel again unless a new message happens to land on it. I didn't like that, so there are two things handling it:

- **`GapTimeout`** — once a buffered message has been waiting longer than this (it's configurable), I give up on the missing number and release whatever I've got instead. `Rocket.SkippedMessageCount` keeps track of exactly how many message numbers got abandoned this way, so it's visible on the rocket itself rather than silently disappearing.
- **`RocketGapSweepService`** — a background job that periodically goes and checks channels with stuff still waiting, instead of relying on a new message to trigger that check. It only bothers with a channel once its most recently buffered message has gone quiet for longer than `GapTimeout` — if a channel's actively getting traffic, the sweep leaves it alone.
- **`IRocketChannelLock`** — a Redis-backed lock. Once I added the sweep, it became possible for the sweep and the normal reactive path to both want to touch the same channel at the same time, so this lock makes sure only one of them ever does.

### What if the launch message itself is the one that's missing

A rocket doesn't actually need `RocketLaunched` to exist. `Type`/`Mission`/`LaunchSpeed` are nullable, and they just stay `null` (`HasLaunchDetails: false`) until a launch message shows up — this covers a rocket exploding before its launch message ever arrives, which is a real case I accounted for while building this. If `RocketLaunched` arrives within the grace period, it's applied normally like any other message. If it only turns up after `GapTimeout` has already passed, it's treated the same as any other late message — the sequencer drops it, and the rocket is left permanently without launch details rather than trying to backfill them in after the fact.

### Keeping it from falling over

- SQL transactions run at `Serializable` isolation, with EF Core's retry-on-failure turned on for transient faults.
- The background worker treats two kinds of failure differently: a bad message (`Result.Failure`) gets logged and acknowledged, because redelivering it would just fail the exact same way again. A transient failure (thrown exception, e.g. the lock timing out) is left unacknowledged on purpose, so it can be retried.
- If a message genuinely gets stuck — worker crashed, or failed and was never retried — Redis's `XAUTOCLAIM` picks it back up automatically. Nothing gets permanently lost just because one attempt at it failed.
- There's also a `GlobalExceptionHandler` sitting at the HTTP boundary as a last resort for anything truly unexpected. That's separate from the `Result` pattern, not a replacement for it — `Result` is for outcomes I expect (bad input, a duplicate, a gap); exceptions are reserved for things that are genuinely broken (infrastructure down, a bug).

## Running it locally

You need .NET 10 SDK and Docker.

```bash
# 1. Start SQL Server (1433) and Redis (6379)
docker compose up -d

# 2. Run the API — it binds to http://localhost:8088 by default
#    (see src/Rockets.Api/Properties/launchSettings.json)
dotnet run --project src/Rockets.Api

# 3. Point the provided test program at it
./darwin_arm64/rockets launch "http://localhost:8088/messages" --message-delay=500ms --concurrency-level=1
# (use whichever folder matches your OS/arch; on macOS the binary isn't signed,
#  so Gatekeeper quarantines it the first time — clear that once with:
#  xattr -d com.apple.quarantine darwin_arm64/rockets)

# 4. Look at the state
curl http://localhost:8088/rockets
curl http://localhost:8088/rockets/{channel}
```

The database schema and the Redis consumer group both get created automatically the first time it runs (`EnsureCreated`, `XGROUP CREATE`) — there's nothing else to set up beyond the two containers.

There's also a Bruno collection under `bruno/` if you'd rather click through requests than use curl — it walks through all five message types on one rocket, plus a few edge cases (unknown message type, duplicate, genuinely out-of-order).

## Running the tests

```bash
dotnet test Rockets.slnx
```

`tests/Rockets.Api.Tests` is a full end-to-end suite using `WebApplicationFactory<Program>` against the same `docker compose` services — it's hitting real SQL Server and real Redis, not mocks. Each run gets its own isolated `RocketsTest` database and a Redis stream with a random suffix (see `RocketsApiFactory`), so it never collides with anything from manual testing or a previous run. `tests/Rockets.Domain.Tests` is still an empty scaffold — I didn't end up putting pure unit tests there since the end-to-end suite already exercises the domain thoroughly.

## Things I'd do differently with more time

- **`EnsureCreated` instead of EF migrations.** Fine for this exercise, but a real system needs migrations so the schema can evolve without dropping the database.
- **`GetStaleChannelsWithBacklogAsync` pulls every buffered event into memory** to group them by channel in C#. I did it that way because it's easy to read, and at this scale it doesn't matter — but with a lot of data, that grouping should happen in SQL instead.
