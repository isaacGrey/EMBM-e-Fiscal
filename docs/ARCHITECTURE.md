# Architecture

The React frontend has a public website, subscription-linked registration, three sign-in experiences, and a customer workspace. It uses a relative `/api` URL. Vite forwards API calls to the loopback ASP.NET Core server in development and preview. A future deployment must route `/api` to the API and serve the SPA fallback for frontend routes.

`Embm.EFiscal.Api` is a single process. `Modules/Platform` contains public liveness. `Infrastructure` holds PostgreSQL provider registration, the shared EF Core context, and migrations. The API validates protected cookie sessions and checks tenant membership and company scope server-side for workspace endpoints. OpenAPI is development-only. Error responses use ASP.NET Core Problem Details and logs include a JSON console provider.

The EF Core provider requires a PostgreSQL connection string. The Identity and Tenants modules share the context. Email verification leaves the saved subscription selection at Pending Payment. The Billing module fulfills verified payments separately from identity and company configuration. The development-only fixture command supplies synthetic subscribers for onboarding tests. Documents owns fiscal commitment and the PostgreSQL outbox. Live connectors and regulator transmission remain separately gated. RabbitMQ can transport eligible work when the corresponding approved integration is available.

## Company configuration and delivery boundaries

CompanyConfiguration shares the existing tenant context. CompanyEmailSettings stores only non-secret provider and sender preferences. EmailTemplateManagement and DocumentTemplateManagement share scoped template metadata and append-only version rows. The workspace uses authenticated configuration endpoints, a restricted plaintext renderer for fictional email previews, a development mailbox adapter, and both a declarative browser layout preview and rendered sample PDFs. Company.Manage and company-file membership are checked on the API. Mutations revalidate under the subscriber lock. Expected revisions protect editing by multiple authorized employees.

The document worker generates and preserves PDFs after development fixture commitment. The commit transaction pins template content and enqueues durable work. The local adapter records delivery attempts without an external connection. Approved provider credentials, sender verification and outbound SMTP or OAuth remain gated. Keep PDFs distinct from structured regulatory payloads. Enforce destination and address policies before any future custom SMTP network access and keep encrypted provider secrets outside public configuration DTOs.

The external Sales Order interface currently uses a synthetic provider. Allocations preserve the source identity and company scope. Source lookup and form population remain read-only. Commitment independently validates remaining quantities and permitted partial invoicing under the subscriber lock. Live providers must be verified before activation.

## Subscription and credit engine

Billing owns the published catalog, checkout, payments and immutable payment events, paid subscription periods, renewal preferences, and the subscriber credit wallet. PostgreSQL stores exact PHP prices and pinned versions. The development gateway implements hosted checkout and signs simulated provider events. The server verifies the event and matches order scope and amount under database locks. Initial payment, subscription renewal, and credit top-up all use this fulfillment boundary. A verified payment that cannot provision successfully is recorded for review.

Subscriber row locks coordinate wallet allocations, expiry, reservation, consumption, and refund reversals with onboarding and seat changes. Checkout also locks the purchasing user row before locking an existing subscriber. Billing maintenance and authenticated balance reads catch up paid annual slices and expired reservations and lots. Plan changes are effective at a confirmed renewal. Billing evaluates current commercial eligibility separately from tenant membership and the historical access those permissions grant. Connector operations can use the pinned plan's connection limits, connector categories, and feature entitlements when implemented.

The wallet service joins the caller's PostgreSQL transaction for Phase 6. The document caller is responsible for immutable commitment, outbox creation, and regulatory authorization before committing an eligible reservation. Company and document history stay under existing tenant permissions. A statutory correction exception requires approved evidence, document-specific authorization, and an audit entry before it can bypass a billing restriction. Vendor credit adjustments currently add compensating credits with an actor and approval reference.

## Document commitment boundary

The server resolves the permitted action from the document type and rejects a mismatched requested action before any write. Sales Invoices and Credit Memos use `CommitAndSend`. Sales Orders use `CommitOnly`. Client-side rates are for estimation only. The API owns subscription checks, tenant and user permissions, applicable mappings, linked invoice and order validations, wallet eligibility, and the effective credit cost.

`CommitAndSend` stores the immutable document, applicable credit debit, idempotency result, and outbox work in the same database transaction. Validate available credits and enforce a single debit per operation even under concurrent requests. Replay the committed result for a matching tenant-scoped idempotency key and reject conflicting reuse. A failed transaction leaves no debit or outbox work. A Credit Memo preserves and references the original invoice. Invoicing against a Sales Order must validate authorized remaining invoicable quantities in the commitment transaction.

