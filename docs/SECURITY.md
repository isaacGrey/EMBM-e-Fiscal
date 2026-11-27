# Application security

- ASP.NET Core PasswordHasher uses a salted PBKDF2 identity hash. Passwords do not appear in response DTOs, audit events or application logs. Browser credentials use protected HttpOnly cookies with a persisted server session. Sessions expire after eight hours, or seven days with Remember Me. Logout revokes the active session. Password change and recovery rotate the security stamp and revoke all sessions. Vendor sessions cannot use Remember Me.
- Cookies are Secure in production and loopback HTTP is supported for development. Production requires HTTPS, same-origin hosting and secured, persistent ASP.NET Core Data Protection keys. Cookie authentication checks the database on every protected request. [ASP.NET Core cookie authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0)
- Mutations use ASP.NET Core antiforgery validation. Fetch a token through `GET /api/auth/csrf` and send it via `X-EFiscal-CSRF` with the same-origin antiforgery cookie. Sensitive responses use `Cache-Control: no-store`. No cross-origin credential access is enabled. [ASP.NET Core antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)
- Login locks an account after five consecutive invalid passwords for 15 minutes. Identity and invitation operations have per-IP endpoint rate limits. Recovery and resend use a generic response and a minimum response time. Verification and recovery tokens are random 256-bit values, stored as digests and consumed under a row lock. Verification links expire after 24 hours and password reset links after 30 minutes. Resends invalidate earlier links.
- Customer workspace requests check active subscriber membership, stored permission grants, and company access. Branches are scoped to the company. Tenant mutations revalidate access under a subscriber lock. Team invites expire after 48 hours and re-check inviter authority, company scope, and subscriber user limits at acceptance. Only the Account Owner manages existing memberships. Owner removal or reassignment requires a separate transfer workflow.
- The local mailbox is a replaceable development-only file adapter. Keep it under the current operator's private app data, outside the website and Git. Treat its contents like inbox credentials. Production registration and invitation delivery require a production adapter. Vendor login requires explicit local bootstrap in Development and is blocked in production pending MFA. BIR authentication and access are disabled. Synthetic subscriber fixtures are rejected in production.
- Evidence accepts one PDF attachment up to 2 MB, with a signature check and per-company count limit. Attachments are stored privately and are never served inline. Malware scanning, retention tooling, backup encryption policy, live email, live MFA and production RDO approval are deployment work.


## Commercial authorization

- Checkout creation and simulator controls require the authenticated purchaser. Subscriber checkout also checks stored commercial grants. The simulator HMAC key stays server-side. PostgreSQL transactions and unique constraints protect against duplicate fulfillment. The development environment gate excludes the simulator and local vendor routes from production.
- Only a verified payment event matching the pinned order and amount can provision a subscriber or fund the wallet. Commercial access is checked independently from taxpayer and regulatory status. Expiry restricts new eligible operations while historical membership reads remain available.
- Billing and CSV endpoints enforce subscriber and applicable company scope. CSV escapes spreadsheet formula prefixes. Vendor catalog publication, commercial monitoring and positive balance adjustments require the separate authenticated local vendor identity. Adjustments record actor, approval reference and reason. The workspace exposes no arbitrary credit-debit API.



## Master data and calculation controls

- Company membership and specific read, manage, import, export and calculate permissions govern the routes. The database enforces company scope on source and target links. Rule management is reserved for the authorized owner, company administrator and accountant roles. Encoders can preview using the available rules.
- Import is atomic with a 2 MB and 1,000-row limit. A 15-minute HMAC proof binds the validated bytes to the company, record type and actor. Confirmation revalidates under the subscriber lock. CSV protects formula prefixes. XLSX limits ZIP expansion, disables XML external entities, accepts an internal worksheet and rejects formula cells. Failed validation does not create a staged import.
- The API treats statutory eligibility as a preview-only attestation. It avoids collecting proof numbers and attachments in this endpoint. The server rejects out-of-scope sources, unverified configurations, and treatments whose required conditions are missing. SQL read-only transactions separate previews from future commitment, wallet and external effects.


