# Implemented API contract

| Method and route | Access | Response |
| --- | --- | --- |
| GET /api/health | Public | 200 JSON with `status: "healthy"` and `service: "embm-efiscal-api"`. An unhealthy process check returns 503 Problem Details. |
| GET /openapi/v1.json | Development only | OpenAPI JSON. |

The health endpoint checks process liveness. It does not report database readiness or integration status. Unmatched routes return 404 Problem Details. Nonpublic endpoints require a server-validated cookie session, and workspace operations also require an active customer membership for the requested scope.

The client preview fetches liveness on entry using TanStack Query, checks the shape with Zod, and offers a retry on failure. The public site and estimator read illustrative terms from the PostgreSQL catalog. The sample workspace uses fictional records and the authenticated workspace uses PostgreSQL data.

## Identity and tenant API

JSON requests and responses use camelCase. Mutations require a token from `GET /api/auth/csrf` in the `X-EFiscal-CSRF` header and its same-origin cookie. Obtain a fresh token after an identity change. Validation errors use Problem Details. Unauthenticated requests receive 401. Authorized users receive 404 for unavailable tenant or company resources.

| Route | Behavior |
| --- | --- |
| POST /api/auth/register | Validate B2B or B2C details and preserve selected plan and billing frequency. Return a generic accepted response for new and duplicate addresses. |
| POST /api/auth/verify-email, /resend-verification | Consume or replace the verification link. |
| POST /api/auth/login, /logout; GET /api/auth/me | Create or revoke the cookie session. Read verified identity and pending subscription data. |
| POST /api/auth/forgot-password, /reset-password | Request a generic recovery response. Reset with an expiring token and revoke prior sessions. |
| PUT /api/auth/profile, /password | Update the signed-in user or change their password and end sessions. |
| POST /api/auth/vendor/login, /bir/login | Explicit local vendor login only. BIR rejects access. |
| GET /api/workspace | List member subscribers, pending subscription and currently authorized session context. |
| GET or PUT /api/workspace/{subscriberId} | Read authorized subscriber information or update its display name as the Account Owner. |
| GET /api/workspace/{subscriberId}/rdos | List active reference offices for an authorized subscriber. |
| GET or POST /api/workspace/{subscriberId}/companies | List authorized companies and permissions or create the company, taxpayer, RDO and first branch in one transaction. |
| GET or PUT …/companies/{companyId} | Read scoped details or save authorized company information. |
| PUT …/companies/{companyId}/taxpayer | Save taxpayer configuration and preserve prior effective-dated RDO assignments. |
| GET or POST …/companies/{companyId}/branches; GET or PUT …/branches/{branchId} | List, read, create or update branches within an authorized company. |
| POST …/companies/{companyId}/evidence | Attach one private multipart PDF in field `file`. |
| POST /api/workspace/{subscriberId}/context | Validate and save active company and optional branch in the server session. |
| GET …/team; POST …/team/invitations; PUT or DELETE …/team/members/{userId} | Read authorized team data, invite users, manage role and company access or remove a non-owner member. |
| POST /api/invitations/accept, /register | Accept as the verified matching identity or create a new verified identity using the emailed proof. |

A verified, unprovisioned registration reports Pending Payment. Registration does not activate a subscription, provision an owner, or allocate credits. Email links are delivered through the adapter, with no token returned in an API response.

## Company delivery configuration API

Routes are relative to `/api/workspace/{subscriberId}/companies/{companyId}/configuration`. Each requires an active customer session with Company.Manage and authorized access to that company. Writes and previews require the antiforgery token. A missing or inaccessible resource returns 404. Concurrent revisions return 409.

| Route | Behavior |
| --- | --- |
| GET or PUT /sender | Read or save sender and provider preferences with an expected revision. Returns unverified/pending status and local test history. Provider secrets are excluded and rejected. |
| POST /sender/validate | Validate the same non-secret settings. Reports explicitly that no connection or sender verification was attempted. |
| POST /sender/test | Write an audited sample to the development mailbox using a saved local platform sender preference. The sender read exposes the last successful local test and independent verification and connection status. |
| GET or POST /templates/{kind}/{documentType} | List saved configurations and built-in content or create a company template. Kinds are email and pdf. Types are invoice and credit-memo. |
| GET or PUT /templates/{templateId} | Read current version and history or append a validated version with an expected version. |
| POST /templates/{templateId}/duplicate | Copy the current saved version to a new template in the same company and type. |
| POST /templates/{templateId}/restore | Append the selected historical content as a new version; preserve older rows. |
| PUT /templates/{kind}/{documentType}/default | Select a matching company template or clear the company override to use the built-in default. |
| POST /email-preview/{documentType} | Render allowlisted variables using fictional sample values. |
| POST /email-preview/{documentType}/test | Write a rate-limited fictional sample to the local development mailbox. No caller-supplied recipient or external delivery. |

