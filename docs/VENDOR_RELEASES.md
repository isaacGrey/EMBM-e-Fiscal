# Phase 12 vendor administration

The Development workspace uses synthetic accounting and commerce adapters. Production connector publication and vendor sign-in require approved provider evidence and production authentication.

## Local operator access

Set `Identity__EnableLocalVendorLogin=true` for the Development API. The existing `--bootstrap-local-vendor` command accepts `Identity__LocalVendorProfile` when creating a fixture identity. Use a separate synthetic identity for each operator in a release exercise.

| Profile | Access |
| --- | --- |
| `ConnectorEditor` | View, edit, copy, import and validate definitions |
| `ConnectorReviewer` | View and approve validated synthetic versions |
| `ConnectorPublisher` | View, publish, run approved sandbox tests, manage lifecycle and revoke versions |
| `CommercialObserver` | View the billing catalog and monitor subscriptions, payments and wallet activity |
| `Administrator` | Existing local administrator fixture access, including all listed operations |

Commercial reads, catalog publication and wallet adjustments have separate server-side grants. The commercial activity page loads wallets for its visible subscriber page. Wallet adjustments continue through the existing credit ledger with an approval reference.

## Release and incident workflow

1. Create or copy a version in **Universal API Connector Builder**. Review the version configuration and validate it.
2. Use an authorized reviewer to approve the validated version. Use an authorized publisher to publish it. Marketplace discovery selects the newest published version. Existing connections use their original version pins.
3. Open **Connector monitoring** to see availability and connection health by release. Select a release to inspect its version audit, connection counts and accounting operation counts. Counts contain no customer or account identifiers.
4. For a connector-wide interruption, select **Disable connector**. Publishing during the interruption records a release and preserves the disabled state. Use **Resume connector** to resume operations explicitly. Deprecation continues to block new bindings. Retirement is terminal.
5. For a release-specific incident, select its version, record a revocation reason and confirm. The audit record is append-only. Connections pinned to that release are shown as suspended, and subsequent runtime and sandbox calls are denied. If the current release is revoked, publish a higher reviewed version to reopen discovery. Older usable pins continue to operate.
6. Review unsubmitted work and operations awaiting accounting confirmation before planning a cutover. Moving an existing binding to another version requires a separate review of mappings, destination coverage and existing provider identities. Publishing a release never rewrites those pins.

Historic configurations and revoked releases remain available to authorized operators. In-flight work that already passed authorization can finish under its original pin. The controls apply to subsequent runtime work.
