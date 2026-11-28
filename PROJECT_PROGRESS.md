# Implementation progress

## Task 01 implemented

Initialized Git at `D:\EMBM-eFiscal`. Built a responsive public site, shared design system, illustrative credit estimator, sample client workspace, and distinct client, vendor, and BIR portal foundations. Account sign-in is disabled. The vendor and BIR shells expose no private records or usable actions. The BIR sign-in preview accepts an optional RDO context locally without granting scope.

Added the ASP.NET Core API host, process liveness, development OpenAPI, Problem Details, structured logging, optional EF Core PostgreSQL registration, and a Docker build definition. The client preview checks the local health endpoint through the Vite proxy. No database was contacted.

## Product positioning correction

The public site now presents EMBM eFiscal as a dedicated Philippine electronic invoicing SaaS from EMBM Services OPC. The hero, sample invoice workspace, six planned capabilities, integration and pricing sections, FAQs, and page metadata reflect that scope. The four named accounting products remain external systems, and Shopee and TikTok Shop are planned sources for eligible orders. Sales Orders are explicitly supporting commercial documents and do not automatically become reportable invoices. The master requirements and development instructions record the scope permanently.

The TypeScript and Vite production build passed. All nine Playwright tests passed against the production preview at desktop (1440px), tablet (820px), and mobile (390px) widths. The checks covered the revised hero and CTA, six capability cards, planned connector labels, Sales Order reporting language, estimator, and portal flows. Desktop and mobile full-page screenshots were inspected. Graphify's local source index was refreshed.

## Phase 1 verification

- TypeScript and Vite production build passed.
- .NET Debug and Release builds passed with zero warnings and errors. Four API integration tests passed in each configuration.
- Nine Playwright tests passed against Vite development and production-preview servers. The browser checks covered desktop (1440px), tablet (820px), and mobile (390px), estimator calculations and validation, FAQ and navigation, sample onboarding, live API liveness, dialog controls, and privileged access screens.
- Inspected full-page and viewport screenshots for desktop, tablet, and mobile. Chrome DevTools Protocol collected homepage console and network diagnostics and script/layout metrics. The production homepage checks found no browser warnings, errors, failed requests, or horizontal overflow.
- Graphify local code graph generated and queried: 236 nodes, 417 edges, and 11 communities. Outputs are local under `graphify-out/`.
- Docker daemon unavailable on the development machine. Image build and PostgreSQL connectivity were not verified.

## Phase 2 public website and subscription preview

Added dedicated Features, Integrations, How It Works, Pricing, FAQ, Resources, and Contact & Support pages using the existing public brand and homepage. All six connectors show Planned. Support requests can be prepared and copied locally.

Pricing uses one illustrative catalog for Starter, Business, and Enterprise, monthly and annual billing, allowances, integration eligibility, renewal, and top-up packages. The estimator calculates monthly and annual credits and recommends a plan using company file and user allowances. See the Save & Send correction below for the current credit rules. It selects an eligible plan from the catalog or reports when no plan fits. The registration preview carries the plan and billing period, validates B2B and B2C sample details locally, and clears details after validation. Client, vendor, and BIR login previews remain separate and locked.

Verified Phase 2 on the existing local frontend and API servers. TypeScript and the Vite production build passed. All 15 Playwright tests passed across desktop, tablet, and mobile, including monthly and annual billing, credit estimates and shortfalls, both registration classifications, return to Pricing, separate login previews, and public navigation. Browser and Chrome DevTools Protocol checks found no page errors or failed requests on the tested journeys. Registration produced no write requests or browser storage entries. Separately verified the support clipboard action and checked Pricing and registration at 320px. Inspected desktop and mobile screenshots under `frontend/TestResults/phase-2`. Refreshed and queried the Graphify source graph.

Commercial values and preview terms are illustrative. Live authentication, payments, provisioning, connector activation, support delivery, and government access are outside Phase 2.

## Save & Send credit model correction

Public pricing, the estimator, FAQs, and registration now use Save & Send Credits. Estimates count Sales Invoices and Credit Memos at the default rate of one each. Sales Order volume can be entered for context and contributes zero. Plan recommendations still consider company file and user limits.

The permanent specifications distinguish CommitAndSend for Sales Invoices and Credit Memos from CommitOnly for Sales Orders. The architecture and API contract document atomic and idempotent deductions, order conversion, independent statuses, and acceptance criteria for the financial and connector modules.

TypeScript validation and the frontend production build passed. All 15 Playwright tests passed across desktop, tablet, and mobile. The pricing regression checks that raising Sales Order volume to 100,000 leaves the credit totals and plan recommendation unchanged, while one additional Credit Memo can change the recommended tier. Verified the Buy Credits link and inspected the estimator at desktop and mobile widths. Graphify was refreshed.

## Phase 3 identity and onboarding

Implemented persisted B2B and B2C identities, subscription selection, verification and recovery email links, protected cookie sessions, account lockout, CSRF protection, and revocation. Verified accounts without a subscriber remain at Pending Payment. Local vendor bootstrap is explicitly gated to Development. BIR access stays disabled.

The existing EF Core context now has an additive identity and tenant migration. Company onboarding, private PDF evidence, effective-dated RDO assignments, branch setup, checked company and branch switching, subscriber and profile settings, and team invitations and membership permissions use authorized PostgreSQL records. Security-sensitive updates produce audit events. Only synthetic development subscribers were provisioned, using local database commands and a simulated local mailbox.

Verified the migration on dedicated loopback PostgreSQL development and test databases. No model changes are pending. The .NET Release build and frontend TypeScript and Vite build passed. Seven backend tests passed, including expired and consumed tokens, session expiration and revocation, cross-tenant denial, privilege escalation prevention, and production denial of development and government access. All 21 Playwright tests passed across desktop, tablet, and mobile. A separate browser smoke check verified local vendor bootstrap, customer scope denial, and sign-out. Reviewed browser screenshots and refreshed Graphify. Rechecked the desktop and mobile onboarding flows after clarifying the workspace navigation's active state.

