---
kind: adr-index
updated: 2026-09-28
---

# Decision Log

Format: one file per decision. Status is `Accepted`, `Proposed`, `Superseded` or `Open`.
An `Open` entry is a decision we know we owe, with no answer yet — it is not a placeholder to delete.

| [[ADR-0001-backend-dotnet-narchitecture]] | .NET 10 backend, Clean Architecture + CQRS | Accepted | landed |
| [[ADR-0002-mobile-react-native]] | React Native client, deferred | Accepted | client |
| [[ADR-0003-music-provider-youtube]] | YouTube as the music provider | Accepted | S4 |
| [[ADR-0004-realtime-signalr]] | SignalR over WebSocket for realtime | Accepted | landed |
| [[ADR-0005-api-style-rest-json]] | REST + JSON + OpenAPI | Accepted | landed |
| [[ADR-0006-datastore]] | Supabase (managed PostgreSQL) as the datastore | Accepted | landed, S4 |
| [[ADR-0007-password-hashing-bcrypt]] | BCrypt for password hashing | Accepted | landed |
| [[ADR-0008-oidc-social-login]] | OpenID Connect for Google and Facebook sign-in | Accepted, amended 2026-09-27: one shared validation core | S8 |
| [[ADR-0009-code-quality-gates]] | OWASP conformance and enforced code-size limits | Accepted | landed |
| [[ADR-0010-service-scope]] | Ship all three services | Accepted | landed |
| [[ADR-0011-solution-layout]] | Solution layout mirrors nArchitecture | Accepted | landed |
| [[ADR-0012-test-stack]] | xUnit, NSubstitute, Testcontainers | Accepted | landed |
| [[ADR-0013-mail-provider]] | Transactional mail through a hosted provider | Accepted | S1a |
| [[ADR-0014-location-time-licence]] | Geofence plus optional event code | Accepted | landed |
| [[ADR-0015-hosting-and-deployment]] | Local Docker for the defence, CI without deployment | Accepted | landed |
| [[ADR-0016-supabase-plan-region]] | Supabase Free tier, Frankfurt | Accepted | S4 |
| [[ADR-0017-playback-session]] | PlaybackSession aggregate, client-driven queue advance | Accepted | landed |
| [[ADR-0018-load-testing-tool]] | Gatling for load testing | Accepted | S9 |
| [[ADR-0019-hub-action-auditing]] | Audit SignalR hub actions with the same record | Accepted | landed |
| [[ADR-0020-monorepo-layout]] | Backend and client in one repository | Accepted | landed |
| [[ADR-0021-backend-dependency-set]] | MediatR, FluentValidation, built-in rate limiting and OpenAPI | Accepted | landed |
| [[ADR-0022-observability-stack]] | Serilog with OpenTelemetry | Accepted | landed |
| [[ADR-0023-catalog-cache]] | Two-level catalog cache | Accepted | S4 |
| [[ADR-0024-expiry-and-cleanup]] | Lazy expiry checks, one cleanup service | Accepted | landed, S4 |
| [[ADR-0025-agent-git-protocol]] | Git protocol for unattended agent runs | Accepted, push clause superseded by ADR-0027 | landed |
| [[ADR-0026-environment-bring-up]] | Environment bring-up sequence | Accepted | landed |
| [[ADR-0027-agent-push-and-pr]] | Agent pushes its branch and opens a draft pull request | Accepted | landed |
| [[ADR-0028-domain-owns-its-base-types]] | Domain owns its base types; Application owns its service abstractions | Accepted | landed |
| [[ADR-0029-containerised-dev-and-secret-vault]] | Fully containerised development, and a secret vault for API keys | Accepted | landed, S1b |
| [[ADR-0030-hub-rate-limits-and-origin]] | Hub call limits and handshake origin | Accepted | S9 |
| [[ADR-0031-musicroom-as-openid-provider]] | The MusicRoom API becomes an OpenID Provider at S8 | Accepted | S8 |
| [[ADR-0035-swagger-ui-for-the-openapi-document]] | Swagger UI over the existing OpenAPI document, Development only | Accepted | S3a |
| [[ADR-0036-access-token-mechanics]] | JwtBearer issues and validates; the token carries `sid`; 30 seconds of clock skew | Accepted | landed |
| [[ADR-0037-credential-rate-limiting]] | Per-IP middleware plus a per-account in-process counter; a refusal is a value | Accepted | landed |
| [[ADR-0032-refresh-token-lifetime]] | Refresh-token families live 30 days, absolute | Accepted | landed |
| [[ADR-0033-audit-write-posture]] | The audit record commits with its action, fails closed, behind `IUnitOfWork` | Accepted | landed |
| [[ADR-0034-profile-storage-and-visibility]] | Typed profile columns with a tier each; preferences as private rows | Accepted | landed, S3 |
| [[ADR-0038-realtime-broadcast-posture]] | A handler queues a broadcast; a behaviour sends it after the commit | Accepted | landed |
| [[ADR-0039-queue-broadcast-payloads]] | An `EventHub` payload carries the authoritative value, not a row version | Accepted | landed |
| [[ADR-0040-pull-requests-need-not-be-up-to-date]] | A pull request need not be up to date with `main` to merge | Accepted | landed |
| [[ADR-0041-limiter-after-authentication]] | The rate limiter runs after authentication, so a per-account limit is per account | Accepted | landed |
| [[ADR-0042-breached-and-context-password-screening]] | A chosen password is screened against a bundled breached set and a context word list, with no outbound call | Accepted | built by SPEC-035, T-32 closed. Amended twice: the unification came first (SPEC-034), and the "two homes" rationale was replaced after SPEC-035 measured that neither non-register handler reaches the account |
| [[ADR-0043-validation-error-key-naming]] | A validation error is keyed by the field name the client sent, camelCase, never empty | Accepted | built by SPEC-037, O28 closed. Amended before landing: `WithName` names a message and `OverridePropertyName` names a key, two routes' fields are spelled differently from their properties, and `genres.count` was struck as a defect |
| [[ADR-0044-development-database-backup]] | The development database is dumped on demand into an ignored directory, seven kept | Accepted | not built, O23 |
| [[ADR-0045-stable-error-codes]] | Every refusal carries a stable code; the text stays English and the client owns the language | Accepted | not built |

| [[ADR-0046-live-deployment-render]] | The live deployment target is Render — the only one of the three free tiers the owner named that can host the API and a self-hosted Infisical | Accepted | built by SPEC-051, O11 closed |

Related: [[MEMORY]] · [[system-overview]] · [[standards-router]]