`CommitOnly` commits the immutable Sales Order and accounting synchronization outbox work without checking, reserving, or changing credits. Keep lifecycle events separate from the committed order. Track commitment, accounting synchronization, delivery, BIR reporting, and credit use separately. For Sales Orders, delivery, reporting, and credit use are Not Applicable. Worker retries and manual resends reuse the committed record without touching the wallet. Post-commit delivery failure updates delivery status while retaining the original debit. BIR transmission remains disabled pending formal authorization.

The Documents module implements this boundary with a subscriber lock, transaction-scoped fiscal resolution, effective branch number series, source allocations and durable outbox operations. PostgreSQL protects committed rows and issued files against mutation. A development worker claims jobs with leased row locks, generates the preserved PDF from pinned values, and uses a private synthetic mailbox. Accounting awaits a verified connection and regulatory reporting awaits authorization.

## Connector architecture requirements

Separate the vendor registry and its versioned, reviewed capabilities from company connection bindings and mappings. Publish approved versions as data and resolve entitlement-filtered marketplace entries on the server. The import and validation boundary creates inert administrative configuration. The runtime separately resolves the pinned version and capability, validates destinations and request and response contracts, attaches scoped credentials and dispatches a cloud or Windows bridge adapter. Use the existing Universal Field Mapping boundary for declarative transforms. Financial commitment and reconciliation continue to use authoritative committed and actual values respectively.

