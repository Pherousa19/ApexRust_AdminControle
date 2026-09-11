# Apex Control — Admin Rebuild V6

This release replaces the previous admin presentation layer rather than continuing to patch V3/V4/V5 CSS.

## Scope
- `src/admin-render.js`: rebuilt admin application shell and dashboard while preserving existing route/function contracts.
- `public/admin.css`: replaced with a clean, self-contained admin design system. It does not depend on storefront card/grid sizing.
- Existing store, RCON, player, Discord, Stripe, ticket, delivery and database logic remains in place.
- Existing admin routes and POST actions remain unchanged.

## Layout rules
- Every grid uses `minmax(0, 1fr)` and `min-width: 0`.
- Tables scroll inside their own containers.
- Forms cannot force parent grids wider.
- Server/player/store pages use explicit grid spans instead of selector-order hacks.
- Desktop, tablet and mobile breakpoints are isolated.

## Important
This is an admin UI rebuild, not a backend rewrite. The control-plane endpoints from the previous release are retained so plugin integration work can continue after the UI foundation is stable.
