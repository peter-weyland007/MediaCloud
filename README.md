# MediaCloud

Single-pane-of-glass media operations platform for managing and monitoring:
- Radarr, Sonarr, Lidarr
- Plex
- Prowlarr
- qBittorrent, SABnzbd
- Overseerr

## MVP Focus
- Multi-user auth (Admin/User/Viewer)
- Unified health/issues dashboard
- Issue click-through resolution pages
- Per-media detail pages
- Request submission via Overseerr
- Runtime and language mismatch validation

## Stack Decision
- Frontend: Blazor Web App (`apps/web`)
- UI components: MudBlazor
- Backend: ASP.NET Core Web API (`apps/api`)
- Persistence: SQLite via Entity Framework Core

## Repo Layout
- `apps/web` — Blazor + MudBlazor frontend UI
- `apps/api` — ASP.NET Core API + integrations + SQLite persistence
- `packages/shared-types` — shared schemas/types (placeholder)
- `packages/rbac` — role/permission helpers (placeholder)
- `infra/docker` — local/prod container assets
- `.github/workflows` — CI/CD workflows
- `docs/adr` — architecture decision records

## Current Scaffold Status
1. `.NET solution` created (`MediaCloud.slnx`)
2. Blazor app scaffolded with MudBlazor service/layout wiring
3. API scaffolded with SQLite `MediaCloudDbContext`
4. Placeholder auth endpoints (`/api/auth/login`) and health endpoint (`/api/health`) added

## Deployment (GHCR + unRAID)
- Build workflow: `.github/workflows/build-and-publish-ghcr.yml`
- Promote workflow: `.github/workflows/promote-ghcr-prod.yml` (manual action: **Promote GHCR image to prod**)
- Container docs: `infra/docker/UNRAID.md`
- Example compose: `infra/docker/unraid-compose.yml`

## Database
- MediaCloud uses **SQLite** for persistence.
- Default app DB file: `apps/api/Data/mediacloud.db` (dev)
- Container DB file: `/app/data/mediacloud.db` via mounted volume

## Next Steps
1. Implement real session auth (cookie/JWT)
2. Add RBAC policy enforcement across API routes and UI actions
3. Add service adapter interfaces for Radarr/Sonarr/Lidarr/Plex/Prowlarr/qBittorrent/SABnzbd/Overseerr