Prioritize MYOB Business, AccountRight Online, supported AccountRight Desktop, QuickBooks Desktop and QuickBooks Online. Retain AutoCount Accounting and AccountingSuite / 1C. The commerce targets are Shopee Philippines, Lazada Philippines and TikTok Shop Philippines. MYOB documents distinct service routes for eligible cloud and local files. Verify customer editions, Philippine availability and approved operations before activation. QuickBooks Desktop uses vendor-supported SDK or Web Connector mechanisms. QuickBooks Online uses app registration and OAuth consent for a company. Phase 13 production activation requires official provider evidence and authorized testing. Shopee’s developer portal is identified. Its Philippine order operations and authorization scopes still require verification with authorized partner access. [MYOB Business API](https://developer.myob.com/api/myob-business-api/api-overview/getting-started/) · [QuickBooks Desktop SDK](https://static.developer.intuit.com/qbSDK-current/doc/pdf/QBSDK_ProGuide.pdf) · [QuickBooks Web Connector](https://static.developer.intuit.com/resources/QBWC_proguide.pdf) · [QuickBooks Online authorization](https://developer.intuit.com/app/developer/qbo/docs/develop/authentication-and-authorization/oauth-2.0) · [Shopee Open Platform](https://open.shopee.com/documents/v2/OpenAPI) · [Lazada Open Platform](https://open.lazada.com/apps/doc/doc?docId=108260&nodeId=10777) · [TikTok Shop Partner Center](https://partner.tiktokshop.com/docv2/page/tts-developer-types)

### Phase 7 registry and development adapters

Connect stores connector definitions and versioned normalized configuration in PostgreSQL. The marketplace reads the latest published version. Connections and company field mappings refer to specific versions. The registered adapters execute only against synthetic accounting files and stores inside the Development process. The client flow binds the selected resource after an expiring, user- and company-scoped authorization ticket. The subscriber lock serializes entitlement checks and connection writes. The shared Documents service resolves eligible accounting and commerce source references through its existing provider interface.

Swagger 2.0, OpenAPI 3.x, Postman 2.x, and eFiscal v1 imports normalize to reviewable operations. Plain OpenAPI JSON and a bounded YAML subset are supported. Unresolved pointers, external references, executable content and unsupported features are handled at the import boundary. No spec-supplied endpoint is executed. [OpenAPI specifications](https://spec.openapis.org/oas/) · [Postman Collection format](https://schema.postman.com/)

The Windows bridge registration API records company scope, a SHA-256 public-key fingerprint, adapter metadata, agent version and revocation. The heartbeat time is reserved for authenticated traffic. An operational agent requires proof of possession of its registered key and outbound-only TLS to an authorized eFiscal destination. Future heartbeats carry an authenticated registration ID, monotonic sequence, UTC timestamp and installed adapter metadata. Check company scope and revocation before accepting traffic. Local accounting APIs remain inside the customer network.


### Phase 8 mapping and accounting execution

The Connect module adds separate outbound, inbound, and accounting response mapping profiles. A profile scopes its revisions to one company connection, connector version, document type and operation. A company can copy a published vendor template into its own version history. Reference choices and legacy development bindings are pinned with the mapping revision. Outbound rules run after financial commitment. They compare every protected financial field and line against the immutable document and resolve required external references through the pinned configuration.

Development routing selects one enabled branch or company destination for a document type. The commitment transaction records the existing accounting outbox row and any eligible destination and mapping pins. The accounting worker claims pending jobs with PostgreSQL leases. Actual mock record creation uses a separate transaction and a stable operation key. The worker reads that record back before recording confirmation and external identity. A lost or ambiguous response leads to readback or authorized verification. Posted records and uncertain submissions retain their original mapping. Linked invoices and credit memos wait for the related record in the same destination.

The API execution workspace builds requests from explicitly approved capabilities and reviewed operations. The Phase 8 transport runs in-process with zero outbound network access. Uploaded specifications never acquire execution permission on import. Vendor test results and monitoring contain synthetic or aggregate information. The mock provider retains separate observed store revisions for incremental retrieval and refund review.


## Phase 10 / 10.1 reporting preparation

Reporting is a separate module over immutable committed invoices and credit memos. An explicitly requested preparation copies the pinned fiscal evidence into an internal `embm-preparation-v1` envelope and stores its exact UTF-8 SHA-256 digest. It does not construct an official BIR payload. The preparation and optional local simulation are independent from document commitment, accounting, delivery and regulator status. No network transport, endpoint configuration, credentials or live-send switch exists.

Company reads require Reports.View and company membership. Preparation and simulation also require Company.Manage, revalidated under the subscriber lock. Simulation is mapped only in Development and requires both subscriber and document fixture flags. Future official mapping and transport must be separately reviewed against published BIR specifications and authorization; neither is assumed by this preparation milestone.

### Phase 10.3A authorization evidence

Reporting exposes a separate company authorization register and a preparation-only eligibility service. The service evaluates the current evidence version for the selected branch and document type using Manila calendar dates. Expired, suspended, revoked and unreviewed records are not included as current documented authorizations. It does not infer which permits apply to a taxpayer. Existing reporting preparation remains available independently of these permit records.

### Provider accreditation records

The development vendor portal has separate provider application, internal readiness, regulatory source review and material change registers. The registers store revisioned evidence for EMBM Services OPC and EMBM eFiscal. They have no taxpayer evidence access or runtime authorization effect. The project-owner report of a requested presentation is shown separately from the external decision record.

### Canonical electronic invoices

Sales Invoice and Credit Memo commitment stores an embm-invoice-v1 envelope in the same transaction and immutable evidence store as other document evidence. This envelope is separate from embm-preparation-v1 and cannot satisfy the preparation simulator. The canonical serializer writes compact UTF-8 JSON with ordinal object keys and preserves array order. Financial decimals use invariant strings. The financial snapshot hash covers the fiscalSnapshot object in canonical form. The artifact hash covers the exact download bytes. Operational generation metadata is outside both hashes. The payload uses pinned line calculation rules and document snapshots, and records current documented permit references at commitment when applicable on the document date. Legacy generation uses only committed data, reports missing historical evidence, and does not look up current authorizations.

### Internal schema registry and validation

The vendor registry shows immutable internal canonical and Development simulation versions separately. Certification and production categories remain disabled and contain no government schemas. Committed document reads match the pinned internal profile, verify both artifact and snapshot hashes, and validate exact decimal strings, identity, numbering, dates, source relationships, line consistency and totals. Missing regulatory evidence is a separate state from invalid internal data. Validation is read-only and does not recalculate or amend committed values.

Phase 10 taxpayer governance keeps read and preservation permissions separate from commercial issuance entitlements. The taxpayer export is a bounded, consistent copy of authorized company records. The serializable document flow remains the source of fiscal commitment. Continuity records describe outages and recovery without creating a fiscal document. Provider billing exposes confirmed subscription and credit payment records from the commercial ledger, independently of client taxpayer reporting.

### Development regulator portal

The Reporting module applies one active regulator grant to a scoped query over committed synthetic fiscal documents. Tax summaries and linked reconciliation, reporting and audit projections use the same fiscal scope. Each read and export saves its audit before returning the result. The local fixture commands provision and revoke grants independently of customer and vendor access. Production still requires approved identity and formal authorization.

### Connector release management

Published connector versions remain immutable. Release withdrawal is an append-only record that the runtime checks when resolving a connection pin. Connector-wide disablement and explicit resumption are independent of publication. The marketplace tracks forward publication, while pinned connections and accounting history retain their original version.
