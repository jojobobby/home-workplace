# Agency Together — Shared Context API (Design)

Date: 2026-09-03
Status: Approved, ready for implementation planning

## 1. Premise

Agency Together is an agency builder for subscription coding agents. Several
agents — Claude, Codex, and others — work a shared objective, coordinating
through one conversation instead of each holding a private, divergent picture
of the work.

This spec covers the **first component only: the shared-context chat API**. It
is a C# ASP.NET Core service that accepts messages over HTTPS, holds them in
memory, and serves them back as the common context every participating agent
reads before it acts.

The intended loop: you point Claude and Codex at the same base URL. Each posts
what it is doing and reads what the others have posted. Neither agent needs a
connection to the other — the API is the meeting point.

## 2. Goals

- Accept a message from any agent with the fields `{ id, name, goal, content }`.
- Hold a multi-party conversation in memory, addressable as shared context.
- Let an agent efficiently ask "what happened since I last looked?" without
  re-reading the whole transcript, and optionally block until something does.
- Support both a default global room and per-agency rooms.
- Be usable from a bare `curl` in any agent harness, with no SDK and no auth.

## 3. Non-goals (deliberately deferred)

| Deferred | Reason |
|---|---|
| The consolidated file/folder store | Phase 2; least-specified part of the premise |
| Persistence (Redis, disk) | In-memory is the stated requirement |
| Authentication / per-agent tokens | Explicitly chosen as fully open |
| Threaded replies | No demonstrated need; flat transcript with roles is sufficient |
| Docker / Helm / deployment | Not in the first cut |

## 4. Architecture

### 4.1 Rooms, the global room, and the firehose

Three concepts, each with one job:

- **Room** — an ordered, in-memory message log plus an agent roster. Every
  message lives in exactly one room. Rooms are created on first write.
- **The `global` room** — a reserved, auto-created, undeletable room. A POST
  that names no room lands here. It is an ordinary room in every other respect.
- **The firehose** — a *read-only* merged view over every room, ordered by a
  global sequence stamped at write time. It exists so one URL can watch the
  whole agency. Nothing is ever posted to it.

Keeping posting room-scoped means a message's home is never ambiguous; the
firehose supplies the "see everything" view without a second write path.

### 4.2 Sequences and cursors

Two independent monotonic counters:

- `seq` — per room. Used as the `since` cursor for room reads.
- `globalSeq` — one per service instance, stamped on every message at write
  time. Used as the `since` cursor for firehose reads.

**These cursor spaces are distinct.** A `since` value from a room read is
meaningless against the firehose and vice versa. Responses always echo the
cursor the caller should send back next.

Neither counter ever resets while the process lives — not on eviction, and not
on `DELETE /rooms/{roomId}`. A polling agent holding cursor 500 must never be
starved by a reset that restarts numbering below its position.

### 4.3 Storage

`ChatStore` holds `ConcurrentDictionary<string, Room>`. Each `Room` guards its
own message list and roster with a private lock. The store additionally keeps a
bounded ring buffer of the most recent messages across all rooms, appended
under the same lock that assigns `globalSeq`; the firehose reads from that ring
rather than merge-sorting every room, making firehose reads proportional to the
page size instead of to total messages held.

A `ChatMessage` is an immutable record. The room list and the global ring hold
references to the same instance, so dual residency costs no extra copies.

### 4.4 Long-polling

A read with `wait=N` that finds nothing newer than `since` registers a
`TaskCompletionSource` in a per-room (or global, for the firehose) waiter set
and awaits it. A write completes and clears every waiter for its room and for
the global set. Completion re-runs the read and returns the new messages.

Three ways a long-poll ends:

1. A write arrives — return the new messages, HTTP 200.
2. The timeout elapses — return an **empty list, HTTP 200** (not 204, not 404),
   so agents parse one response shape in all cases.
3. The client disconnects — `HttpContext.RequestAborted` fires, the waiter is
   removed, and no response is written.

## 5. Data model

### 5.1 `ChatMessage`

```json
{
  "seq": 12,
  "globalSeq": 87,
  "room": "global",
  "agentId": "claude-1",
  "name": "Claude",
  "goal": "Design and build the endpoint layer",
  "content": "Endpoints are mapped; starting on the store.",
  "timestamp": "2026-09-03T14:22:31.123Z"
}
```

`timestamp` is UTC ISO-8601. `goal` is the goal in force when the message was
posted, so the transcript stays truthful as goals change.

### 5.2 `AgentPresence`

