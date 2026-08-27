# DESIGN_SYSTEM SPECIFICATION

## 1. Master Style Architecture (`styles.scss`)

- Architecture: Deploys Tailwind CSS configured with custom SCSS design tokens. Zero individual component `.scss` files are created during Angular component generation.

## 2. Slate Navy & Emerald Color Tokens

- Primary Slate Navy (`#1E293B` / `#0F172A`): Core branding, navigation headers, primary buttons, and structural framing.
- Financial Emerald (`#047857`, revised 2026-08-27 from `#059669` — the original shade measured ~3.8:1 contrast against Neutral Surface, failing WCAG AA's 4.5:1 minimum for normal text; `#047857` measures ~5.5:1 on white and still passes against Slate Navy): Positive ledger balances, payment success confirmations, and active status indicators.
- Alert Amber & Rose (`#D97706` / `#E11D48`): Late fee warnings, delinquent account flags, and urgent maintenance notices.
- Neutral Surface (`#F8FAFC` / `#FFFFFF`): Accessible background containers engineered for clear contrast on mobile displays.
