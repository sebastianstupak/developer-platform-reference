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
- No changes to `docker-compose.yml` are required. `realm-export.json` gains `displayName: "Developer Platform"` so the login header renders the wordmark (the header text is the realm display name).
- Operational note: a running container created before the Phase-7 `keycloak/ → infra/keycloak/` move holds a **stale bind mount** to the old path, so the theme never loads. `docker compose up -d --force-recreate keycloak` fixes the mount and makes Keycloak re-scan themes and re-import the realm from `realm-export.json`. Adding new theme files afterward only needs the browser refresh (`start-dev` cache is off); adding a new theme **directory** needs another recreate (Windows bind-mount limitation, see §5).

## 4. Parent + style chain

- `theme.properties`: `parent=keycloak` (the legacy PatternFly-v3/v4 login base). Kept deliberately rather than migrating to `keycloak.v2`: it renders cleanly, is simpler to override, and our CSS fully controls the look regardless of base. (`keycloak.v2` remains a future option; not worth the selector re-targeting for zero visual gain.)
- `styles=css/login.css css/fonts.css css/zinc.css`. `css/login.css` resolves from the parent chain (inherited base); `css/fonts.css` declares the bundled `@font-face`s; `css/zinc.css` is our override and is listed last so it wins.

## 5. Typography

- Bundle Inter in the theme: `resources/css/inter-{400,500,600,700}.woff2` + an `@font-face` block in `resources/css/fonts.css` (added to `styles=`). The login then renders in Inter offline, matching the app (which loads Inter 300–700 from Google Fonts). The current name-only `Inter` fallback (which silently degrades to system-ui) is replaced, and `zinc.css` forces Inter on PatternFly titles/inputs/buttons (which set their own font-family).
- The woff2 files are co-located with the CSS (`resources/css/`) rather than a separate `resources/fonts/` dir: Docker Desktop on Windows does not propagate newly-created bind-mount subdirectories into a running container, whereas new files added to an existing mounted dir (`css/`) do propagate. Same-directory `url("inter-400.woff2")` references keep it simple.

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
