# EMBM eFiscal development demonstration

Use a dedicated local PostgreSQL development database, the simulated identity mailbox and fictional names and records. Start the API and frontend using the Codex Cloud commands in the README. Provision a synthetic local vendor with the documented bootstrap command. Run the Playwright suite serially against a fixed build. The suite creates fresh fictional subscribers and companies so another run does not require deleting records. Preserve prior runs and select a fresh isolated database when a blank environment is needed.

With the fixture environment loaded, run the scenarios below from the repository root. Use the generated Chromium configuration described in the README when running with Codex Cloud.

```bash
dotnet test Embm.EFiscal.slnx -c Release --no-build --filter 'FullyQualifiedName~BirConnector|FullyQualifiedName~ReportingJobs|FullyQualifiedName~TaxpayerExport|FullyQualifiedName~ContinuityReferences'
cd frontend
npm run test:e2e -- --workers=1
```

Use the browser-created corporate and individual accounts as examples. The document scenario supplies the invoice, email, PDF and export evidence. The Phase 9 scenario supplies the historical invoice, marketplace order and reconciliation records. The PostgreSQL connector and reporting scenarios supply the independent review, activation and retry evidence. These automated scenarios use independent synthetic accounts.

## Client flow

1. Register a fictional corporate taxpayer and confirm payment using the development checkout. Set up the head office and a second fictional branch. Confirm the illustrative RDO and use the separate regulatory authorization page to record supporting evidence. Use the independent authorized review flow to review the evidence.
2. Create one customer, an item, an approved synthetic VAT group and a shipping service. Save a Sales Order and check the wallet. Convert the order to a Sales Invoice, calculate totals, preview the PDF, and confirm Save & Send. Confirm one credit was used. Open the original PDF, download the canonical JSON and use **Verify Integrity**. Inspect the original delivery history in the simulated mailbox.
3. Connect the Development Accounting company file and publish its outbound mapping. Confirm synchronization, run an independent reconciliation, simulate a discrepancy and rerun after correction. Use Historical References to show the cutover exclusion and retrieve a supported mock invoice for a Credit Memo. Use a separately approved development commerce mapping to demonstrate shipping and the seller-funded discount.
4. Open **BIR Reporting Preparation**. Prepare the committed invoice. Observe the canonical and preparation counts, pinned payload and government status. In the development simulation queue, demonstrate acceptance, rejection, retry or status verification and duplicate prevention.
5. Open **Data Portability**. Export the inclusive business date of the demonstration transactions. Download the ZIP and inspect its manifest and checksums. Show the preservation and reporting continuity registers. A manual invoice reference records original identity for review and creates no fiscal document.

## Provider flow

1. Open the local vendor regulatory workspace. Show ESP readiness, application evidence, requirements review and infrastructure declarations. A development record does not confer approval.
2. In **BIR API Configuration**, import or enter the synthetic specification for the reserved simulator origin. Review the internal request and response envelope, run the approval and activation steps with appropriate independent grants, and execute a simulation. Use the aggregate queue counts to show operational state. Access one taxpayer's confidential evidence only through the assigned review case.
3. Show the provider billing record from the subscriber workspace. A confirmed payment supports the billing review and has no issued provider invoice number or verified tax amount.

Use this fictional configuration in the manual configuration field. Use the independent reviewer to record the validation and approval, and an authorized activator to activate it. Select **Accepted** for a fictional acknowledgment and **Validation Rejected** to inspect a fictional rejection. Use the client queue for a retry or status-verification exercise.

```json
{
  "efiscalConnector": 1,
  "servers": ["https://bir-simulator.example.test"],
  "endpoints": [
    {
      "method": "POST",
      "path": "/sim/submissions",
      "request": { "type": "object" },
      "response": { "type": "object" }
    },
    {
      "method": "GET",
      "path": "/sim/status",
      "response": { "type": "object" }
    }
  ]
}
```

## Status and dependencies

Development reporting uses an isolated simulator. Real endpoints, authentication, formats, acknowledgments, deadlines, production hosting and actual accreditation or certification require independent verification and approval. Refer to work on ESP accreditation and the requested system presentation as project-owner information. Keep the official reporting controls disabled. The deadline preview uses fictional rules and can never mark a taxpayer legally overdue.

| Dependency | Required evidence | Current gate |
| --- | --- | --- |
| Applicability and commencement | Authoritative issuance and reporting rules, effective dates, covered taxpayers and branches, and any historical or outage obligations | Regulatory requirement review and fictional deadline preview |
| Official connectivity | Government-issued specifications covering environments, authentication, signing, schemas, error and acknowledgment formats, and status and replay semantics | Internal preparation and in-process simulation |
| Accreditation and certification | Applicable ESP guidance, verified official authorization, and access to the designated certification process | Private development evidence register |
| Record preservation and recovery | Verified retention periods, approved manual series and recovery treatment, and authorization for any controlled disposal | Preserve-all policies, legal holds, and fictional incident references |
| Infrastructure and evidence | Approved residency and processing terms, production hosting evidence, and production file scanning | Private infrastructure declarations and development evidence handling |
| Provider billing | Verified EMBM taxpayer profile, approved service charges, verified tax and discount treatment, and issuance authorization | Confirmed payment and refund history |

The requirements registry references the BIR publications below. Retrieve their official versions and applicable amendments for source review. The cited digest could not be retrieved from the cloud environment. [RMC No. 98-2026 digest](https://bir-cdn.bir.gov.ph/BIR/pdf/RMC%20No.%2098-2026%20Digest.pdf) · [RR No. 11-2025](https://bir-cdn.bir.gov.ph/BIR/pdf/RR%20No.%2011-2025.pdf) · [RR No. 26-2025](https://bir-cdn.bir.gov.ph/BIR/pdf/RR%20No.%2026-2025.pdf)

Any future regulator-facing read contract must verify an explicit, current authorization for the taxpayer, company and permitted branches before selecting records. RDO and nationwide views require independently verified permissions. Selecting an RDO cannot expand a grant. The existing scoped records can support invoices, tax summaries, reconciliation, reporting and audit views under that future contract. The regulator sign-in remains disabled.
