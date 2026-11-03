# 0005. Ambit's look on Tailwind CSS

Status: accepted (2026-10-29)

## Decision

The shell and colours of Ambit's web app (ambit.project, `apps/web/src/styles/tokens.css` and `layout.css`): a dark
navy sidebar in both themes with grouped entries, a 56 px top bar with search and the user, a light page by default
and a dark theme, the brand blue `#1e4f8f` as the accent, Inter. Ambit uses HeroUI on React; here the same tokens feed
Tailwind CSS v4 (CLI) with shadcn-style names (`apps/Shipment.Api/Styles/app.css` → `wwwroot/css/app.css`, committed,
so the image needs no Node). Icons: Lucide (`tools/generate-icons.py`). UI texts in English (ADR 0006, PRODUCT.md).
