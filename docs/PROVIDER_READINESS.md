# Phase 13 provider readiness

Use **Vendor Super Admin → Universal API Connector Builder → Production integration readiness** to assess a pinned review-only connector version. The register uses the existing connector catalog and is available in Development with the connector view grant.

## Evidence and review

An editor can record an incomplete packet and return to it later by appending a new record. A packet identifies the provider edition, market, registered official documentation link, partner or account access record, authorization record and validity date, authorized sandbox report, and capability scope. Reference IDs should identify evidence held in the authorized provider or company system. Store credentials, private keys, and customer records in their authorized systems.

The connector version and its revision identify the configuration under review. Changing the configuration makes the prior packet stale. A reviewer with the connector approval grant can request changes or confirm review for a complete and current packet recorded by another operator. A reviewer can withdraw submitted or reviewed evidence. The history records these events and retains prior versions. Authorization is valid through the recorded Manila calendar date. The API rejects a review submitted against a superseded revision or sequence.

The **Evidence reviewed** state records an operator decision. Activation still requires a separately reviewed adapter and confirmed provider authorization for the specific production environment. The register uses inert references and makes no outbound provider requests. The existing connector runtime continues to accept synthetic adapters in Development.

## Provider verification status

The repository contains planned catalog entries and public documentation references. The cloud proxy returned HTTP 403 for the checked official documentation links below. The repository contains no verified partner approval, active production authorization, or authorized sandbox result for these integrations.

| Catalog entry | Verified inputs still required |
| --- | --- |
| MYOB Business | Eligible edition and market, registered application and account consent, authorized company-file fixture and operation evidence |
| MYOB AccountRight Online | Eligible online edition and market, authorized company file and verified permitted operations |
| MYOB AccountRight Desktop | Supported local edition and version, approved bridge deployment, authorized test company and operation evidence |
| QuickBooks Desktop | Eligible edition and version, supported SDK or Web Connector transport, authorized test company and verified bridge operation evidence |
| QuickBooks Online | Eligible edition and market, approved application and company consent, authorized sandbox and operation-specific confirmation |
| Shopee Philippines | Philippine partner application approval, authorized test shop, exact order and financial scopes and field classifications |
| Lazada Philippines | Philippine partner application approval, authorized test shop, exact operation and financial scopes and field classifications |
| TikTok Shop Philippines | Philippine partner application approval, authorized test shop, exact order and financial scopes and field classifications |
| AutoCount Accounting | Official references for the supported edition, market, transport and authorized capabilities |
| AccountingSuite / 1C | Exact product and edition, official contract and supported transport, eligible market and authorized capabilities |

Official references already identified by the project include [MYOB](https://developer.myob.com/api/myob-business-api/api-overview/getting-started/), [QuickBooks Desktop SDK](https://static.developer.intuit.com/qbSDK-current/doc/pdf/QBSDK_ProGuide.pdf), [QuickBooks Online authorization](https://developer.intuit.com/app/developer/qbo/docs/develop/authentication-and-authorization/oauth-2.0), [Shopee Open Platform](https://open.shopee.com/documents/v2/OpenAPI), [Lazada Open Platform](https://open.lazada.com/apps/doc/doc?docId=108260&nodeId=10777), and [TikTok Shop Partner Center](https://partner.tiktokshop.com/docv2/page/tts-developer-types).

Production verification must confirm the actual authorized account or company file, provider destination and applicable transport, granted permission scope, idempotency and read-back behavior for writes, and approved financial mappings where applicable. Provider orders and settlement evidence retain independent financial meaning from committed eFiscal records.
