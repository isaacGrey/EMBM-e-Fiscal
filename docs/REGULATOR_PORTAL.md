# Phase 11 regulator read-only portal

The portal is a development fixture for reviewing scoped fiscal records. Production regulator sign-in and the regulator records API are disabled. Any production rollout requires formal authorization, the approved identity provider with MFA, independently recorded grants, and verified scope and audit requirements.

## Run a synthetic exercise

Use a dedicated loopback eFiscal development database. Apply the EF migrations and use the existing development checkout to produce a fictional committed Sales Invoice or Credit Memo. Keep the original subscriber, company and branch identifiers available for the grant.

Start the API with `Identity__EnableLocalRegulatorLogin=true`. To create a separate synthetic identity, supply an operator-selected password through `Identity__LocalRegulatorPassword` in the maintenance command process. Keep that value out of configuration files and source control.

```bash
dotnet run --project backend/Embm.EFiscal.Api -c Release --no-build -- --bootstrap-local-regulator regulator@example.test
dotnet run --project backend/Embm.EFiscal.Api -c Release --no-build -- --grant-local-regulator /path/to/private-grant.json
```

The second command prints the grant ID. A grant file uses the following fields. Use your synthetic company IDs and set the start and expiry for the exercise. The expiration must be within 90 days.

```json
{
  "email": "regulator@example.test",
  "scope": "Company",
  "subscriberId": "10000000-0000-0000-0000-000000000001",
  "companyId": "10000000-0000-0000-0000-000000000002",
  "branchId": null,
  "rdoCode": null,
  "categories": ["documents", "tax-summary", "reconciliation", "reporting", "audit"],
  "canExport": true,
  "reference": "Synthetic authorization exercise",
  "startsAt": "2026-11-28T00:00:00Z",
  "expiresAt": "2026-11-29T00:00:00Z"
}
```

For a single-branch exercise, specify the branch from that company. For an RDO grant, set `scope` to `Rdo`, supply an active fixture RDO code, and leave the subscriber, company and branch fields null. An explicit `Nationwide` fixture grant leaves all four fields null. RDO and nationwide grant holders still specify one taxpayer subscriber and company per search.

Sign in at `/bir/sign-in`, select the grant, choose a business date range and category, and search. Use **Export this page as JSON** if the grant permits it. The export carries the resolved scope. Tax summaries group stored totals by currency and document type, showing invoice and credit memo magnitudes separately.

Revoke a grant by passing its ID to `--revoke-local-regulator` using the same command form. Repeat the search using the existing browser session to verify rejection. New grants use new IDs. Turn off the fixture setting for routine development.

## Scope and audit

The server rechecks identity, grant ownership, validity dates, scope, category and export permission on every record request. RDO matches use the fiscal document's business date and prefer an effective branch assignment over the company assignment. The requested RDO only narrows scope. Searches contain only committed synthetic invoices and credit memos. Reconciliation, reporting and audit views are restricted to evidence linked to those documents.

Every successfully authorized read or export records the grant, identity, resolved filters, time, page and result count. PostgreSQL preserves those rows and allows issued grants to be changed only by revocation. The portal exposes limited fiscal projections and does not include buyer addresses, raw reporting payloads, evidence files or administrative operations.