Production mail, vendor MFA, official RDO data, payment and paid provisioning, live integrations, and government access are outside this verification. The Save & Send pricing correction remains in effect.

## Company workspace delivery configuration

The active company now has Invoicing Workstations and Delivery & Templates pages. The workstation entry points explain independent Sales Invoice, Credit Memo and Sales Order flows without opening financial entry. Company.Manage and current company access govern sender preferences, allowed copies, and email and PDF template configuration. Sender preferences accept no credentials and remain pending provider authorization. Validation does not resolve or connect to any provider.

Email templates support server-validated plaintext and allowlisted variables, fictional sample previews and a separately rate-limited development mailbox test with audit history. PDF layout settings support per-document defaults, a bounded inline PNG, fixed sample invoice fields, configurable layout, and browser previews. Saved template versions are append-only. Duplicate, default and restore actions remain company scoped. Migration `20261126072132_CompanyDeliveryConfiguration` adds three tables to the existing context.

The permanent requirements and planned module boundaries cover authorized live sending, provider secrets, issued PDF retention, external Sales Order sources and partial invoicing. These remain separate from this configuration milestone. No financial drafts, live provider connections, or credit use were added.

The migration was applied to isolated local development and test PostgreSQL databases, and the EF model has no pending changes. The .NET Release, TypeScript and Vite builds passed. All eight backend tests and all 21 Playwright tests passed across desktop, tablet and mobile. Tests covered company and role isolation, sender and template validation, stale edits, version restoration, fictional email previews, local test history and company switching. Refreshed Graphify.

## Phase 4 subscription, payment and Save & Send credit engine

Added the versioned PostgreSQL catalog for plans and credit packages, pinned orders and paid subscription terms, payment history, and one shared credit wallet per subscriber. The client can verify an account, complete a simulated checkout and proceed to company onboarding. The server provisions from signed, matched payment events. Paid renewals and top-ups allocate credits according to the pinned terms. Duplicate events are idempotent. Eligible refund simulations reverse unused credits.

The client workspace includes Subscription, Save & Send Credits and Billing History, with plan changes scheduled for confirmed renewal, renewal and top-up consent settings, filters, and CSV export. The separate local Vendor Super Admin workspace publishes new plan and package versions, reviews payment and wallet activity, and records audited positive credit adjustments.

The future document engine can reserve, release and consume credits within its commitment transaction. Sales Order Save requires subscription entitlement and consumes zero credits. Live payment providers, recurring collection, production vendor authentication, EMBM subscription tax invoices and the financial document commitment integration require their respective approvals or implementation phases.

Applied the additive commercial migration to the dedicated local development and test databases. EF reports no pending model changes. The .NET Release build and TypeScript/Vite build passed. All 12 PostgreSQL integration tests and all 24 Playwright tests passed. The browser suite exercised the homepage, signup, delayed confirmation, onboarding eligibility, duplicate top-up, CSV export, and payment confirmation across desktop, tablet and mobile. Rechecked the three billing browser tests after a table-spacing adjustment. A separate browser smoke test verified local vendor commercial access on desktop and mobile. Refreshed the Graphify source graph and report.



## Integration priorities and Universal API Connector Builder

The permanent specifications now prioritize MYOB Business, AccountRight Online, supported AccountRight Desktop, QuickBooks Desktop and QuickBooks Online, and retain AutoCount Accounting and AccountingSuite / 1C. They prioritize Shopee Philippines, Lazada Philippines and TikTok Shop Philippines for eligible authorized source orders. The approved Builder requirements cover safe import, reviewed and published connector versions, scoped connections and credentials, capability enforcement, declarative mapping, sandbox testing and separate Windows bridge adapters. Provider operations and target editions require official verification and authorization before activation. Connector implementation and marketplace publication remain assigned to Phases 7, 8, 12 and 13.

## Phase 5 company master data, tax and discounts

Added company-scoped customer and item records, groups, units and conversions, effective-dated prices, payment terms, currencies and authorized exchange rates, lightweight accounting dimensions, and typed custom field definitions. The portal supports search, filters, sorting, pagination, status changes and revision history. Customer and item spreadsheet workflows support CSV and XLSX templates, import validation and approval, error reports and authorized export.

The read-only financial service resolves company terms, uses exact decimal arithmetic, and returns separate sales classifications, VAT, discounts, withholding fields, and net due. It supports inclusive and exclusive VAT, commercial line and allocated document discounts, and Credit Memo preview direction. The Phase 6 document engine will use the returned versions and authoritative values for committed snapshots.

### Regulatory coverage

