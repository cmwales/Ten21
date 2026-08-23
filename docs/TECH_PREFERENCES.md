# TECH_PREFERENCES SPECIFICATION

## 1. Core Tech Stack

- Backend Framework: C# (.NET 9 Web API)
- Database ORM: Entity Framework Core (EF Core)
- Database Engine: SQL Server / Azure SQL / PostgreSQL (Self-Hosted container ready)
- Frontend Framework: Angular (Latest version with Standalone Components & Signals)
- Styling Framework: Tailwind CSS
- Hosting & Distribution: Web API hosted on VPS / Docker containers; Angular SPA static assets served via low-cost CDN (Cloudflare Pages / Azure Static Web Apps).

## 2. Security & Infrastructure Preferences

- Identity Engine: ASP.NET Core Identity (`Microsoft.AspNetCore.Identity`)
- Key & Secrets Management: ASP.NET Core Data Protection API (Self-Hosted Key Persistence via local certificate/file store)
- Rate Limiting Engine: Native `Microsoft.AspNetCore.RateLimiting` middleware
- Payment Processor Integration: External Payment Gateway Client Tokenization SDK (Zero raw card/ACH storage)

- Unified Product & System Domain: ten21.io
- Subdomain & Network Routing Layout:
  - Public Marketing & SSR Landing: https://ten21.io
  - Web Application Portal: https://app.ten21.io
  - Backend Web API Services: https://api.ten21.io