The editor offers a browser illustration and a watermarked server-rendered sample PDF. The API stores declarative layout versions, including a bounded inline PNG, and preserves mandatory line columns. The Documents API owns issued PDF downloads, source-order retrieval and conversion. Live sender verification and provider connectivity await approval.

## Subscription, billing and wallet API

| Route | Behavior |
| --- | --- |
| GET /api/billing/catalog | Published plan and credit package versions and checkout availability. Anonymous. |
| POST /api/billing/checkout | Verified customer opens an initial checkout using saved registration. Send `purpose: InitialSubscription` and an operation key. Development only. |
| GET /api/billing/checkout/{id}; POST …/simulate/{outcome} | Purchasing account reads the checkout or simulates a signed result. Supported outcomes are paid, processing, failed, cancelled, duplicate and refund. Development only. |
| GET /api/billing/subscribers/{subscriber}/summary | Billing.View. Returns paid period, entitlements, renewal and eligible packages, plus projected credit balances and lots. |
| POST …/checkout | Billing.Manage for renewal, Credits.Purchase for top-ups. Send an operation key and, for top-ups, a published package version ID. Development only. |
| PUT …/subscription; PUT …/auto-top-up | Manage scheduled plan changes, cancellation and renewal consent, the development payment reference, and authorized top-up settings. |
| GET …/payments | Billing.View. Filter by purpose and status and paginate with `page`. |
| GET …/credits | Credits.View. Filter by Manila dates, company scope, document type and search. Paginate with `page` or set `export=true` for CSV. |
| GET /api/vendor/billing/catalog, /monitor; POST /api/vendor/billing/plans, /packages, /wallets/{subscriber}/adjustments | Authorized local vendor management of new commercial versions, payment and wallet monitoring, and audited adjustments. Development only. |

A checkout result is a status view. Payment fulfillment happens when the server verifies the event. Development payment confirmations are simulation records. The credit consumption service is internal to the backend. The financial document API will call it as part of the commitment transaction.

## Document action contract and acceptance criteria

The Documents endpoints implement the final actions below. The estimator informs plan selection. The API determines the charge and enforces permissions and commercial and issuance eligibility.

| Document | Frontend final action | Server action | Default credit effect |
| --- | --- | --- | --- |
| Sales Invoice | Save & Send | CommitAndSend | 1 on successful commitment |
| Credit Memo | Save & Send | CommitAndSend | 1 on successful commitment |
| Sales Order | Save | CommitOnly | 0, including when available balance is zero |

- Frontend actions follow the table. A Sales Order exposes only Save. The server rejects mismatched actions, including an invoice or Credit Memo submitted as CommitOnly, and derives credit applicability from the document type and authorized configuration.
- Successful Sales Invoice and Credit Memo commitments validate the document, permissions, applicable mappings, and credit balance. Commit the immutable document and one default credit debit together, enqueue accounting synchronization and applicable electronic delivery, and prepare only applicable authorized reporting. A Credit Memo requires the applicable original invoice reference and preserves that record.
- An entitled, authorized Sales Order with valid mappings saves at zero credit balance and queues accounting synchronization. Verify its synchronization status and audit history. Its wallet does not change and there is no credit check, reservation, delivery job, or BIR reporting job. Delivery, reporting, and credit consumption status are Not Applicable.
- Failed validation or commitment leaves the wallet unchanged. Concurrent or repeated Save & Send requests with the same idempotency key produce at most one commitment and debit. Verify transactional rollback and reuse of the committed result.
- Accounting synchronization, resends, electronic delivery retries, and reporting retries never debit the wallet. A delivery failure after successful commitment retains the original debit. Test the independent statuses for commitment, synchronization, delivery, reporting, and credit use.
- A committed Sales Order can be referenced by a new invoice. Verify that only the resulting invoice's successful Save & Send debits credits. The original order is preserved and unauthorized, duplicate, or excessive fulfillment is rejected.


