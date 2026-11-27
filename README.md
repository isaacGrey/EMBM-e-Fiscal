# EMBM eFiscal

Invoice. Integrate. Comply.

EMBM eFiscal is a dedicated Philippine electronic invoicing SaaS developed by EMBM Services OPC. Its scope covers electronic invoice issuance and delivery, Philippine tax configuration, secure invoice records, and BIR e-Invoicing readiness. Field mapping, accounting synchronization, invoice reconciliation, and Save & Send credit subscriptions support electronic invoicing alongside the accounting software a business already uses.

The public site reads illustrative plans and credit packages from a versioned PostgreSQL catalog and includes a separate sample workspace. Registered customers can verify email and sign in. The customer workspace uses PostgreSQL records for eligible subscribers. Listed accounting and e-commerce connectors are planned, and live BIR transmission is disabled pending formal authorization.

Requires Node.js 24, the .NET 10 SDK, and a dedicated PostgreSQL 15+ development database. Check ports 5173 and 5108 before starting services.

Configure a local PostgreSQL role and an empty database named `efiscal_development`. Set the connection string and simulated mailbox in the API terminal from the repository root. Use operator-supplied local credentials.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__Efiscal = 'Host=127.0.0.1;Port=15432;Database=efiscal_development;Username=<local-role>;Password=<local-password>'
$env:Identity__DevelopmentMailbox = Join-Path $env:LOCALAPPDATA 'EMBM-eFiscal/identity-mail'
dotnet tool restore
dotnet ef database update --project backend/Embm.EFiscal.Api --configuration Release
dotnet run --project backend/Embm.EFiscal.Api --configuration Release -- --import-rdos (Resolve-Path infrastructure/dev-rdos.json).Path
```

The RDO import supplies fictional local references for onboarding. It is optional for registration. Use a verified official export and source provenance before production use. Review generated migration SQL before applying changes to a persistent environment. [EF Core migration guidance](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying?tabs=vs)

If the API and frontend are not already running, start them in separate PowerShell terminals. Use the configured environment in the API terminal.

```powershell
dotnet run --project backend/Embm.EFiscal.Api --configuration Release
```

```powershell
Set-Location frontend
npm.cmd ci # Initial setup only. Skip while Vite is active or dependencies are installed.
npm.cmd run dev
```

Open [the homepage](http://127.0.0.1:5173). [API health](http://127.0.0.1:5108/api/health) and [development OpenAPI](http://127.0.0.1:5108/openapi/v1.json) run on port 5108. The frontend proxies API requests.

Select a plan on [Pricing](http://127.0.0.1:5173/pricing) to register a test identity. Open the verification or password reset link from the private simulated mailbox JSON files. After verification, sign in as the customer, select Continue to checkout, and simulate a successful payment. That confirmation provisions the subscriber, assigns the Account Owner role, and funds its Save & Send wallet. Use the Subscription, Save & Send Credits, and Billing History links to inspect the result. Simulation supports payment decline, cancellation, delayed confirmation, duplicate confirmation, and refunds for unused credits.

For test automation that needs a synthetic onboarding account without a paid subscription, run this after verifying its email.

```powershell
dotnet run --project backend/Embm.EFiscal.Api --configuration Release --no-build -- --provision-dev test-owner@example.test
```

The checkout simulator and vendor commercial controls are available in Development. Live checkout, recurring charges, and production payment methods require an approved provider. The fixture command operates only in Development against a dedicated loopback eFiscal database. It grants a development subscriber membership without issuing credits. For vendor testing, explicitly set `Identity__EnableLocalVendorLogin=true` for the API and bootstrap process. Supply an operator-selected password as `Identity__LocalVendorPassword` in that bootstrap process. Run the same command with `--bootstrap-local-vendor` and your chosen local admin email, then remove the password variable. [Vendor Sign In](http://127.0.0.1:5173/vendor/sign-in) is separate from [Client Sign In](http://127.0.0.1:5173/sign-in). [BIR Sign In](http://127.0.0.1:5173/bir/sign-in) stays disabled.

From the root, run `dotnet build` and `dotnet test`. Set `EFISCAL_TEST_DB` to a separate loopback PostgreSQL database named `efiscal_test` to run the PostgreSQL integration tests. The tests apply the migration and use uniquely named synthetic records. For Playwright identity tests, set `EFISCAL_DEV_MAILBOX` to the API mailbox directory and set `ConnectionStrings__Efiscal` in the test process to the same development connection. They require a Release API build for the fixture command. From `frontend`, run `npm.cmd run build` for TypeScript and the production build. With both services running and Microsoft Edge installed, run `npm.cmd run test:e2e`. Set `EFISCAL_PREVIEW=1` in the test environment to run against the Vite build preview. Set `EFISCAL_SCREENSHOTS` to an absolute output directory to save browser screenshots. Run `graphify extract . --code-only` from the root to update the local source graph.

In a paid synthetic company, add customers, items, authorized company tax settings and an effective numbering series for each supported document type. Save a platform sender with a `.test` address before using local Save & Send. The Documents workstations calculate and preview in memory. Sales Order Save uses zero credits. A successful development Sales Invoice or Credit Memo Save & Send uses one credit and queues PDF rendering and a private local mailbox delivery. The original PDF is available in the permanent document viewer. Production issuance, live email, accounting connections and BIR submission remain gated.

In a paid synthetic company, select Integration Marketplace. Authorize a demo account, bind a company file or store, test the connection and map its fictional customer and item to active company master records. An active connection can retrieve source orders for the Sales Invoice workstation. In the local vendor portal, Universal API Connector Builder can review a specification and publish an approved mock version.

The Connector Builder browser test uses a bootstrapped local vendor account. Set `EFISCAL_PHASE7_VENDOR_EMAIL` and `EFISCAL_PHASE7_VENDOR_PASSWORD` in the test process alongside the existing mailbox and database settings. Use a dedicated synthetic account.

For development synchronization, use a company binding to Development Accounting v2. Create an outbound field mapping, activate its version, and choose it under Synchronization Center for the required document type and branch. The permanent viewer displays confirmed mock identities and attempt history. The vendor workspace exposes published mapping templates, the isolated API sandbox and aggregate monitoring.

See [the master requirements](MASTER_SPEC.md), [progress](PROJECT_PROGRESS.md), and [the API contract](docs/FRONTEND_BACKEND_CONTRACT.md).

## Local continuation on the official checkout

The official local directory is `D:\Projects\EMBM-eFiscal\EMBM-e-Fiscal`. Use this checkout for all commands. An unrelated or unverified server may already occupy 5173; explicitly start this frontend with `npm.cmd run dev -- --port 5174` in that case. Its API proxy still targets 5108. Do not stop an unknown process to free a port.

For PostgreSQL tests, set both `EFISCAL_TEST_DB` and `ConnectionStrings__Efiscal` to the same dedicated loopback `efiscal_test` connection in the test process. This ensures optional database workers are registered during host construction, before fixture configuration overrides. Keep development and test database connections distinct. Run the full browser suite sequentially (`npm.cmd run test:e2e -- --workers=1`) against a fixed build to avoid exhausting shared loopback rate limits. Do not rebuild preview assets while browser tests are running.

The company workspace now includes BIR Reporting Preparation. Authorized administrators can prepare internal evidence for committed invoices/credit memos and validate its integrity with a Development-only synthetic simulation. This is not an official BIR payload, submission or acceptance. Preparation and simulation consume no credits. The forthcoming BIR API, production issuance and live government transmission remain unavailable.

### Phase 10.3 development workspaces

Use BIR Compliance in the client portal to manage company and branch authorization evidence. Owners assign the Regulatory Reviewer role to a different account and select its covered companies. Open a committed Sales Invoice or Credit Memo to view or download its internal structured invoice. Older documents can generate internal JSON from their committed snapshot. Use the Development vendor portal to manage provider evidence, review regulatory sources, and inspect published internal schema versions.
