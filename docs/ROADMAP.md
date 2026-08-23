# SCHEDULE & ROADMAP SPECIFICATION

## 1. Virtual Board Meeting Phases

| Phase | Meeting Topic | Core Objectives & Deliverables | Target Document(s) | Status |
| --- | --- | --- | --- | --- |
| **Phase 0** | Security & Multi-Tenancy Baseline | Stack selection, database isolation, OWASP hardening, 9-tier RBAC, parent organization hierarchy. | `SECURITY`, `ARCHITECTURE`, `TECH_PREFERENCES`, `DATA_MODEL` | **COMPLETED** |
| **Phase 1** | Monetization & Billing Logic | SaaS pricing models, ACH convenience fee margins, double-entry ledgers, recurring auto-pay, statutory late fee application. | `BUSINESS_RULES`, `FEATURES` | **PENDING (NEXT UP)** |
| **Phase 2** | Core Operational Modules | Maintenance work orders, vendor dispatching, ARC alteration requests, amenity bookings, official board elections. | `FEATURES` | **PENDING** |
| **Phase 3** | Complete Database Schema | Complete EF Core entity maps, indexes, soft-delete filters, temporal audit logging. | `DATA_MODEL` | **PENDING** |
| **Phase 4** | UI/UX & Design System | Angular layout standards, Tailwind CSS design tokens, mobile-first resident view, accessibility (a11y). | `DESIGN_SYSTEM` | **PENDING** |
| **Phase 5** | Zero-Touch Self Onboarding | Self-service signup portal, instant tenant provisioning, automated seed roles, payment vault initialization. | `BUSINESS_RULES`, `ARCHITECTURE` | **PENDING** |
| **Phase 6** | Micro-Task Backlog Execution | Deconstructing features into 15-minute executable vertical slices (DTO + C# Service + Angular Component + Unit Test). | `TASKS` | **PENDING** |

## 2. Phase Execution Log

- **Phase 0 Completed:** Locked in .NET 9 + Angular stack, single shared database with EF Core filters & RLS, ASP.NET Core Identity with self-hosted Data Protection, tokenized ACH payment security, 9-tier role taxonomy, and PMC parent organization context switching.
