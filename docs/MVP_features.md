# V1 MINIMUM VIABLE PRODUCT (MVP) FEATURE SPECIFICATION

## 1. Public Acquisition, Sales & SEO Engine

- Public Landing Page: High-converting marketing view showcasing core platform value propositions and mobile product previews.
- Interactive Unit Pricing Calculator: Real-time cost estimator calculating monthly SaaS subscription rates based on total managed units.
- Angular SSR & Technical SEO: Server-side prerendering for public routes (`/`, `/pricing`, `/features`), dynamic Open Graph metadata, and automated `sitemap.xml`/`robots.txt` generation.
- Self-Service SaaS Provisioning: Automated signup workflow allowing new managers to register, configure their organization, and launch a portal in under two minutes.
- Public Legal & Compliance Center: Dedicated routes hosting Terms of Service, Privacy Policy (PII disclosure), EULA, and Subscription Agreements.

## 2. Identity, Security & Access Control

- ASP.NET Core Identity & JWT Pipeline: Stateless authentication issuing access tokens and HTTP-Only refresh tokens carrying tenant and role claims.
- Role-Based Multi-Factor Authentication (MFA): Mandatory email one-time-code verification for administrative roles and adaptive MFA prompts for sensitive account changes.
- Brute-Force Lockout & Rate Throttling: 5-strike failed login lockout (15-minute freeze) paired with sliding-window IP rate limiting on authentication endpoints.
- 9-Tier Additive Claims RBAC: Fine-grained permission matrix mapping 9 system roles (SuperAdmin, Property Manager, Board Member, Property Owner, Tenant, Vendor, Committee Member, On-Site Staff, Accountant) to additive claim bundles.
- Hardened Owner vs. Tenant Isolation: Policy-layer authorization handlers strictly blocking non-owner renters (`Tenant`) from accessing financial ledgers, legal notices, or owner voting workflows.
- OWASP Security & Application Encryption: ASP.NET Core Data Protection API for application-layer PII encryption, parameterized EF Core queries, Angular DOM sanitization, and hardened HTTP headers (CSP, HSTS, X-Frame-Options).

## 3. Portfolio, Property & Customization Setup

- PMC Portfolio Switcher: Parent-child context switching (`OrganizationId` → `TenantId`) enabling property managers to toggle between distinct property contexts under one authenticated login.
- Property & Unit Management: Configuration workflows for defining residential, commercial, or multi-family property structures, target rent rates, and unit occupancy status tracking.
- Portal Branding Customization: Ability to upload PMC company logos, select brand accent colors, and configure customized tenant portal welcome notes and property rules.
- Property & Unit Photo Vault: Hero banner and unit condition photo galleries stored using direct presigned S3/Cloudflare R2 URLs.

## 4. Lease, Document Vault & Financial Ledgers

- Lease Schedule & Expiration Tracker: Define lease start/end dates, monthly rent dues, security deposit tracking, and automated 30/60/90-day expiration alerts with 1-click renewal triggers.
- Role-Gated Lease Document Vault: Central repository for storing uploaded executed lease PDFs, addendums, move-in inspection forms, and unit disclosures.
- Multi-Tender Manual Rent Ledger: Multi-tender payment logging across Cash, Check, Zelle, Venmo, or Direct Deposit with real-time balance calculations.
- Fee & Credit Adjustment Engine: Support for tacking on or removing late/utility fees, logging manual credit adjustments, and reallocating funds from one charge to another.
- Security Deposit Escrow Ledger: Dedicated liability ledger tracking held security deposits separately from operating rental income.
- Property Expense Logging: Operational cost logging (plumbing, repairs, property taxes, insurance) to calculate Net Operating Income (NOI).
- Dynamic Tenant Statements & Exports: Real-time itemized statement calculations displaying charges versus payments logged alongside downloadable financial rent roll summaries.

## 5. Maintenance Dispatch & Communication Workflows

- Tenant Maintenance Submission: Ticket creation interface with direct AWS S3/Cloudflare R2 presigned photo uploads featuring client-side WebP compression and a hard 10MB file limit.
- Landlord Dispatch Dashboard: Work order prioritization, vendor assignments, repair scheduling, and resolution status tracking.
- Vendor Execution Portal: Mobile-responsive contractor view for assigned tasks, work status updates, and completion proof photo uploads.
- Occupant Announcement Broadcast: Community notice engine broadcasting property-wide announcements directly to occupant dashboards.

## 6. Platform Core Capabilities & UX Standards

- Portfolio Metrics Dashboard: At-a-glance landlord KPI view displaying occupancy rates, total rent collected vs. outstanding, and open maintenance tickets.
- Platform SuperAdmin Portal: Internal administrative view for provisioning tenant accounts, inspecting system diagnostics, and managing global SaaS subscriptions.
- Master Stylesheet & Design Tokens: Centralized `styles.scss` Tailwind architecture utilizing custom SCSS tokens, Slate Navy framing, and Financial Emerald status accents.
- Accessibility & Localization: WCAG 2.1 AA compliant UI featuring responsive card-stack layouts, visible focus rings, minimum 48x48px tap targets, and tri-lingual localization (`en-US`, `es-US`, `fr-CA`) via `@ngx-translate`.