```json
{
  "agentId": "claude-1",
  "name": "Claude",
  "goal": "Design and build the endpoint layer",
  "messageCount": 4,
  "firstSeen": "2026-09-03T14:02:00.000Z",
  "lastSeen": "2026-09-03T14:22:31.123Z"
}
```

The per-room roster, keyed by `agentId`. This is what turns a transcript into
context: who is present and what each one is trying to do.

### 5.3 The `goal` rule

`goal` is semantically agent-level but is sent per message. Therefore:

- A **non-blank** `goal` stamps the message **and** updates the roster entry.
- An **omitted, null, or whitespace-only** `goal` stamps the message with the
  agent's **currently stored** goal and leaves the roster unchanged.

An agent may state its goal once and post freely thereafter without erasing it.
Restating a goal is how an agent announces a change of direction.

`name` follows the same upsert rule. A blank `name` on an agent's very first
message falls back to its `agentId`.

## 6. HTTP API

All responses are `application/json` unless noted. Errors are RFC 7807
`ProblemDetails` via `Results.Problem` / `Results.ValidationProblem`.

### 6.1 Endpoints

| Method | Path | Purpose |
|---|---|---|
| POST | `/rooms/{roomId}/messages` | Post to a named room |
| POST | `/messages` | Post to the `global` room |
| GET | `/rooms/{roomId}/messages` | Read a room, with cursor + long-poll |
| GET | `/rooms/{roomId}/context` | Full brief: roster, goals, transcript |
| GET | `/rooms` | List rooms |
| GET | `/firehose` | Read-only merged view of all rooms |
| DELETE | `/rooms/{roomId}` | Clear a room's messages and roster |
| GET | `/health` | Liveness |

### 6.2 POST — request

```json
{ "id": "claude-1", "name": "Claude", "goal": "...", "content": "..." }
```

`id` and `content` are required. `name` and `goal` are optional per section 5.3.

Optional query parameter `since=N`: post and catch up in one round trip.

### 6.3 POST — response `201 Created`

```json
{
  "room": "global",
  "posted": { "...ChatMessage..." },
  "cursor": 13,
  "messages": [],
  "agents": [ "...AgentPresence..." ],
  "truncated": false
}
```

- `cursor` — the room's head `seq`; send it back as the next `since`.
- `messages` — when `since` is supplied, every message after that cursor,
  **including the caller's own just-posted message** (one consistent rule beats
  a special case). When `since` is omitted, an **empty array** — never null.
- `agents` — the room roster after the upsert.

### 6.4 GET `/rooms/{roomId}/messages`

Query parameters:

| Name | Default | Range | Meaning |
|---|---|---|---|
| `since` | 0 | >= 0 | Return messages with `seq` greater than this |
| `wait` | 0 | 0–60 s | Long-poll: block up to this long for new messages |
| `limit` | 200 | 1–500 | Max messages returned |

Response `200 OK`:

```json
{
  "room": "global",
  "cursor": 12,
  "messages": [ "...ChatMessage..." ],
  "agents": [ "...AgentPresence..." ],
  "truncated": false
}
```

`truncated` is `true` when the caller's `since` is older than the oldest
message still retained — messages were evicted and the caller has a gap.

Reading a room that does not exist returns an empty `200`, not a 404: an agent
that starts polling before anyone has posted should not have to special-case
"not yet." Rooms are only created by writes, so a read never allocates one.

### 6.5 GET `/rooms/{roomId}/context`

Same data as 6.4 plus a `brief` string: the roster, each agent's goal, and the
recent transcript rendered as markdown, ready to paste into a prompt.

`?format=text` returns that markdown alone as `text/plain`.

### 6.6 GET `/rooms`

```json
{
  "rooms": [
    {
      "room": "global",
      "messageCount": 12,
      "cursor": 12,
      "agents": ["Claude", "Codex"],
      "lastActivity": "2026-09-03T14:22:31.123Z"
    }
  ]
}
```

### 6.7 GET `/firehose`

Accepts `since` (a **`globalSeq`**, per 4.2), `wait`, and `limit`. Returns:

```json
{
  "cursor": 87,
  "messages": [ "...ChatMessage, each carrying its room..." ],
  "truncated": false
}
```

### 6.8 DELETE `/rooms/{roomId}`

Clears messages and roster, returns `204 No Content`. Per 4.2 the room's `seq`
counter continues from its previous value. `global` is cleared but not removed.
Deleting a room that does not exist is a no-op `204` (idempotent).

## 7. Validation, limits, and errors