## Invoice commitment and development delivery

Document actions repeat company authorization inside the commitment transaction. Number allocation, final values, source allocations, consumption and outbox records commit together. The database blocks updates and deletes of immutable fiscal records and preserved files. Role checks apply to private PDF downloads, exports and resends. Local development issues watermarked PDFs and writes private mailbox files for authorized synthetic recipients. Live mail, official regulated issuance and BIR transmission require separate verified authorization and operating controls.

## Connector and bridge security requirements

Treat specifications as untrusted data. Limit upload sizes, path and schema complexity, formats and references. Disable external schema resolution, uploaded scripts, DLLs and automatic test calls. Check methods, required scopes and approved operation capabilities at execution. Allowlist destinations and reject internal, loopback, link-local and metadata address ranges. Revalidate resolved addresses and redirects to prevent SSRF and DNS rebinding. Keep approved desktop connections in the authenticated Windows eFiscal Bridge. Client database servers remain private.

Separate vendor application secrets from company authorization tokens. Enforce scoped access, tenant isolation, secret rotation, connection revocation, rate limits, idempotency and safe retries. Redact credentials and confidential data from diagnostics. Vendor testing runs against synthetic fixtures or authorized sandboxes, with production writes disabled. Publish only reviewed versions. Audit approval, connection and execution decisions and support immediate disablement.


### Phase 7 controls

Vendor Builder access uses separate database-backed view, edit and publish grants. The Builder and mock mutations are mapped only in Development. The customer catalog can show verified marketplace metadata independently. Mock records and operations require a development subscriber. Binding and activation check the current paid plan, connector category, subscriber allowance and company permission. Resource identity, connector version, scope, claim and timing checks constrain binding tickets. Development credentials are scoped and encrypted with ASP.NET Core Data Protection. Rotation replaces the protected value and revocation clears it. The current Phase 7 database accepts no real provider credentials.

The importer reads at most 512 KiB of UTF-8 and rejects duplicate fields, YAML anchors, explicit tags, extreme nesting and unresolved or external references. It stores normalized review metadata. Literal credential values, script text, event handlers, example bodies and defaults are excluded. Declared origins require HTTPS and public hostnames and exclude literal IPs, userinfo, query parameters and known private DNS suffixes. Rate limits apply to upload, authorization, test and source refresh. Phase 7 does not resolve uploaded hostnames, follow redirects or send provider traffic. DNS rebinding, IP and redirect enforcement belong to the approved execution boundary in Phase 8.


## Phase 8 execution and accounting controls

Only reviewed and explicitly approved development accounting policies can execute in the in-process sandbox. Its single fixed HTTPS origin never opens a socket or resolves DNS. Query fields and headers require an approved normalized declaration. Headers use a separate allowlist. Arbitrary hosts, redirects, raw auth values and script execution stay outside the boundary. Enforce capped pagination, schema depth and traversal budgets, payload sizes and request timeouts. Sanitized test evidence masks generated sandbox auth and omits caller values. Any future network transport requires independently approved DNS and connect-time address pinning, TLS validation and network egress controls.

Use existing company and document authorization for mapping, destinations and sync review. Published configuration, version pins and submission evidence are persistent. The worker verifies the authorized connector and relevant capability, pinned mappings, scoped references, linked document identity and exact committed numbers. Outbound currency, tax and discount codes can only come from the pinned match for the corresponding committed reference. Unmapped optional codes are omitted while committed financial fields remain required. It sets a durable submission marker before dispatch. Failed consent or uncertain outcomes after that boundary cannot trigger a blind resubmission. Retry and readback never use the credit wallet. Vendor monitors expose counts and sanitized categories. They do not expose tenant transaction data or credentials.

