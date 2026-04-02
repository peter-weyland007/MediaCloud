# MediaCloud on unRAID (GHCR)

Use this if you want the same pattern as WeismanTracker:
- Build image to GHCR from `main`
- Promote a tested tag to `prod`
- Run unRAID container from `ghcr.io/peter-weyland007/mediacloud:prod`

## Required container settings

- **Repository**: `ghcr.io/peter-weyland007/mediacloud:prod`
- **Host Port**: `5288`
- **Container Port**: `8080`
- **Volume**: `/mnt/user/appdata/mediacloud` -> `/app/data`

## Environment variables

- `API_PORT=5299`
- `WEB_PORT=8080`
- `ApiBaseUrl=http://127.0.0.1:5299`
- `ConnectionStrings__DefaultConnection=Data Source=/app/data/mediacloud.db`
- `Jwt__SigningKey=<set a long random value>`

> The app is SQLite-based and persists to `/app/data/mediacloud.db`.

## Deploy/update flow

1. Push to `main` -> workflow **Build and Publish GHCR** publishes tags (`main`, `sha-*`).
2. In GitHub Actions, run **Promote GHCR image to prod** with chosen source tag.
3. In unRAID, update/recreate container (or pull latest `:prod`).

## Notes

- If you keep multiple app versions, use different host ports.
- Keep JWT signing key stable between restarts if you want existing sessions to remain valid.
