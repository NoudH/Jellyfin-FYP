# Jellyfin Watch Recommendations

A Jellyfin plugin that creates personalized movie and series recommendations from each user's viewing history. Anime represented in Jellyfin as series is included.

## Features

- Calculates recommendations separately for each Jellyfin user using watched or started movies and series as viewing history.
- Ranks unwatched library titles using genre and tag similarity to watched titles; watched-title completion and recency weight that similarity, while candidate ratings and release/addition recency contribute when available.
- Stores the last successful results in a server-side cache. A refresh failure for one user keeps that user's previous cache.
- Optionally adds off-library movie and series candidates from Seerr when Jellyfin Enhanced (JE) is installed, Seerr is enabled and configured in JE, and the Jellyfin user is linked and eligible.
- Uses JE in the web client for off-library posters and request actions. If JE or its Seerr integration is unavailable, recommendations continue with titles from the Jellyfin library.
- Exposes results through an authenticated API for the signed-in user; clients cannot choose another user's cache by supplying a user ID.

JE recommendations are fetched for a bounded set of watched titles that have TMDb IDs, rather than from a single Seerr personal feed. The integration reuses JE's configuration and internal implementation details, so compatibility may depend on the JE version.

## Requirements

- Jellyfin Server 12.0 or later.
- Jellyfin Enhanced is optional; it is needed only for Seerr candidates and JE-powered posters/request actions for off-library titles.
- The .NET 10 SDK is required to build and test from source.

## Install from a plugin repository

After the corresponding workflow has published a build, add one of these manifest URLs in Jellyfin under **Dashboard → Plugins → Repositories**, then install **Watch Recommendations** and restart Jellyfin if requested:

- Stable releases: `https://raw.githubusercontent.com/NoudH/Jellyfin-FYP/master/manifest.json`
- Development releases (the `Dev` branch): `https://raw.githubusercontent.com/NoudH/Jellyfin-FYP/Dev/manifest-dev.json`

## Use

The **Rebuild Watch Recommendations** task runs every 24 hours by default. In Jellyfin's **Dashboard → Scheduled Tasks**, you can change its trigger cadence or run it manually. Each run processes users independently and replaces each user's cache only after a successful refresh.

The embedded **Watch Recommendations** view reads the latest cached results. Its **Reload results** button reloads the cache from the API; it does not start the scheduled task. A first-time install may show no recommendations until the task has run successfully.

The authenticated endpoint is `GET /WatchRecommendations/recommendations`. It derives the user from the Jellyfin authentication identity. Cached data is persisted under Jellyfin's plugin path at `configurations/Jellyfin.Plugin.WatchRecommendations/recommendations-cache.json`.

## Build and test

From the repository root, with the .NET 10 SDK installed:

```powershell
dotnet test Jellyfin-FYP.sln
dotnet build Jellyfin.Plugin.WatchRecommendations\Jellyfin.Plugin.WatchRecommendations.csproj --configuration Release
```

The plugin assembly is produced under `Jellyfin.Plugin.WatchRecommendations\bin\Release\net10.0\`.

## Releases

Pushing a tag in the format `vMAJOR.MINOR.PATCH.REVISION` runs the stable build and test workflow, creates a GitHub Release, and updates `manifest.json`. Commits pushed to `Dev` run the development workflow and update `manifest-dev.json`; manifest-only commits do not trigger another development build.