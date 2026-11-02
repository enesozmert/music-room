# Running MusicRoom live, on Render

**Live at https://music-room-api-regw.onrender.com** since 2026-10-11.

The deployment ADR-0046 chose, as a runbook. It covers what is clicked, in what order, and what breaks
if the order is wrong. `render.yaml` carries the same stack as code; this file carries the parts a
Blueprint cannot: the values, the sequence and the traps.

**Nothing in this file is a secret, and no secret belongs in it.** Every credential is entered once in
a dashboard and lives there.

## What runs where

| Piece | Where | Plan |
|---|---|---|
| API | Render web service, Docker, Frankfurt | free |
| Vault (Infisical) | Render web service, image `infisical/infisical:latest-postgres`, Frankfurt | free |
| Vault cache | Render Key Value | free, 25 MB, not persisted |
| Application database | Supabase project `musicroom`, Frankfurt | free |
| Vault database | Supabase project `musicroom-vault`, Frankfurt | free |

Two Supabase projects rather than one, because ADR-0029 requires the vault's store to be separate from
the application's — one backup and one migration should not cover both the secret store and the user
data. Two Supabase projects rather than Render's own PostgreSQL, because **Render's free database is
deleted 30 days after it is created**, and the database that disappears would be the one holding every
secret.

## Order

It matters. Each step depends on the one above it.

### 1. The application database

Supabase → project `musicroom` → **Connect** → **Direct connection string** → **Transaction pooler**
(port 6543) → Type `.NET`. Append the flags Npgsql needs behind PgBouncer:

```
;SSL Mode=Require;Trust Server Certificate=true;No Reset On Close=true;Max Auto Prepare=0;Maximum Pool Size=10
```

`No Reset On Close=true` is the one that is not optional. Npgsql sends `DISCARD ALL` when it returns a
connection to its pool; PgBouncer in transaction mode refuses that statement, and the failure surfaces
later as unrelated errors on random requests.

Take the **session pooler** string (port 5432) as well. It is not for the API — it is what migrations
run through, because transaction-mode pooling cannot carry them.

### 2. Migrations

Repository → Settings → Secrets and variables → Actions → `DATABASE_DIRECT_URL` = the session-pooler
string. Then run the **Migrate production database** workflow by hand.

By hand, and not on deploy: schema ownership belongs to migrations run deliberately (SDD §8.5). An
application that migrates as it starts migrates once per instance and once per restart, and a free tier
restarts whenever it feels like it.

### 3. The API

Render → New → Web Service → this repository, branch `main`.

| Setting | Value |
|---|---|
| Language | Docker |
| Region | **Frankfurt** — the database is there |
| Dockerfile Path | `MusicRoom/Dockerfile` |
| Docker Build Context Directory | `.` |
| Root Directory | *(empty)* |
| Health Check Path | `/health` |
| Instance Type | Free |

The context is the repository root because the Dockerfile's `COPY` paths say `MusicRoom/…` — SPEC-051.
Setting a Root Directory *and* these two is the mistake that produced `"/src": not found`: two settings
that have to agree, and only one of them was.

Environment variables, before the first deploy — the host refuses to start without them and a missing
one is a crash loop rather than a warning:

```
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
ASPNETCORE_HTTP_PORTS=10000
PORT=10000
JWT_ISSUER=musicroom
JWT_AUDIENCE=musicroom-clients
JWT_ACCESS_TOKEN_MINUTES=15
BCRYPT_WORK_FACTOR=12
MAIL_PROVIDER=brevo
SECRET_PROVIDER=env
```

and, one at a time because the values are secret or multi-line:

- `DATABASE_URL` — step 1's transaction-pooler string
- `MAIL_API_KEY`, `MAIL_FROM` — Brevo, with a sender Brevo has verified
- `OIDC_SIGNING_KEY` — a **new** key for this deployment:

  ```
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 2>/dev/null | awk 'BEGIN{ORS="\\n"}1'
  ```

  One line with `\n` written out, which the provider undoes. A production signing key is not a key that
  also sits in somebody's `.env`; losing it invalidates every token it signed, which is what
  `OIDC_SIGNING_KEY_PREVIOUS` exists to avoid during a rotation.

