# apps/api

ASP.NET Core Web API for MediaCloud.

## Current scaffold
- EF Core + SQLite wired (`ConnectionStrings:MediaCloud`)
- `MediaCloudDbContext` + `AppUser` model + `UserRole` enum
- Automatic DB creation at startup (`data/mediacloud.db`)
- Seed admin user: `admin / changeme` (scaffold-only)
- Endpoints:
  - `GET /api/health`
  - `POST /api/auth/login`
  - `GET /api/users`

## Responsibilities
- Auth/session endpoints
- RBAC enforcement middleware
- Service adapter layer (Radarr/Sonarr/Lidarr/Plex/Prowlarr/qBittorrent/SABnzbd/Overseerr)
- Issue normalization
- Runtime/language validation jobs
- Remediation action handlers
