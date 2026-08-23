# SCHEDULE & ROADMAP SPECIFICATION

## 1. Virtual Board Meeting Phases

| Phase | Meeting Topic | Core Objectives & Deliverables | Target Document(s) | Status |
| --- | --- | --- | --- | --- |
| **Phase 0** | Security & Multi-Tenancy Baseline | Stack selection, database isolation, OWASP hardening, 9-tier RBAC, parent organization hierarchy. | `SECURITY`, `ARCHITECTURE`, `TECH_PREFERENCES`, `DATA_MODEL` | **COMPLETED** |
| **Phase 1** | Monetization & Billing Logic | SaaS pricing models, ACH convenience fee margins, double-entry ledgers, recurring auto-pay, statutory late fee application. | `BUSINESS_RULES`, `FEATURES` | **PENDING (NEXT UP)** |
| **Phase 2** | Core Operational Modules | Maintenance work orders, vendor dispatching, ARC alteration requests, amenity bookings, official board elections. | `FEATURES` | **PENDING** |
| **Phase 3** | Complete Database Schema | Complete EF Core entity maps, indexes, soft-delete filters, temporal audit logging. | `DATA_MODEL` | **PENDING** |
| **Phase 4** | UI/UX & Design System | Angular layout standards, Tailwind CSS design tokens, mobile-first resident view, accessibility (a11y). | `DESIGN_SYSTEM` | **COMPLETED** |
| **Phase 5** | Zero-Touch Self Onboarding | Self-service signup portal, instant tenant provisioning, automated seed roles, payment vault initialization. | `BUSINESS_RULES`, `ARCHITECTURE` | **COMPLETED** |
| **Sprint 3** | Property & Unit Setup | Unified property/unit form editor, searchable portfolio list, bulk CSV/XLSX importer, payment-aware property deletion. | `DATA_MODEL`, `MVP_features` | **COMPLETED** |
| **Phase 6** | Micro-Task Backlog Execution | Deconstructing features into 15-minute executable vertical slices (DTO + C# Service + Angular Component + Unit Test). | `TASKS` | **PENDING** |

## 2. Phase Execution Log

- **Phase 0 Completed:** Locked in .NET 9 + Angular stack, single shared database with EF Core filters & RLS, ASP.NET Core Identity with self-hosted Data Protection, tokenized ACH payment security, 9-tier role taxonomy, and PMC parent organization context switching.
- **Phase 4 Completed (out of roadmap order, ahead of Phase 1–3):** Built the Angular standalone SPA, Tailwind design tokens, `@ngx-translate` (en-US/es-US/fr-CA), a JWT `AuthService` + refresh-and-retry interceptor, and functional route guards enforcing the Tenant financial/ledger hard-block at the UI layer — pulled forward because Phase 1–3's backend work has no way to be manually exercised or demoed without a working login and UI shell first. See `User_Stories_Phase_0.md`'s sibling frontend stories (US-00, US-10–US-13) for the detailed record.
- **Phase 5 Completed:** Sprint covering US-14 (Workspace Registration & Onboarding), US-15 (Google OAuth & Post-Login Profile Completion), US-16 (Email Activation & Self-Service Password Recovery), US-17 (Email 6-Digit OTP Dual Authentication), and US-18 (Bot Defense & Registration Rate Limiting). Full acceptance criteria and design decisions in `User_Stories_Phase_5.md`. Each story was built on its own branch and merged into `main` once its own build/tests were green, before the next branch was cut. A follow-up fix later removed TOTP/authenticator-app support in favor of email-only 2FA — see that doc's US-17 section.
- **Sprint 3 Completed:** Sprint covering US-19 (Unified Single Property & Unit Form Editor), US-20 (Property & Unit List View with Real-Time Search & Pagination), US-21 (In-Memory Bulk CSV/XLSX Property & Unit Importer), and US-22 (Conditional Payment-Based Property Deletion). Full acceptance criteria and design decisions in `User_Stories_Sprint_3.md`. Each story was built on its own branch and merged into `main` once its own build/tests were green, same workflow as Phase 5. `Property`/`Unit` are now real entities (`DATA_MODEL.md` still needs a follow-up pass to document their schema formally). US-22's payment check is a deliberate placeholder (always `false`) pending Phase 1's payment ledger — see that story's section in `User_Stories_Sprint_3.md`.