- The preview supports standard domestic VAT with the configured VAT registration, and separate non-VAT seller treatment. Supported exemptions are scoped to reviewed Section 109(R) publication conditions. The zero-rated preview covers the documented direct-export-of-goods scenario. [BIR RR 3-2024](https://bir-cdn.bir.gov.ph/BIR/pdf/RR%203-2024%20%28final%29.pdf) · [RA 11534](https://bir-cdn.bir.gov.ph/local/pdf/RMC%20No.%2089-2021%20RA%2011534.pdf) · [BIR RR 10-2025](https://bir-cdn.bir.gov.ph/BIR/pdf/RR%20No.%2010-2025%20%281%29.pdf)
- The verified senior citizen and PWD scenarios are limited to qualifying medicine for exclusive personal use with confirmed evidence, starting from the supported VATable or non-VAT treatment. The preview separates covered quantity, excludes VAT from the covered base and applies the applicable discount. Competing promotions require review. Solo-parent and athlete or coach references remain available for configuration review. [BIR RMC 71-2022 and interagency guidelines](https://bir-cdn.bir.gov.ph/local/pdf/RMC%20No.%2071-2022%20Annex%20A.pdf) · [RA 9994](https://lawphil.net/statutes/repacts/ra2010/ra_9994_2010.html) · [RA 10754](https://lawphil.net/statutes/repacts/ra2016/ra_10754_2016.html)
- The engine models creditable income withholding and withholding VAT separately. Production calculation for these components is disabled pending scenario-specific verification of payer, supplier, amounts and basis. BIR's separate CWT presentation requirement belongs in the Phase 6 invoice output. [BIR RR 24-2025](https://bir-cdn.bir.gov.ph/BIR/pdf/RR%20No.%2024-2025.pdf) · [BIR notice for RR 6-2026](https://bir-cdn.bir.gov.ph/BIR/pdf/PR114OCT1026.pdf)
- The source catalog identifies the supported preview date floor. A company records the independently configured effective dates and selects the verified scope it authorizes. The catalog is versioned in code and its references never automatically enable company rules.

### Phase 4 acceptance still outstanding

Automatic renewal and automatic top-up persist consent and configuration. Recurring collection, automated top-up charges, and production payment methods require an approved provider and authorized operational policies. Credit expiry and configurable rollover are implemented in the lot allocator and maintenance worker. Final commercial terms and production acceptance for expiration and rollover remain pending.

### Verification

Applied the additive master-data migration to isolated development and test PostgreSQL databases. EF reports no pending model changes. The .NET Release build, TypeScript validation and Vite production build passed. All 19 backend tests passed, including PostgreSQL master-data and tenant-isolation coverage, financial calculations and the Phase 4 regression tests. All 27 Playwright tests passed across desktop, tablet and mobile. The new browser flow verified customer entry, CSV import, Excel export, tax configuration and the calculation preview. The three master-data browser tests passed again after the final contrast adjustment. Chrome DevTools checks observed no browser or server errors in that flow. Refreshed Graphify and saved screenshots under `frontend/TestResults/phase-5`.


## Phase 6 electronic invoice engine and development delivery

### 6A document workflows

Added separate company workstations, registers and permanent viewers for Sales Invoices, Credit Memos and Sales Orders. Forms remain in memory. Direct invoices and invoices linked to eFiscal Sales Orders use the shared Phase 5 calculation service. The synthetic external-order adapter exercises authorized retrieval and preserves source identity and remaining quantities. Linked Credit Memos preserve the original buyer and fiscal terms and enforce quantity and value caps. Accounting work awaits an authorized connection. Sales Orders keep electronic delivery, regulator access and credits out of their lifecycle.

Commitment allocates an effective company or branch series and stores immutable fiscal rows, source allocations, the idempotency result, outbox work and the applicable debit in one PostgreSQL transaction. Save & Send costs one credit for each newly committed Sales Invoice or Credit Memo. Sales Order Save remains available at zero credit balance for entitled companies. The `20261126094118_CommittedDocumentsAndProcessing` migration adds database constraints and immutability guards.

### 6B PDF templates and original preservation

Sales Invoice and Credit Memo PDF layouts have independent saved versions, defaults and restore history. The editor supports a fictional server-rendered preview, line column order and widths, page and font settings, bounded company logo, financial summary, terms and footer. A document pins the content used at commitment. The renderer generates a watermarked synthetic document. The worker stores the original PDF and its SHA-256 hash. Authorized viewing, printing, download and resend reuse that preserved file.

### 6C local email and delivery

Company settings expose non-secret provider preferences, a rate-limited local sender test, local test history, and separate sender and provider status. Individual email templates use allowlisted invoice or Credit Memo variables with fictional previews. Durable processing records separate rendering and delivery states. The development adapter writes the pinned message and exact preserved PDF to a private mailbox with a stable per-job reference. Retrying or resending leaves the original charge unchanged.

Production regulated issuance, SMTP and OAuth sending, credential provisioning, official provider receipt handling and BIR connectivity remain gated to their verified provider and regulatory approvals. The current PDF layouts are synthetic. Philippine electronic invoice readiness requires the appropriate structured data and authorization, independently of PDF rendering. [BIR RMC No. 98-2026 digest](https://bir-cdn.bir.gov.ph/BIR/pdf/RMC%20No.%2098-2026%20Digest.pdf)

### 6D verification and remaining work


Applied the document migration to the development and test PostgreSQL databases. EF reports no pending model changes. The .NET Release build completed with zero warnings and errors. TypeScript and the Vite production build passed. All 26 PostgreSQL tests passed, covering earlier identity, billing and calculation behavior, concurrent commitments and quantity limits, numbering and rollback, unauthorized access, immutable records, original hashes, and recovery from simulated renderer and email failures. Inspected all four pages of the synthetic Sales Invoice and the Credit Memo with Poppler.

All 30 Playwright tests passed across desktop, tablet and mobile after restoring the Invoicing Workstations navigation entry. The new flow verifies zero-credit Sales Order Save, partial invoicing, direct issuance, linked Credit Memo commitment, PDF and email previews, original PDF download, delivery history and resend without a second charge. The company setup flow verifies sender testing and both email and PDF template types. Reviewed browser screenshots and Chrome DevTools diagnostics. Captures are under `frontend/TestResults/phase-6`. Refreshed and queried Graphify.

Production provider onboarding and verification, reviewed production invoice layouts, authorized structured reporting, and live accounting execution remain open. The local renderer keeps mandatory line columns visible. The current workspace uses linked Credit Memos for quantity returns and price adjustments. Corrections involving original statutory discounts or withholding are blocked for review. Additional order lifecycle controls and live connector synchronization history remain pending. The next connector implementation belongs to Phase 7’s registry, approved import and client connection boundary under the updated master requirements.


## Phase 7 EMBM eFiscal Connect

Added the PostgreSQL connector registry, marketplace, Universal API Connector Builder and company connection workspaces. Seeded the approved priority accounting and Philippine commerce listings plus two explicitly synthetic adapters. The Builder imports OpenAPI 3.x JSON and supported YAML, Swagger 2.0, Postman 2.x and eFiscal v1. It stores normalized review metadata, supports manual operations, validates the selected development adapter and separates approval from publication. Separate grants govern reading, editing and publication. Published versions stay immutable. Deprecation blocks new bindings. Disablement and retirement suspend existing bindings without changing history.

The development accounting connector provides company files, master-data lookups and searchable fictional Sales Orders. The development commerce connector provides authorized stores and an immutable, deduplicated source-order register. Company bindings pin the connector version and keep credentials scoped and protected. Company mapping revisions associate synthetic customer and item codes with active master records. Both adapters pass eligible source references into the existing transient invoice workstation and use existing calculations and commitment for issuance. Connector actions consume zero credits. Only successful new invoice or Credit Memo Save & Send consumes a credit.

The Windows bridge foundation stores pending scoped registration metadata, adapter versions and revocation information. A running agent, verified heartbeat transport and live connector execution are later work. The generic mapping runtime, synchronization, provider sandbox traffic and production credential exchange remain gated. Provider-specific editions, rights, eligible regions and commercial terms must be verified before any real connection is enabled.

Phase 6 still requires additional Sales Order lifecycle controls and reviewed Credit Memo adjustments for original statutory discounts or withholding. Production issuance, live email delivery and BIR transmission remain gated.

### Phase 7 verification


Applied `20261127003905_ConnectorRegistryAndCompanyConnections` and `20261127004608_ConnectorLifecycle` to the dedicated development and test PostgreSQL databases. EF reports no pending model changes. The .NET Release build completed with zero warnings and errors. TypeScript validation and the Vite production build passed. The development OpenAPI document exposes 28 Connector Builder and Connect paths. All 29 backend tests passed, including the Phase 6 financial regression suite, published version immutability, scoped authorization, source deduplication, bridge metadata revocation and zero-credit retrieval. Updated and queried Graphify.


The full Playwright run passed 32 of 33 tests and identified horizontal overflow in the mobile Builder. After constraining the wide tables to their own scroll regions, the complete Connector Builder, accounting connection and store-to-invoice workflow passed again on desktop, tablet and mobile. The three targeted runs and the full run together passed all 33 browser checks. Inspected final screenshots including mobile tables and checked Chrome DevTools for page and API errors. Captures are in `frontend/TestResults/phase-7`.


## Phase 8 Universal Field Mapping and accounting synchronization

### 8A Field and reference mapping

Added company-scoped inbound, outbound and response profiles for Sales Invoices, Credit Memos and Sales Orders. Profiles bind to a connection, connector version, operation and document type. The designer exposes searchable field metadata, validation and redacted previews. Typed declarative rules preserve exact committed financial values. Reference lookup decisions and vendor template copies are pinned in immutable revisions. The development field catalogue and references come from approved capability definitions and fixtures. Provider-specific metadata discovery remains gated.

### 8B Controlled execution and Vendor API Sandbox

Added reviewed operation policies, bounded request construction, sandbox authentication, schema checks, rate limits, error classification and sanitized test audit results. Approval binds an operation to its connector version and capability. The execution transport handles requests entirely in process with an isolated mock accounting store. Live provider traffic, approved production egress, provider credentials and operational Windows bridge access are deferred.

### 8C Accounting synchronization and external identity

Reused the transactional accounting outbox and added durable job claims, leases, pinned connector and mapping versions, attempts and readback confirmation. Each applicable company and branch configuration resolves one destination per document type. The mock accounting store creates durable records keyed to stable operation and destination identity. Ambiguous writes are verification-only. The worker preserves the original eFiscal record and number, tracks external identity separately and records material variance for review. Linked Sales Invoices and Credit Memos use confirmed same-destination parent identities. Inbound commerce refreshes preserve source identity and append status revisions, including refund and cancellation observations. Source amounts and adjustments are retained for review through the authorized financial workflow.

### 8D Monitoring and verification

Added company synchronization filters, attempt history, authorized retry and verification, external reference details in permanent document viewers, and aggregate Vendor statistics. Mock connector tests, mapping, retrieval, synchronization and retry use zero credits. The existing Sales Invoice and Credit Memo Save & Send debit rules continue to apply.

The .NET Release build completed with zero warnings and errors. TypeScript validation and the Vite production build passed. All 34 backend tests passed against isolated PostgreSQL, including new mapping, execution security, source refresh and synchronization tests. The complete pre-change Playwright baseline passed all 33 tests, including the Phase 7 mobile case. Graphify was refreshed.


Both `20261127010801_MappingExecutionAndAccountingSync` and `20261127012025_SourceStatusHistoryAndResponsePins` are applied to the development and test PostgreSQL databases. EF reports no pending model changes. The first complete Phase 8 browser run passed all 36 Playwright tests in 5.1 minutes, including the prior Phase 7 mobile overflow case and the new mapping-to-accounting flow on desktop, tablet and mobile. After tightening the pinned currency, tax and discount reference checks, the targeted Phase 8 browser test passed on all three layouts. It also exercised a custom header mapping with a redacted preview. The earlier Phase 7 Connector Builder test passed in a separate targeted mobile rerun. The full PostgreSQL suite passed again. Reviewed desktop, tablet and mobile screenshots and checked Chromium DevTools diagnostics. Captures are in `frontend/TestResults/phase-8`.

The next incomplete workflow is handing marketplace shipping and discounts into the invoice draft under approved financial rules. Provider-specific metadata and approved live provider transports remain open. Live issuance, live email, BIR transmission and operational Windows bridge functionality remain gated. Phase 6 statutory adjustment and Sales Order lifecycle limitations still apply. Phase 9 reconciliation remains pending.


## Official checkout continuation — 27 November 2026

Verified `D:\Projects\EMBM-eFiscal\EMBM-e-Fiscal` against `https://github.com/isaacGrey/EMBM-e-Fiscal.git`. The starting working tree was clean on main at e4e1ef004aeadc472e63d46d9825d03daf48ffc3 (Initial commit - EMBM e-Fiscal). Fetch confirmed zero commits ahead/behind origin/main. No commit or push was authorized or performed.

### Verified baseline and documentation gap

The transferred progress log ends at Phase 8. Its exact recorded incomplete increment is marketplace shipping/discount handoff under approved financial rules, with Phase 9 described as pending. The same repository already contains reconciliation coverage, evidence, endpoints, worker, screens and migration 20261127015051_ReconciliationCoverageAndEvidence. Their presence is not proof of full Phase 9 acceptance; the transferred checkout has no dedicated reconciliation acceptance tests or completed Phase 9 entry. Preserve this work. The owner's current instruction explicitly prioritizes Phase 10 and 10.1: BIR Regulatory Compliance and API-Ready Reporting Gateway.

Prior limitations remain: approved marketplace financial treatment, statutory Credit Memo adjustments, additional Sales Order lifecycle controls, operational Windows bridge, real provider transport, production payments/email/issuance and formally authorized government access. Do not infer completion from these local continuation checks.

### Phase 10 / 10.1 first preparation increment

Implemented company-scoped reporting preparation over committed Sales Invoices and Credit Memos. Internal embm-preparation-v1 evidence copies immutable fiscal snapshots and exact decimal strings and preserves its UTF-8 SHA-256 digest. The workspace lists eligible documents, preparation evidence and independently labeled local simulation. Sales Orders are excluded at both API and database boundaries. Reads require Reports.View and authorized company membership; preparation and simulation additionally require Company.Manage under the subscriber row lock. Read-only users cannot mutate.

Preparation is idempotent per document/schema and simulation is idempotent per preparation. Both records are protected against update/delete by PostgreSQL triggers. Simulation exists only in Development for synthetic subscribers and documents and confirms local evidence integrity only. Original invoice, PDF, delivery, accounting and regulator states remain unchanged. No reporting action changes the wallet. The government status remains NotSubmitted.

The owner reports that BIR's forthcoming API is unavailable. No government specification, authentication, payload, endpoint, acknowledgment or transmission transport was invented. BIR login, production issuance and live transmission remain disabled. The internal envelope is not an official BIR payload. Future transport and official mappings require verified specifications and authorization.

### Local environment and migration verification

Node 24.20.0, npm 11.19.0, .NET SDK 10.0.402, restored dotnet-ef 10.0.15, PostgreSQL 15.8 and Graphify 0.9.120 are available. npm ci completed with zero reported vulnerabilities. Existing private operator configuration outside the repository identifies the loopback PostgreSQL cluster on 15432 and separate efiscal_development/efiscal_test databases. No secrets were copied into source control. The original migration baseline was already applied. Applied additive migration 20261127053018_ReportingPreparationGateway to development and test without deleting existing data; EF reports no pending model changes. Docker CLI is installed but its Linux daemon is unavailable; the current local PostgreSQL/outbox runtime does not require it. RabbitMQ delivery is not activated by this milestone.

The API runs from this checkout at http://127.0.0.1:5108. Its health and Development OpenAPI contracts were verified. The frontend runs from this checkout at http://127.0.0.1:5174 because an unverified existing server occupies 5173. Browser tests use the fixed production preview on 4173. RDO fixture import was verified using an absolute path; README now resolves that path before dotnet run changes the working directory.

### Remaining Phase 10 / 10.1 work

This is the first preparation increment, not completed regulatory certification or full Phase 10 acceptance. Remaining internal work includes versioned company compliance review/authorization evidence, richer readiness validation with reviewed criteria, scoped preparation exports and operational review history. Official payload mapping, transport, delivery policy, government receipts and live testing remain blocked on actual BIR specifications and formal authorization. Nationwide/read-only regulator grants and their acceptance require their separately authorized phase. Continue from this preparation module while preserving the earlier recorded incomplete work.

### Verification results

The Release API build completed with zero warnings/errors and the frontend TypeScript/Vite production build passed. The final PostgreSQL API suite passed all 35 tests with zero skips. Reporting regression covers preparation and simulation replay, SHA-256 integrity, tenant isolation, authorized auditor reads with denied mutation, Sales Order exclusion, production simulation denial, absent transmission routes, immutable evidence, unchanged regulator state and zero additional wallet consumption. The existing connector test now asserts the already-implemented marketplace financial-review block and retains commitment coverage using the authorized accounting order; no financial guard was relaxed.

The initial local API test invocation omitted ConnectionStrings__Efiscal and therefore omitted optional document workers at host construction, causing worker timeouts. Re-running with both connection variables set to the isolated test database resolved them. README records this setup. Initial parallel browser checks hit shared IP/CSRF limits and one check encountered changing preview assets; security limits were retained and final browser verification runs sequentially against fixed assets. Reporting had already passed on desktop, tablet and mobile. The final full-browser result is recorded below. Screenshots are local under frontend/TestResults/phase-10 and Graphify outputs remain ignored under graphify-out.

Final browser verification passed all 36 Playwright tests in 5.4 minutes with one worker and zero skips, covering desktop, tablet and mobile. The reporting extension verifies preparation, local simulation, Sales Order exclusion, unchanged credit balance, no page/API errors and no horizontal page overflow. Inspected final reporting screenshots on all three layouts. The complete final API run passed all 35 tests in 2 minutes 4 seconds. Both development services remain running from the official checkout (frontend 5174, API 5108); the test-only preview on 4173 exited normally. Graphify was refreshed after structural changes: 3,395 nodes, 11,826 edges and 133 communities, with local AST extraction and no model API cost. git diff --check passes. main still points to e4e1ef0 with zero commits ahead/behind origin/main; all continuation changes remain uncommitted and unpushed.

## Phase 10.2 continuation

Checkpoint 0522cda preserved the previously verified reporting preparation and integrity simulation on GitHub main. The transferred source implements a development accounting read adapter, effective coverage and cutover exclusion, both reconciliation modes, missing-record findings in both directions, discrepancy comparison and persisted observations and review history. These are source findings. The transferred test suite has no dedicated historical reconciliation acceptance scenario. Only main was available locally and remotely after fetch.

The checkout contains reconciliation coverage and historical reference records. Full accounting migration, historical Sales Order handoff, Credit Memos against independent historical originals, and prior adjustment accounting have no verified end-to-end implementation here. Marketplace orders with shipping, discounts or refunds are deliberately rejected at invoice preview and commitment. No approved seller-funded discount mapping or marketplace settlement integration was verified.

The first Phase 10.2B increment exposes tenant-scoped taxpayer and branch readiness in the reporting workspace. It derives effective RDO context and evidence counts from existing company records and does not change the data model. Next, implement versioned authorization evidence and reviewer controls. Accreditation, regulatory requirement and schema registries, canonical invoice generation and download, dedicated BIR connector, reporting queue and outcomes, data portability and the demonstration workflow remain open.

Phase 10.2B readiness verification passed the .NET Release build with zero warnings or errors, TypeScript and Vite build, all 35 PostgreSQL API tests, and all 36 Playwright tests across desktop, tablet and mobile. EF found no pending migrations or model changes. Graphify was refreshed. The official local frontend and API are reachable on 5174 and 5108.

## Phase 10.3A authorization evidence

Added scoped taxpayer authorization records for six named authorization kinds and an Other category. Each record covers one branch and either Sales Invoices or Credit Memos. Evidence is versioned, and reviewer decisions append to history. Company owners and administrators submit. A separately assigned Regulatory Reviewer records evidence decisions, external documentation, suspension and revocation. An account owner assigns reviewers to selected companies. Manila-date eligibility excludes expired and inactive evidence and never enables production issuance or live transmission. The client workspace supports submission, history, review and SHA-256-checked attachment downloads. ESP accreditation belongs to the separate provider workspace.

Additive migration 20261127061232 applied to development and test. EF confirmed no pending model changes. The Release API and TypeScript/Vite builds passed. All 36 PostgreSQL API tests passed. All three targeted Playwright workflows passed on desktop, tablet and mobile, using Edge CDP and the local test runner. The browser tests exercise submission, history, download, and credit preservation. Independent review, expiry and revocation are verified by the PostgreSQL API test. The complete browser suite is reserved for Phase 10.3 acceptance. Graphify refreshed. RTK, Context7, Playwright MCP, Chrome DevTools MCP and shadcn MCP were unavailable in this session. The local Playwright runner, CDP and shared UI components were used instead. The Docker daemon is unavailable. The frontend and API are healthy at ports 5174 and 5108.

Next is Phase 10.3B, provider accreditation and readiness evidence, versioned regulatory requirements, and material change review. Canonical invoices and schema management follow. Production evidence malware scanning and verified official applicability policy remain open. Phase 9 recovery gaps remain as recorded above.

## Phase 10.3B provider accreditation

Added the Development vendor Regulatory Compliance workspace with versioned ESP application records, a 15-topic internal readiness checklist, a BIR source requirements register, and material change assessments. Scoped grants protect private, SHA-256-checked evidence files. The workspace identifies the system presentation as project-owner-reported. An outside decision can be recorded with documentary evidence and an application reference. No status establishes official accreditation or runtime eligibility.

The additive 20261127062039 migration applied to development and test. EF found no pending model changes. Both builds passed. The targeted PostgreSQL accreditation test passed. All three targeted Playwright workflows passed on desktop, tablet and mobile after correcting a browser pattern validation error. Graphify refreshed. Next is Phase 10.3C canonical invoice generation and commitment integration.

## Phase 10.3C canonical invoices

Save & Send now stores internal canonical JSON for committed Sales Invoices and Credit Memos in the same PostgreSQL transaction. Sales Orders stay excluded. The envelope pins financial values, parties, document relationships, line rule evidence and documented authorization references effective at both commitment and the document date. SHA-256 covers the exact canonical attachment and its fiscal snapshot. Legacy generation identifies uncaptured information and never fills it from current master data. Retrieval and retries add no credit use.

Release build, four targeted PostgreSQL tests and three targeted browser workflows on desktop, tablet and mobile passed. The tests cover exact amounts, original invoice links, historical buyer and permit snapshots, deterministic bytes, scoped downloads, reporting separation and wallet rules. The first test run exposed a fixture RDO effective after its fixed document date. The test now uses a document date covered by the fixture. No migration was required and EF found no pending model changes. Graphify refreshed. Next is Phase 10.3D schema registry, validation and viewer.

## Phase 10.3D schema and document interfaces

Added distinct internal canonical and Development simulation schema versions with stored content, checksums and history. The vendor registry shows the reserved official categories as disabled. The Sales Invoice and Credit Memo viewer can show structured data and JSON, download the exact committed bytes, request integrity verification, and show internal validation separately from missing regulatory evidence. The customer portal groups the existing taxpayer, branch, authorization and reporting pages under BIR Compliance. An invited, company-scoped Regulatory Reviewer can review evidence from the customer workspace.

The vendor workspace has provider accreditation, readiness, requirements, change review and schema functions. A scoped vendor-side workflow for tenant taxpayer verification and branch and authorization review is still open. Tenant review remains delegated through company grants. Production evidence malware scanning and official applicability policy remain open. Legacy generation reports absent historical inputs and no historical data is reconstructed. Keep Phase 9 recovery work as recorded above. Phase 10.4 integration depends on verified official specifications and formal authorization.

## Phase 10.3E verification

The .NET Release build passed with zero warnings and errors. TypeScript and Vite passed. All 38 PostgreSQL API tests passed with no skips in 2 minutes 11 seconds. All 36 Playwright tests passed in 4.5 minutes with no skips across desktop, tablet and mobile. The browser run covers invited reviewer access, evidence submission and download, provider workspace, material changes, schema visibility, structured Sales Invoice and Credit Memo views, internal validation and integrity checks. Existing financial, wallet, reporting, accounting and tenant isolation regressions passed. Dedicated Phase 9 historical reconciliation acceptance remains unverified.

Three additive migrations applied to local development and test. EF found no pending migrations or model changes. Graphify refreshed to 3,625 nodes, 12,540 edges and 132 communities. The frontend and API are healthy on ports 5174 and 5108. Phase 10.3 development regression passed for the shipped scope. The open vendor-side tenant review, official regulatory dependencies and evidence scanning limits above remain outside that verified scope.

## Codex Cloud onboarding

Prepared the official checkout on Debian 13 with .NET SDK 10.0.404, EF CLI 10.0.15, Node 24.22.3, npm 11.19.1, and PostgreSQL 17.12. The API and Vite proxy returned the development health and database-backed catalog. Both isolated PostgreSQL databases have all 14 migrations, and EF found no pending model changes. The Release build, TypeScript and Vite build, and all 38 backend tests passed.

Graphify 0.9.120 generated and queried a local code graph. RTK 1.2.1 ran direct Git commands and preserved the native exit status. The standard Caveman skill is local to the cloud checkout. Use native Git, Playwright 1.65.1, Chromium 156 and CDP for this workflow. The cloud task exposed no Context7, Playwright, Chrome DevTools or shadcn MCP tools. GitHub fetch through the configured origin worked.

All 36 Playwright cases passed with zero skips across desktop, tablet and mobile, including the vendor scenarios using a synthetic account. The pricing smoke check found no browser console errors. The saved refresh reused unchanged npm dependencies and up-to-date NuGet restore outputs. PostgreSQL, the API and Vite restarted successfully, and the database-backed catalog remained available. The environment draft contains the Linux install and startup instructions and is ready for publication review. This checkpoint covers onboarding.

## Phase 9A and 9B recovery checkpoint

The official Git remote exposed only main. Reused the existing coverage, independent development accounting readback, reconciliation worker, and append-only evidence. Dedicated PostgreSQL tests passed for inclusive coverage dates, adoption defaults, optional end dates, both modes, issuance exclusions, historical counts, and prior findings retained after a coverage change. Independent readback tests passed for exact match, VAT, discount, quantity and total variances, missing and duplicate records, partial evidence, incomplete retrieval, unavailable connections, immutable snapshots, freshness, and resolution after successful verification. The tested reconciliation calls consumed zero credits and created no additional accounting postings.

The overview excludes stale results from fresh conclusive outcomes. Batch rate-limit retries retain the completed cursor and resume at the failed document. Discovery no longer resolves a missing-record finding on an identity link alone. Dedicated worker recovery acceptance and the remaining migration, historical references, marketplace, and browser workflows still require verification.

## Phase 9C–9E recovery checkpoint

Added explicit accounting destination windows with inclusive business dates, overlap rejection, authorized closure of the prior open window, and scoped mapping selection. A committed synchronization pin keeps its original destination. Historical Sales Order handoff retains the external company, source account and line quantities. A PostgreSQL test verified both sides of the cutover and partial historical order quantities.

Historical Credit Memos now support a verified development accounting ledger with one original PHP VATable line and complete prior adjustment evidence. The API re-reads the scoped original at preview and commitment, deducts external and prior local credits, and reuses the locked document commitment and credit path. Tests covered partial return and amount adjustment, excessive and concurrent commitment rejection, incomplete history, and accounting identity. Live provider consistency, statutory discounts, withholding and complex currency treatment remain gated.

The versioned commerce source mapping supports the approved synthetic seller-funded discount and distinct VATable shipping service. Marketplace fees and settlement remain frozen as source evidence. PostgreSQL verified PHP 582.40 invoice revenue, PHP 562.40 settlement, source and mapping pins, the one-credit charge, duplicate prevention and blocked refund treatment. The destination, credit reference and marketplace mapping migrations are additive.

### Recovery classification

- Present and previously verified: committed-document immutability, credit accounting, scoped outbound identities, source retrieval and original PDFs. These remain part of regression acceptance.
- Present and previously unverified as Phase 9 workflows: coverage configuration and history, independent development accounting comparison, jobs, discrepancy review, reports, and document viewer integration. Dedicated PostgreSQL checks and a cross-device browser scenario exercise these paths.
- Partial before recovery: stale verification metrics, rate-limited batch progress, resolution after fresh evidence, accounting migration, and historical Sales Order provenance. The recovery adds the missing controls while retaining prior evidence.
- Missing before recovery: approved accounting destination windows, the complete-evidence development historical Credit Memo flow, and the versioned seller-discount and shipping mapping. The recovery adds these supported paths and blocks unsupported terms before commitment.
- Externally blocked: live accounting and marketplace providers, authoritative financial treatments for unsupported source components, and safe shared consistency for mutable historical adjustment ledgers. The verified development fixtures do not authorize production posting, issuance or BIR transmission.

The Release build and complete PostgreSQL suite passed all 44 tests, including six dedicated Phase 9 acceptance tests. The additive migrations are applied to both development and test databases. Both hold 17 applied migrations and EF reports no pending model changes. The durable batch test verified scheduled and post-sync runs, rate-limit cursor preservation, expired-lease recovery, and zero extra credit use. The browser regression is running with the coverage scenario waiting for the specific newly activated revision.

## Phase 9F development acceptance

Phase 9, 9.1 and 9.2 passed the supported development acceptance. The complete PostgreSQL suite passed 44 tests and the complete Playwright suite passed 39 cases across desktop, tablet and mobile, with zero skips. The browser coverage includes both reconciliation modes, historical exclusions and references, cutover administration, historical Credit Memo and marketplace invoice issuance, comparison and resolution, exports, and unauthorized access. The .NET Release, TypeScript and Vite builds passed. The suite also exercised the existing Phase 10 regulatory workflows. Graphify refreshed locally to 3,678 nodes and 12,962 edges.

The development and test databases each have 17 applied migrations, including the three additive Phase 9 migrations, with no pending EF model changes. Production connectivity, unsupported marketplace treatments, and historical adjustment scenarios without complete provider evidence or safe consistency remain gated.

## Phase 10A vendor review checkpoint

Added opt-in review cases tied to a single company authorization and branch. Vendor case management grants can enroll and assign independent reviewers without exposing evidence to the manager. Expiring assignments and append-only revocations govern read and write access. Clients submit replacement evidence through the existing versioned history. Internal vendor decisions cannot document external authorization or enable runtime eligibility. Evidence downloads check scope, scan state and SHA-256 and record an audit event. Scanning is a labeled synthetic development fixture. Production scanning remains unavailable and file retrieval fails closed.

Two focused PostgreSQL authorization tests passed. The new test covered scoped assignment, quarantine, clarification and replacement evidence, internal approval boundaries and expired or revoked access. TypeScript validation passed. Source retrieval from the BIR CDN returned HTTP 403 for the three previously cited publications. Official applicability stays under review pending verification.

## Phase 10B regulatory connector checkpoint

Added the separate development BIR API Configuration workspace and immutable configuration, lifecycle and test history. It accepts bounded OpenAPI and declarative synthetic connector imports, with a reserved simulator origin and an exact method and path allowlist. Official records can hold pending documentation metadata. The implementation registers no network transport and accepts no authentication credentials. Independent users validate and activate synthetic versions. Test payloads use an internal versioned envelope and simulated responses never establish government acknowledgment.

The dedicated PostgreSQL connector test passed, covering unsafe reference and origin rejection, executable metadata rejection, YAML import, authorization separation, immutable versions and official activation denial. TypeScript validation passed.

## Phase 10C durable reporting checkpoint

Added explicitly selected, company-scoped preparation and development simulation jobs. Each simulated operation pins the canonical hash, schema, mapping and synthetic connector version. PostgreSQL claims and leases recover interrupted work and retain immutable attempt hashes. Safe retries, delayed acknowledgment, partial batch outcomes and status-only verification are supported. Official transmission, certification, and automatic historical submission remain unavailable. Existing synchronous preparation and integrity simulation remain compatible.

A PostgreSQL end-to-end worker test passed. It exercised preparation, duplicate operation replay, an uncertain outcome verified without resubmission, delayed acknowledgment, rate-limit retry after an expired lease, and partial batch results. It verified unchanged reporting credits and fictional acknowledgments. TypeScript validation passed.

## Phase 10D–10F governance continuation

Fast-forwarded the existing work branch to `a0cc483` after preserving the overlapping Phase 9 edits in Git stash `bf32b34`. Used separate development and test PostgreSQL databases for the new migration history. The 10A–10C baseline Release build and four focused reporting and accreditation tests passed.

Existing taxpayer document, canonical data, PDF and reporting reads stay company-scoped after commercial cancellation. New commitment retains the commercial gate. Added bounded taxpayer ZIP export with a manifest and file integrity checks, immutable export history, download auditing, retention policy revisions, and scoped legal holds with owner-only release. Dedicated PostgreSQL tests passed archive and per-file checksum checks, cross-company and cross-tenant download rejection, post-cancellation access, zero extra credits and hold release authorization.

Added incident revisions, synthetic manual invoice references, fictional Manila deadline preview and a private infrastructure evidence register. The provider billing view derives confirmed charges and refunds from the existing commercial ledger. Provider tax values and issued invoice references require verified authorization. The client shows preparation counts and separate governance navigation. The vendor connector shows aggregate queue, incident, hold and export counts without taxpayer identifiers.

Production retention and recovery rules, provider tax treatment, official API and certification, infrastructure approval and applicable jurisdiction-specific obligations still require verified evidence. Retrieval of the cited BIR digest was blocked by the cloud egress policy. No authoritative filing or recovery dates were inferred.

## Phase 10G presentation verification

Added [the development demonstration guide](docs/BIR_DEMONSTRATION.md) with repeatable fictional fixture setup and a simulator specification. A Chromium rehearsal imported that specification, opened its request and response schemas, used three independent synthetic operators for creation, review and activation, and displayed fictional acknowledgment and rejection while the government state remained `NotSubmitted`. The test connector was disabled at the end. The application and worker tests use separate synthetic accounts for customer, accounting and retry coverage. The complete 34-step presentation across both customer and provider systems still needs a single continuous rehearsal.

The provider billing foundation exposes subscription, renewal and credit purchases when present in confirmed checkout history. Provider taxpayer identification, approved service charges, verified tax and discount inputs and legal issuance are open. The next internal verification is the continuous presentation with one curated set of connected fictional records. Official API contracts, production scanning, infrastructure approval and formal government authorization remain external dependencies.

Release build passed with zero warnings and errors. TypeScript and Vite build passed. Both isolated PostgreSQL databases have all 22 migrations applied, including the two additive governance migrations. EF found no pending migrations or model changes. The complete PostgreSQL backend suite passed 52 of 52 tests with zero skips. The complete Chromium suite passed 39 of 39 cases with zero skips, 13 each on desktop, tablet and mobile. The browser regression covered the archive download, vendor governance counters, and the Phase 9 historical invoice, marketplace and reconciliation workflows. Screenshot review caught a wrapping download button on tablet. The corrected button passed the full rerun on all three layouts.

Implementation checkpoint `13e0f2e` was pushed and verified on `main`. The shipped Phase 10 development workflows passed regression. Full Phase 10 acceptance remains open pending the continuous presentation rehearsal and completion of provider billing evidence. Live transmission and formal production regulatory acceptance remain gated. The Phase 11 contract is documented for future use and has no runtime endpoints.

## Phase 11 development regulator portal

Started on `Development` from `49b9ec4`. Added a separate opt-in synthetic regulator identity, local grant fixture commands, and a read-only portal for committed invoices and credit memos, tax summaries, linked reconciliation and reporting evidence, and fiscal audit history. Each read or authorized JSON export resolves an active grant and records its scope. Company grants can pin a branch. RDO grants use document-date assignments with branch precedence. Nationwide fixture access requires an explicit grant.

Migration `20261128114041_RegulatorReadAccess` adds grants and audit records with database protection for revocation history and append-only reads. It applied to the development and test databases, which each have 23 applied migrations. EF found no pending model changes. The Release and TypeScript/Vite builds passed. Synthetic Chromium sign-in, search, tax summary, export and revocation passed across desktop, tablet and mobile. The complete regression suites are running on this checkpoint.

The local databases had recorded the first migration before its protection SQL was present. Additive migration `20261128115156_EnsureRegulatorReadIntegrity` installed the triggers in both databases, now at 24 migrations. A rolled-back PostgreSQL check confirmed that both reopening a revoked grant and modifying a recorded read audit are rejected. The Phase 11 regression test also exercises these protections.

Production regulator sign-in and record access require verified regulator authorization and approved MFA identity integration. The fixture guide records the setup and the current boundaries. [Regulator portal guide](docs/REGULATOR_PORTAL.md)
