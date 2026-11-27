# Design system

Brand tokens live in `frontend/src/styles.css`: Midnight Navy #0A172B, Electric Blue #3F5EF7, Jade Teal #13A89E, Porcelain #F5F7FB, White #FFFFFF, Primary Text #14243B, Secondary Text #64748B, Border #E2E8F0, Warning #E7A443, and Error #DC525D. The solid action color is a darker electric-blue shade for white button text. Plus Jakarta Sans and Inter are self-hosted. Amounts use locale-aware PHP currency and tabular numerals.

`components/ui.tsx` provides the customized shadcn-style button, Radix dialog, card, badge, input, select, labeled field, responsive table, money display, and loading, error, and empty states. The card and control radii are 14px and 8px. Use the shared focus indicator and semantic headings and landmarks. `components/public-layout.tsx` owns public navigation, brand, and footer. `components/public-content.tsx` shares the product sections between the homepage and dedicated public routes. `pages/portals.tsx` shares the sidebar and header between portal variants. The dashboard component uses illustrative records and is shared by the homepage and client preview.

`frontend/src/public-pages.css` extends the existing tokens for the public page intro, plan cards, comparison tables, billing radio group, and registration layout. The pricing estimator shares labeled fields and live result announcements across Home and Pricing. Registration uses inline field errors, a shared password visibility control, and the existing accessible dialog for development terms. The signed-in workspace uses the existing sidebar, shared inputs and selects, status indicators, and a responsive step indicator for company onboarding.

## Document workstations and records

Use the existing workspace shell and responsive grid for company-scoped forms and registers. Keep document lines, calculated totals and the final action in separate sections. Use a confirmation dialog for Save & Send and place the official number in the permanent viewer after commitment. Allow wide tables to scroll within their labelled regions. Keep source references, the original PDF and independent accounting, delivery and regulatory statuses visible in the record. Mark locally produced documents as development fixtures.

## Connect workspace

Catalog cards use the existing workspace cards, availability badges and generic platform marks. Connector details keep provider requirements and configuration actions visible. Company and store bindings use the same scoped wizard and separate test, mapping and activation actions. Wide review tables scroll inside labelled regions. Onboarding, Builder and marketplace layouts defer to the existing brand and portal typography.


## Phase 8 mapping and synchronization

Mapping tables label the scope, type, required status and transformation for each field. Protected fiscal values have read-only rules. Wide tables scroll horizontally within a labelled region. Mapping previews and synchronization details use compact status labels, show validation before activation, and separate confirmed external identities from eFiscal document numbers.