`MAIL_PROVIDER=logging` is refused outside Development on purpose: that sender writes the verification
link and the password-reset token to the application log (T-26).

### 4. The vault

Supabase → new project `musicroom-vault`, Frankfurt. Its **URI**-format connection string is
Infisical's `DB_CONNECTION_URI` — Infisical is a Node application and does not read .NET's format.

Render → New → Key Value → `music-room-vault-cache`, free, Frankfurt.

Render → New → Web Service → **Existing image** → `docker.io/infisical/infisical:latest-postgres`,
Frankfurt, free. Variables:

| Key | Value |
|---|---|
| `DB_CONNECTION_URI` | the second project's URI string |
| `ENCRYPTION_KEY` | `openssl rand -hex 16` — **generated once; losing it loses every secret in the vault** |
| `AUTH_SECRET` | `openssl rand -base64 32` |
| `SITE_URL` | the vault's own `https://…onrender.com` URL, filled in after the first deploy |
| `REDIS_URL` | the Key Value instance's internal connection string |
| `TELEMETRY_ENABLED` | `false` |

Then, in the vault's UI: create the machine identity, give it a client secret, **add it to the project**
with a read role, and copy the project id. Adding it to the project is the step that is easy to skip and
produces a `403` at fetch time rather than at login.

### 5. Switch the API onto the vault

Put this deployment's secrets into the Infisical project's `prod` environment:

| Goes into the vault | Stays in `render.yaml` |
|---|---|
| `DATABASE_URL`, `MAIL_API_KEY`, `MAIL_FROM`, `OIDC_SIGNING_KEY`, `YOUTUBE_API_KEY`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `GOOGLE_DISCOVERY_URL`, `GOOGLE_ISSUER`, `CORS_ALLOWED_ORIGINS` | `JWT_ISSUER`, `JWT_AUDIENCE`, `JWT_ACCESS_TOKEN_MINUTES`, `BCRYPT_WORK_FACTOR`, `MAIL_PROVIDER`, the ports, `ASPNETCORE_FORWARDEDHEADERS_ENABLED` — none of them is a secret, and a value in two places is a value that disagrees with itself |

The ten values do not get typed into the vault's web form. Put them in a local `.env.prod` — git-ignored,
and the write-boundary hook refuses to create one — and write them in one command (SPEC-054):

```
make vault-seed FILE=.env.prod DRY=1    # names only, nothing sent
make vault-seed FILE=.env.prod          # create what is missing, update what exists
```

It prints names and outcomes and never a value, skips an absent name rather than writing an empty
secret, and is idempotent — so rotating one secret later is the same command with one changed line. The
file also carries the five `INFISICAL_*` values it needs to log in; those are **not** written to the
vault, because they are what opens it.

A `403` on a write means the machine identity is read-only in the project, which is the posture the
application wants: grant write for the seed and take it back afterwards.

Then, on the API service, **in this order**:

```
INFISICAL_SITE_URL=<the vault's URL>
INFISICAL_CLIENT_ID=<the identity's client id, not its identity id>
INFISICAL_CLIENT_SECRET=<its secret>
INFISICAL_PROJECT_ID=<project id>
INFISICAL_ENVIRONMENT=prod
```

**and only then** `SECRET_PROVIDER=infisical`. The other way round the host finds no vault address,
refuses to start, and Render rolls back to the previous deploy — SPEC-047's refusal working as designed,
which looks exactly like a broken deploy while you are watching it happen.

Deploy, then `make smoke-live`. **Remove the moved secrets from Render only after it passes**: while both
hold a value the vault's wins (proven on the development stack by putting a deliberately invalid
`JWT_ACCESS_TOKEN_MINUTES` in the vault and watching the host refuse to boot with a valid one in `.env`),
so keeping them for one deploy costs nothing and leaves a way back.

Those five stay in Render, and they are the only ones that have to: they are what opens the vault, so
they cannot live inside it — ADR-0029 said that before any of it was built.

**The API will not start if the vault does not answer.** That is SPEC-047's refusal and it is deliberate:
a fallback would silently return the system to reading secrets from environment variables, and nothing in
a log would say the posture had changed. Start-up retries for about ninety seconds first, because on this
tier the vault may be waking up.

