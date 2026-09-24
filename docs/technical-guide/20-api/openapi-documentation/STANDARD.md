---
standard: openapi-documentation
aliases: [openapi-documentation]
title: OpenAPI Documentation
version: 1.1.0
status: active
updated: 2026-09-23
applies-to: [backend]
---

# OpenAPI Documentation

## Scope

How the API contract is described, published and kept honest.

## Official requirements

Subject V.4 requires reference documentation presenting the API's methods, inputs and outputs,
and names Swagger as an acceptable generator. Peer evaluators and the team's own mobile
developers are the first consumers, so the document is graded material.

## Project policy

- Generator: **`Microsoft.AspNetCore.OpenApi`**, the built-in one
  (`brain/decisions/ADR-0021-backend-dependency-set.md`). Not Swashbuckle — the reference
  repository uses it because the built-in generator did not exist in net6.
- The OpenAPI 3.1 document is **generated from the code**, never hand-maintained. A hand-written
  spec drifts and drift is worse than no spec.
- Interactive UI is served in Development. In other environments the raw document is served but
  the UI is disabled unless explicitly enabled.
- Every endpoint documents: summary, description, all parameters, request body schema,
  **every** response status it can return, and at least one realistic example.
- The Problem Details error schema is declared once and referenced by every error response.
- Security schemes are declared (bearer JWT) and applied per operation. Endpoints that are
  intentionally anonymous say so explicitly.
- Enums appear as enums in the schema, with their string or integer values, not as bare integers.
- Pagination envelope and client context headers are declared as reusable components.
- The generated document is exported to `docs/api/openapi.json` on release and committed, so a
  peer evaluator can read the contract without running the service.
- CI fails if the generated document does not build, and a diff in `openapi.json` must appear in
  the pull request that changed the endpoint.

### XML comments

Enable `GenerateDocumentationFile`; use `<summary>`, `<param>`, `<response>` on controller
actions so the description comes from the source rather than a parallel document.

## Checklist

- [ ] Document generates and validates in CI
- [ ] Every endpoint has all response codes and one example
- [ ] Security scheme declared and applied
- [ ] `docs/api/openapi.json` updated in the same PR as the endpoint change
- [ ] Interactive UI disabled outside Development

## References

- OpenAPI 3.1 specification — https://spec.openapis.org/oas/v3.1.0
- ASP.NET Core OpenAPI support — https://learn.microsoft.com/aspnet/core/fundamentals/openapi/overview

## Related

`../rest-api-design/STANDARD.md` · `../../70-delivery/ci-cd-containerization/STANDARD.md`
