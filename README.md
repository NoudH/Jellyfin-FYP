# Jellyfin Watch Recommendations

A Jellyfin plugin that creates personalized movie and series recommendations from each user's viewing history. Anime represented in Jellyfin as series is included.

## Features

- Calculates recommendations separately for each Jellyfin user using watched or started movies and series as viewing history.
- Recommends unwatched titles using viewing history and ratings when available.
- Refreshes each user's recommendations on a schedule and keeps the previous list if a refresh fails.
- Optionally adds off-library movie and series candidates from Seerr when Jellyfin Enhanced (JE) is installed, Seerr is enabled and configured in JE, and the Jellyfin user is linked and eligible.
- Uses JE in the web client for off-library posters and request actions. If JE or its Seerr integration is unavailable, recommendations continue with titles from the Jellyfin library.
- The recommendations API only returns results for the signed-in user.

## Requirements

- Jellyfin Server 12.0 or later.
- Jellyfin Enhanced is optional; it is needed only for Seerr candidates and JE-powered posters/request actions for off-library titles.
- The .NET 10 SDK is required to build and test from source.

## Install from a plugin repository

After the corresponding workflow has published a build, add one of these manifest URLs in Jellyfin under **Dashboard → Plugins → Repositories**, then install **Watch Recommendations** and restart Jellyfin if requested:

- Stable releases: `https://raw.githubusercontent.com/NoudH/Jellyfin-FYP/master/manifest.json`
- Development releases (the `Dev` branch): `https://raw.githubusercontent.com/NoudH/Jellyfin-FYP/Dev/manifest-dev.json`

## Use

The **Rebuild Watch Recommendations** task runs every 24 hours by default. In Jellyfin's **Dashboard → Scheduled Tasks**, change how often it runs or start it manually. A failed refresh keeps the previous list.

The **Recommended** view shows the latest list. Use **Reload** to refresh it; this does not start the scheduled task. After installing, wait for the first scheduled run.

The signed-in user's recommendations are available at `GET /WatchRecommendations/recommendations`.

### KefinTweaks home-screen row

To show only titles already in your library, add a KefinTweaks query with type **Custom Endpoint** and path `/WatchRecommendations/recommendations/items`. Set a limit in the query options.

#### Include external Seerr titles

To show library and off-library titles together in a **Recommended** row, install KefinTweaks and Jellyfin's JavaScript Injector, then add this script in the Injector:

```javascript
const script = document.createElement('script');
script.src = 'https://cdn.jsdelivr.net/gh/NoudH/Jellyfin-FYP@master/Jellyfin.Plugin.WatchRecommendations/Integrations/KefinTweaksRecommendations.js';
script.async = true;
document.head.appendChild(script);
```

For builds from `Dev`, replace `@master` with `@Dev`. The row uses normal Jellyfin cards for library titles and JE's Seerr cards for off-library titles, including JE's poster and request controls. If JE is unavailable, only library titles appear. Disable the custom endpoint row above to avoid duplicates.

## Build and test

From the repository root, with the .NET 10 SDK installed:

```powershell
dotnet test Jellyfin-FYP.sln
dotnet build Jellyfin.Plugin.WatchRecommendations\Jellyfin.Plugin.WatchRecommendations.csproj --configuration Release
```

Run the frontend integration tests with Node.js:

```powershell
node --test Jellyfin.Plugin.WatchRecommendations.Tests\KefinTweaksRecommendations.test.cjs
```

The plugin assembly is produced under `Jellyfin.Plugin.WatchRecommendations\bin\Release\net10.0\`.

## Releases

Pushing a tag in the format `vMAJOR.MINOR.PATCH.REVISION` runs the stable build and test workflow, creates a GitHub Release, and updates `manifest.json`. To get the latest development build, use the `manifest-dev.json` from the Dev branch instead.