## Company master data and financial previews

Routes are relative to `/api/workspace/{subscriber}/companies/{company}/master-data`. Customer sessions, company grants and the existing CSRF header apply. Mutations take the subscriber lock and recheck access.

| Route | Behavior |
| --- | --- |
| GET / | Authorized record definitions, editor fields, RDO references, permitted custom definitions, and statutory reference catalog. |
| GET /{kind} | Records with search, active and kind-specific filters, sorted columns and 25-row pages. |
| GET /{kind}/lookup; GET /{kind}/{id} | Scoped reference search and record detail with revision history. |
| POST /{kind}; PUT /{kind}/{id} | Create or revise a record. Updates include the previous revision. Changing the active status appends a revision. |
| GET /{kind}/file?format=csv or xlsx | Export customers or items, or set `template=true` to download the import columns with import permission. |
| POST /{kind}/import/preview, /confirm, /errors | Upload a single multipart `file`. Preview validates the file and returns a signed, short-lived `previewToken` if valid. Re-send the same file and token to confirm. The errors route returns a CSV report. |
| POST /resolve-price; POST /calculate | Resolve dated prices and UOM factors or return financial totals, due date and applied rule revisions. Use decimal strings and ISO calendar dates. |

Calculation results distinguish a Credit Memo reversal direction from its amount magnitude. The calculation transaction is read-only. It does not persist a financial form, issue a number, generate a transaction, or access the Save & Send wallet.


## Company document API

Routes are relative to `/api/workspace/{subscriber}/companies/{company}/documents`. Existing CSRF and scoped roles apply. Use `GET /{type}/options` for supported settings and options, `GET /numbering` to read authorized branch series, and `POST /numbering/{type}` to publish a series revision. `POST /{type}/preview`, `/pdf-preview` and `/email-preview` are read-only. The financial preview supplies the confirmation token for `POST /{type}/commit`, which requires a unique operation key and the same approved document input. Recover a committed response with `GET /commit-result/{key}`. The supported types are `SalesInvoice`, `CreditMemo` and `SalesOrder`. The company register (`GET /{type}/register`), its CSV export (`?export=true`), immutable detail (`GET /{type}/{id}`), issued PDF (`/{type}/{id}/pdf`), source lines (`/{type}/{id}/source-lines`), related links in the detail response, and `GET /delivery-history` are scoped reads. Resend and retry require an allowed operation and independently authorized recipient or existing delivery job. The development external-order fixture exercises the source interface without a live connection.

## Connector API contract requirements

Future vendor APIs keep import, validation, sandbox testing, approval, publication and revocation distinct. Imported endpoint or auth definitions cannot execute automatically. Client APIs list published, entitled versions, initiate account authorization, discover authorized files or shops, bind one per selected connection scope, configure and validate mappings, test, and activate with an audit trail. Return permitted capabilities, versions, connection status and sanitized diagnostics. Credential material and encrypted storage references stay server-side.

Return orders and original external identities as eligible source records. Quantity, cancellation, refund and duplicate checks apply when the company explicitly commits an invoice. Connector lookups, spec upload, configuration, test calls, sync and retries are zero-credit operations. Connector validation and runtime endpoints belong to the later phases in the master requirements.


### Phase 7 routes

The Vendor Super Admin Builder uses `/api/vendor/connectors` for the registry, choices, details, create and version updates. The `/{connectorId}/versions/{versionId}` suffix supports `import`, `validate`, `approve`, `publish` and `copy`. Uploads use multipart `file`, an `expectedRevision` query parameter and the existing CSRF header. Version mutations require the current revision. Publishing is restricted to approved development adapters. Connector-level disablement suspends running bindings. Lifecycle transitions through `POST /{connectorId}/lifecycle` block new bindings on deprecation and suspend existing bindings on retirement. Published versions remain pinned.

Client endpoints use `/api/workspace/{subscriber}/companies/{company}/connect`. Read the marketplace and individual connector details from `/catalog`. Development authorization uses `POST /catalog/{code}/authorize` and returns an expiring binding ticket and authorized fictional resources. Binding through `POST /connections` requires the ticket, version, resource, name and optional branch. Connection actions include test, mapping, activate, reauthorize and disconnect. The mapping read returns the field metadata and saved revisions. Connection responses expose status and version but omit credential reference details and ciphertext.