## The traps this tier comes with

**`ASPNETCORE_FORWARDEDHEADERS_ENABLED` is not optional.** Render terminates TLS at its proxy, so
without it the host sees `http` and two things break at once. The visible one: the discovery document
and every ID token say `iss: http://…` while being served over HTTPS, and a conforming client refuses an
issuer that does not match the origin it fetched from. The silent one: `RemoteIpAddress` is the proxy's,
so SPEC-006's per-IP credential limit puts every caller in the world in one bucket — nothing fails, the
limit simply stops being a limit. Both were found by driving the live deployment, not by reading it.

| Trap | What happens | What to do |
|---|---|---|
| **Services sleep after 15 minutes** | the first request waits about a minute | warm the stack before a demo: the vault first, then the API |
| **Sleep plus the vault dependency** | the API can wake while the vault is cold | start-up retries ~90 s; if the vault is genuinely down the API refuses, which is the design |
| **Supabase pauses a free project after 7 days idle** | both projects; the API answers `503` on readiness | restore from the Supabase dashboard, one click |
| **750 instance-hours per workspace per month** | two services awake continuously do not fit | let them sleep; warm on demand rather than on a schedule |
| **Free Key Value is not persisted** | a restart empties the vault's cache | nothing — durable state is in PostgreSQL |
| **Render's free PostgreSQL expires in 30 days** | not used here, deliberately | keep both databases on Supabase |
| **A variable nobody set looks like a bug** | `YOUTUBE_API_KEY` was never added to this service, so every `GET /tracks` answered `503` for days | `make smoke-live` names it in one request; the start-up log says it in the first screen after a deploy |

## Checking a deployment — `make smoke-live`

```
make smoke-live                              # the live service
make smoke BASE=http://localhost:8080        # the composed stack, same checks
SMOKE_EMAIL=… SMOKE_PASSWORD=… make smoke-live   # also the signed-in reads
```

What it checks is **configuration**, which is what a deployment gets wrong — SPEC-053. It creates
nothing, registers nobody and sends no mail: the 723 tests in `make agent-check` prove the rules, and
repeating them against production would leave rows nothing deletes. With `SMOKE_EMAIL` and
`SMOKE_PASSWORD` for one already-verified account it also signs in, reads `GET /me`, runs one
`GET /tracks`, and then ends its own session, so a run leaves no trace.

| Finding | Cause, in practice |
|---|---|
| `catalog: Degraded` | `YOUTUBE_API_KEY` is not set on the service. Nothing else is affected, which is why readiness is still `200` |
| `the database is reachable` fails | `DATABASE_URL` is wrong, Supabase has paused the project, or migrations were never applied |
| the issuer does not match the base URL | `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is missing — the trap above |
| `track search answers` is `503` | the log names which cause: no key, a key the provider rejects, the daily quota, or the provider itself |

A `503` from search with a key present is usually the key rather than the quota: YouTube answers `400`
for one that is malformed, expired or restricted to referrers this host is not among. The development
stack's key was in exactly that state while the live one worked, which is how the fourth cause was
found. One `Warning` line per call says which it was; `Information` lines carry the quota units spent.

## Warming up before a defence

```
curl -fsS https://music-room-vault.onrender.com/api/status
curl -fsS https://music-room-api-regw.onrender.com/health
curl -fsS https://music-room-api-regw.onrender.com/health/ready
curl -fsS https://music-room-api-regw.onrender.com/.well-known/openid-configuration
```

Or `make smoke-live`, which does all of it and says what is wrong when something is.

Render appends a suffix to a service's host name, so the URL is not the service name — `render.yaml`
names the service and the platform names the host.

In that order: the vault first, because the API reads it at start-up. `/health/ready` is the one that
proves the database is awake too.

## What this deployment is not

It is **not** the machine subject V.7's capacity figure belongs to. That figure is measured on the
declared laptop in `docs/load/REPORT.md`, and a 512 MB shared-CPU container that sleeps after fifteen
minutes is not a measurement platform. This deployment buys a live URL and HTTPS; it does not buy a
number, and the report will not pretend otherwise.
