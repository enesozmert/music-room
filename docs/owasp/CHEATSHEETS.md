---
aliases: [owasp-cheatsheets]
kind: reference-index
updated: 2026-09-23
---

# OWASP Cheat Sheets That Apply Here

Linked, not vendored: the series changes often and a stale local copy is worse than a link.
See [[owasp-provenance]] for what is vendored and why.

Read the sheet **before** writing the feature, not during review.

| Sheet | Read it before | Link |
|---|---|---|
| Password Storage | touching password hashing — confirms BCrypt parameters and the 72-byte limit | https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html |
| Authentication | the registration and login slice | https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html |
| Forgot Password | the reset flow — enumeration and token handling | https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html |
| Session Management | refresh-token rotation and revocation | https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html |
| JSON Web Token for Java (algorithm-agnostic guidance) | issuing and validating our own JWTs | https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html |
| OAuth 2.0 Protocol | the social login slice | https://cheatsheetseries.owasp.org/cheatsheets/OAuth2_Cheat_Sheet.html |
| Authorization | the two-gate design | https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html |
| Input Validation | every command validator | https://cheatsheetseries.owasp.org/cheatsheets/Input_Validation_Cheat_Sheet.html |
| Mass Assignment | DTO binding — reject unknown fields | https://cheatsheetseries.owasp.org/cheatsheets/Mass_Assignment_Cheat_Sheet.html |
| SQL Injection Prevention | any raw SQL in a repository | https://cheatsheetseries.owasp.org/cheatsheets/SQL_Injection_Prevention_Cheat_Sheet.html |
| REST Security | the API contract | https://cheatsheetseries.owasp.org/cheatsheets/REST_Security_Cheat_Sheet.html |
| Logging | the audit behaviour and the never-logged list | https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html |
| Error Handling | the Problem Details mapping | https://cheatsheetseries.owasp.org/cheatsheets/Error_Handling_Cheat_Sheet.html |
| Secrets Management | `.env` handling and the leak procedure | https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html |
| Denial of Service | pagination caps and rate limits | https://cheatsheetseries.owasp.org/cheatsheets/Denial_of_Service_Cheat_Sheet.html |
| Docker Security | the container image | https://cheatsheetseries.owasp.org/cheatsheets/Docker_Security_Cheat_Sheet.html |
| Transport Layer Security | HTTPS, HSTS, WebSocket over TLS | https://cheatsheetseries.owasp.org/cheatsheets/Transport_Layer_Security_Cheat_Sheet.html |
| .NET Security | framework-specific guidance | https://cheatsheetseries.owasp.org/cheatsheets/DotNet_Security_Cheat_Sheet.html |

Not applicable today, revisit if scope changes: XSS Prevention, Content Security Policy,
Clickjacking Defense, CSRF Prevention (no cookie-based sessions, no web frontend), File Upload.

## Related

[[owasp-index]] · [[asvs-conformance]] · [[owasp-top10-checklist]]