Active mock accounting connections expose searchable Sales Orders and development master data under the connection. Active mock stores import source records into `/source-orders`. The register and detail return source state and related invoice links. Eligibility is rechecked through the invoice provider interface at preview and commitment. No connect route issues documents or applies a credit debit. `GET` and `POST /bridge/registrations` manage authorized pending development agent metadata. `POST /bridge/registrations/{id}/revoke` revokes a registration in that company. These routes report the runtime as unavailable until an approved agent is implemented.


## Phase 8 mapping and accounting routes

Company Connect exposes `/mapping-fields/{direction}`, `/mapping-profiles`, and profile-level `/versions`, `/activate`, `/disable` and `/preview`. Save a new version against the current revision. A preview returns validation, sanitized source information, transformed payload, request operation and an expected response example. Previews use a synthetic document or an authorized committed document. Connection reference choices and updates use `/connections/{id}/references`. Suggestions require a company administrator to confirm them before saving.

Configure one automatic or manual accounting destination by document type and branch through `/accounting/destinations`. `/accounting/jobs` supports company, date, branch, type, connection, status and document-number views. `/accounting/jobs/{documentId}` returns scoped attempts and confirmed external identity. Its `/retry` action supports `retry` before submission and `verify` after ambiguous submission. An optional activated replacement mapping is accepted before submission for the same pinned destination. Neither action alters commitment or wallet data.

Vendor routes include `/api/vendor/connectors/mapping-templates` and `/runtime/options`, `/runtime/policies`, `/runtime/test` and `/runtime/monitor` under the vendor connector root. They expose the existing granular grants, approved revision-specific endpoints, sanitized test results, rate-limit diagnostics and aggregate operations.

Store retrieval accepts an optional bounded `limit`, a prior `after` cursor and a `changedSince` timestamp. A synthetic refund refresh appends a status observation and reduces eligible source quantity while preserving the external identity. The invoice handoff uses customer and item identity mappings. Source shipping, discounts, refunds and taxes are visible for review. Applying those amounts to an invoice still requires the existing authorized financial workflow.


## Phase 10 / 10.1 reporting preparation API

Routes are relative to `/api/workspace/{subscriber}/companies/{company}/reporting`. GET `/` accepts a one-based `page` (25 documents per page) and returns eligible invoices/credit memos, preparation/simulation indicators and server-derived actions. Mode is PreparationOnly; liveTransmissionEnabled and officialApiAvailable are false. GET `/{document}` returns authorized internal evidence, its schema version, SHA-256 and optional simulation. All government statuses remain NotSubmitted.

POST `/{document}/prepare` copies immutable committed evidence once; retries return the same preparation. POST `/{document}/simulate` is Development-only, requires an existing preparation and synthetic subscriber/document, validates local integrity and records SimulationValidated once. It does not imply BIR validation or acceptance. Reads require Reports.View; mutations also require Company.Manage. Every route checks subscriber and company scope; mutations require CSRF and revalidate permissions under the subscriber lock. Sales Orders and unavailable resources return 404. Simulation before preparation returns 409. No transmit endpoint exists. All operations consume zero credits and leave document processing states unchanged.

GET /readiness under the company reporting group summarizes taxpayer verification, registration evidence count and effective company and branch RDO assignments as of the Manila calendar date. The endpoint requires Reports.View and company access. It reports issuance and transmission as disabled. An RDO assignment is registration context and confers no authorization.

### Regulatory authorizations

Company routes are under /api/workspace/{subscriber}/companies/{company}/regulatory-authorizations. GET / lists paginated evidence metadata and grant-derived actions. POST / and POST /{id}/versions accept multipart metadata (JSON) and one evidence file. GET /{id}/history returns versions and review events. GET /{id}/versions/{number}/evidence checks scope and integrity before an attachment download. POST /{id}/versions/{number}/reviews requires a decision, reason and expectedSequence. GET /eligibility takes branch and documentType and returns the Manila evaluation date, applicable and expired records, limitations and disabled runtime decisions. New records require expectedVersion 0 and new versions require the current version. Invalid data returns 400, missing scope 404, and stale or contradictory changes 409.