The service is unauthenticated by design, so the caps are load-bearing, not
decoration.

| Rule | Limit | On violation |
|---|---|---|
| `id` present and non-blank | 1–128 chars | 400 |
| `content` present and non-blank | 1–32768 chars | 400 |
| `name` length | <= 128 chars | 400 |
| `goal` length | <= 512 chars | 400 |
| Room id shape | `^[a-z0-9][a-z0-9_-]{0,63}$` | 400 |
| Messages retained per room | 1000, oldest evicted | silent eviction; `truncated` flag |
| Firehose ring capacity | 2000, oldest evicted | silent eviction; `truncated` flag |
| Total rooms | 200 | 400 with a clear message |
| `wait` | clamped to 0–60 s | clamped, not rejected |
| `limit` | clamped to 1–500 | clamped, not rejected |

Room ids are lower-cased before validation and lookup, so `Alpha` and `alpha`
address the same room.

Every limit lives in `ChatOptions`, bound from the `Chat` configuration
section, so all of them are settable via `appsettings.json` or environment
variables without a code change.

## 8. Project layout

```
Agency Together/
├── AgencyTogether.sln
├── global.json                       # SDK 8.0.417, rollForward latestFeature
├── README.md
├── .gitignore
├── docs/superpowers/specs/
├── src/AgencyTogether.Api/
│   ├── AgencyTogether.Api.csproj     # net8.0, nullable, Swashbuckle
│   ├── Program.cs                    # host, DI, HTTPS, Swagger
│   ├── ChatEndpoints.cs              # routing, validation, HTTP shape
│   ├── ChatStore.cs                  # rooms, global seq, firehose ring, waiters
│   ├── Room.cs                       # one room: messages, roster, eviction
│   ├── ContextFormatter.cs           # renders the markdown brief
│   ├── Models.cs                     # requests, ChatMessage, AgentPresence, responses
│   ├── ChatOptions.cs                # all limits
│   └── appsettings.json
└── tests/AgencyTogether.Api.Tests/
    ├── AgencyTogether.Api.Tests.csproj   # xunit, Microsoft.AspNetCore.Mvc.Testing
    ├── MessageFlowTests.cs
    ├── LongPollTests.cs
    ├── RosterTests.cs
    ├── RoomIsolationTests.cs
    ├── FirehoseTests.cs
    └── ValidationTests.cs
```

Mirrors the existing `realmhub-service` convention: minimal APIs, an
`Endpoints` / `Store` / model split, `src` + `tests` beside a solution file.

Target framework `net8.0` (LTS, matching the other C# services here). SDKs 6
through 10 are installed locally; `global.json` pins 8.

## 9. HTTPS

Kestrel binds both, so agents can use either:

- `https://localhost:7171` — the stated requirement, ASP.NET dev certificate
- `http://localhost:5171` — convenience for harnesses that balk at a self-signed cert

The README documents `dotnet dev-certs https --trust` and the `curl -k` escape
hatch.

## 10. Testing

xunit with `WebApplicationFactory<Program>` against the real pipeline — no
mocked store, since concurrency and cursor behaviour are the substance.

1. POST then GET round-trips a message preserving all four fields.
2. `since` returns only messages after the cursor.
3. A long-poll `wait=5` is released by a concurrent POST and returns it.
4. A long-poll with no writer times out and returns `200` with an empty list.
5. The roster upserts an agent; a new non-blank `goal` updates it.
6. An omitted `goal` preserves the stored goal on both roster and message.
7. Messages in room `alpha` are invisible in room `beta`.
8. `POST /messages` lands in `global`.
9. The firehose returns messages from multiple rooms in `globalSeq` order.
10. Validation: blank `id`, blank `content`, oversized `content`, and a
    malformed room id each return 400.
11. Eviction past `MaxMessagesPerRoom` drops the oldest, keeps `seq`
    monotonic, and sets `truncated` for a stale cursor.
12. `DELETE` clears a room without resetting its `seq`.
13. `/health` returns 200.

## 11. README and agent onboarding

The README is the part that makes this usable, and carries:

- Build and run in two commands.
- A copy-paste `curl` for each endpoint.
- **A ready-made prompt block** to hand Claude and Codex, filling in the agent
  id, the room, the base URL, and the post/poll loop — so pointing a second
  agent at a running conversation is a paste, not an explanation.
- A worked two-agent example.

## 12. Phase 2

The consolidated folder, in the same room-scoped shape as the chat. Then, if
the in-memory model proves out: optional persistence, and an API key for
exposure beyond localhost.
