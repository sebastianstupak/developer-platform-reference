# Keycloak Login Theme — Design

**Date:** 2026-08-28
**Status:** Approved (design)
**Feature:** A custom Keycloak login theme (`devplatform`) that matches the Developer Platform's zinc / shadcn-style visual identity across all login-flow pages.

## 1. Goal

The Keycloak sign-in experience (realm `developer-platform`, :8090) should look like it belongs to the platform — same zinc palette, Inter typography, card styling, and "Developer Platform" wordmark — instead of stock Keycloak. A scaffold already exists (`infra/keycloak/themes/devplatform`, mounted into the container, realm `loginTheme=devplatform`) with a faithful token match; this closes the remaining fidelity gaps and verifies it against the rendered pages.

## 2. Approach

**Complete the existing CSS-override theme (FTL-free).** Keep Keycloak's inherited markup and override with CSS only. Rejected alternatives: forking FTL templates (pixel-perfect but couples us to Keycloak's internal template contract and risks breaking auth features on upgrade) and a full PatternFly-replacement stylesheet (heavier, more brittle, no gain).

## 3. Packaging (no plumbing changes)

- Theme lives at `infra/keycloak/themes/devplatform`, mounted read-only into Keycloak 26.2 at `/opt/keycloak/themes/devplatform` (existing `docker-compose.yml`).
- Realm `loginTheme` is already `devplatform` (`infra/keycloak/realm-export.json`).
- Keycloak runs `start-dev`, which disables theme/template caching — CSS edits reload on browser refresh, no container restart.
- No changes to `docker-compose.yml` or the realm are required.

## 4. Parent + style chain

- `theme.properties`: `parent=keycloak.v2` (Keycloak 26.2's PatternFly-v5 login base).
- Fix `styles=` so the parent's base stylesheet loads **before** our override, then `css/zinc.css` last so it wins. A child `styles=` replaces the parent's list, so the base sheet must be re-included explicitly; the current `css/login.css` entry is a phantom (no such file) and is corrected. The exact base filename is confirmed by inspecting the parent theme inside the container (`/opt/keycloak/lib/.../theme/keycloak.v2/login/theme.properties`) or the rendered page's `<link>`s.

## 5. Typography

- Bundle Inter in the theme: `resources/fonts/inter-{400,500,600,700}.woff2` + an `@font-face` block (in `resources/css/fonts.css`, added to `styles=`). The login then renders in Inter offline, matching the app (which loads Inter 300–700 from Google Fonts). The current name-only `Inter` fallback (which silently degrades to system-ui) is replaced.

## 6. Design tokens (locked to the app)

From `DevPlatformTheme` / `app.css`, light palette:

| Token | Value |
| --- | --- |
| Page background | `#fafafa` |
| Card / surface | `#ffffff`, border `#e4e4e7`, radius 12px, shadow `0 1px 2px rgba(9,9,11,.05)` |
| Primary (button) | `#18181b`, hover `#27272a`, text `#fafafa` |
| Text | primary `#18181b`, secondary `#71717a` |
| Input border | `#d4d4d8`; focus ring `0 0 0 1px #18181b`, border `#18181b` |
| Radius (controls) | 8px |
| Error | `#dc2626` |
| Font | Inter (300–700), `letter-spacing -0.02em` on the wordmark |

Wordmark is the styled **text** header "Developer Platform" (weight 600) — the app uses a text wordmark, not a logo image.

## 7. Page coverage (all login-flow pages)

Login, OTP (`login-otp`), password update (`login-update-password`), reset (`login-reset-password`), verify-email, `error`, `info`, terms. These reuse the same PatternFly form/card/button/alert components, so the shared selectors in `zinc.css` cascade to them. Targeted rules are added only for the distinct elements: the OTP code input, the alert/error banner (zinc error tint), and link / "back to login" treatment. All CSS — no FTL.

## 8. Verification (visual)

A theme's correctness is visual, so implementation iterates: render each page against the running Keycloak, screenshot, compare to the app, adjust CSS, repeat. Rendering needs **only** Keycloak (not the Web app), so the pre-existing local `redirect_uri` issue does not block it:

- Login form: reach it via the built-in account console (`/realms/developer-platform/account/`), whose preconfigured redirect URI is valid and lands on the themed login form.
- OTP / reset / error / info variants: trigger via the relevant flow or the forms directly.
- Confirm Inter actually loads (computed font / network) and the style-chain fix did not break the base layout.

Screenshots are captured with the repo's Playwright (headless), saved under a scratch dir (not committed).

## 9. Out of scope (YAGNI)

Dark-mode login, the Account Console (separate app), email templates, a logo image, i18n message-bundle overrides.
