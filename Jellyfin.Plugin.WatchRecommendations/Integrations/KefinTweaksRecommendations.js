(function (root) {
    'use strict';

    const MAX_RESULTS = 50;
    const REFRESH_INTERVAL_MS = 5 * 60 * 1000;
    const SECTION_ID = 'watch-recommendations-kefintweaks';

    function read(item, camelName, pascalName) {
        return item?.[camelName] ?? item?.[pascalName];
    }

    function normalizeMediaType(item) {
        const type = String(read(item, 'mediaType', 'MediaType') ?? read(item, 'type', 'Type') ?? '').toLowerCase();
        if (type === 'movie' || type === 'movies') return 'movie';
        if (type === 'series' || type === 'tv' || type === 'show' || type === 'shows') return 'tv';
        return null;
    }

    function getProviderIds(item) {
        return read(item, 'providerIds', 'ProviderIds') || {};
    }

    function getProviderIdentity(item, mediaType) {
        const providerIds = getProviderIds(item);
        for (const provider of ['Tmdb', 'Imdb']) {
            const value = Object.entries(providerIds)
                .find(([key, id]) => key.toLowerCase() === provider.toLowerCase() && id != null && String(id).trim())?.[1];
            if (value != null) return `${mediaType}:${provider.toLowerCase()}:${String(value).trim().toLowerCase()}`;
        }
        const id = read(item, 'id', 'Id');
        return id ? `${mediaType}:jellyfin:${String(id).toLowerCase()}` : null;
    }

    function getTmdbId(item) {
        const providerIds = getProviderIds(item);
        const value = Object.entries(providerIds)
            .find(([key, id]) => key.toLowerCase() === 'tmdb' && id != null)?.[1];
        const tmdbId = Number(value);
        return Number.isSafeInteger(tmdbId) && tmdbId > 0 ? tmdbId : null;
    }

    function buildRecommendationEntries(recommendations, libraryItems, includeExternal) {
        const byId = new Map();
        const byProviderId = new Map();

        for (const libraryItem of libraryItems || []) {
            const mediaType = normalizeMediaType(libraryItem);
            if (!mediaType) continue;

            const id = read(libraryItem, 'id', 'Id');
            if (id) byId.set(String(id).toLowerCase(), libraryItem);
            const identity = getProviderIdentity(libraryItem, mediaType);
            if (identity && !identity.includes(':jellyfin:')) byProviderId.set(identity, libraryItem);
        }

        const entries = [];
        const seen = new Set();
        for (const recommendation of recommendations || []) {
            const item = read(recommendation, 'item', 'Item');
            if (!item || read(item, 'isStarted', 'IsStarted') || read(item, 'isWatched', 'IsWatched')) continue;

            const mediaType = normalizeMediaType(item);
            if (!mediaType) continue;

            const id = read(item, 'id', 'Id');
            const providerIdentity = getProviderIdentity(item, mediaType);
            const localItem = (id ? byId.get(String(id).toLowerCase()) : null)
                || (providerIdentity ? byProviderId.get(providerIdentity) : null);
            const isInLibrary = Boolean(localItem);
            const libraryFlag = read(item, 'isInLibrary', 'IsInLibrary');
            if (!isInLibrary && libraryFlag !== false) continue;
            if (!isInLibrary && (!includeExternal || !getTmdbId(item))) continue;

            const identity = providerIdentity || (localItem ? getProviderIdentity(localItem, mediaType) : null);
            if (identity && seen.has(identity)) continue;
            if (identity) seen.add(identity);

            const rawScore = read(recommendation, 'score', 'Score');
            const score = rawScore == null || rawScore === '' ? Number.NaN : Number(rawScore);
            entries.push({
                item,
                libraryItem: localItem,
                mediaType,
                isInLibrary,
                tmdbId: getTmdbId(item),
                score: Number.isFinite(score) ? score : null
            });
            if (entries.length >= MAX_RESULTS) break;
        }

        return entries;
    }

    const helpers = { buildRecommendationEntries, getTmdbId, normalizeMediaType };
    if (typeof module === 'object' && module.exports) module.exports = helpers;
    if (!root.document || !root.document.body) return;
    if (root.__watchRecommendationsKefinTweaksLoaded) return;
    root.__watchRecommendationsKefinTweaksLoaded = true;

    let observer;
    let syncTimer;
    let requestVersion = 0;
    let activeContainer = null;
    let activeUserId = null;
    let lastLoadedAt = 0;

    function currentUserId() {
        try {
            return root.ApiClient?.getCurrentUserId?.() || null;
        } catch (_) {
            return null;
        }
    }

    function currentJeApi() {
        return root.JellyfinEnhanced?.jellyseerrAPI || null;
    }

    function isCurrent(version, userId, container) {
        return version === requestVersion
            && userId === currentUserId()
            && !!container
            && container.isConnected
            && container.closest('.libraryPage:not(.hide) .homeSectionsContainer') !== null;
    }

    function addStyles() {
        if (root.document.getElementById(`${SECTION_ID}-styles`)) return;
        const style = root.document.createElement('style');
        style.id = `${SECTION_ID}-styles`;
        style.textContent = `
            .wr-ktw-section { order: 1000; margin: 0 0 1.5rem; }
            .wr-ktw-heading { display: flex; align-items: baseline; flex-wrap: wrap; gap: .75rem; padding: 0 1.5%; }
            .wr-ktw-heading h2 { margin: 0 0 .65rem; font-size: 1.4rem; }
            .wr-ktw-status { margin: 0 0 .65rem; opacity: .75; font-size: .9rem; }
            .wr-ktw-reload { margin: 0 0 .65rem auto; }
            .wr-ktw-track { display: flex; gap: .85rem; overflow-x: auto; overscroll-behavior-x: contain; padding: 0 1.5% .75rem; scroll-snap-type: x mandatory; }
            .wr-ktw-card { flex: 0 0 clamp(145px, 16vw, 220px); min-width: 0; overflow: hidden; border-radius: .35rem; background: rgba(0, 0, 0, .28); scroll-snap-align: start; }
            .wr-ktw-poster-link { display: block; color: inherit; }
            .wr-ktw-poster { display: block; width: 100%; aspect-ratio: 2 / 3; object-fit: cover; background: rgba(0, 0, 0, .35); }
            .wr-ktw-body { padding: .65rem .75rem; }
            .wr-ktw-title { margin: 0 0 .35rem; font-size: 1rem; line-height: 1.25; }
            .wr-ktw-meta { margin: 0; opacity: .78; font-size: .85rem; }
            .wr-ktw-overview { display: -webkit-box; overflow: hidden; margin: .4rem 0 0; opacity: .72; font-size: .82rem; -webkit-box-orient: vertical; -webkit-line-clamp: 3; }
            .wr-ktw-request { margin: 0 .75rem .75rem; width: calc(100% - 1.5rem); }
            @media (max-width: 700px) { .wr-ktw-card { flex-basis: 42vw; } }
        `;
        root.document.head.appendChild(style);
    }

    function createSection(container) {
        let section = container.querySelector(`[data-wr-ktw-id="${SECTION_ID}"]`);
        if (section) return section;

        section = root.document.createElement('section');
        section.className = 'verticalSection emby-scroller-container custom-scroller-container wr-ktw-section';
        section.dataset.wrKtwId = SECTION_ID;

        const heading = root.document.createElement('div');
        heading.className = 'wr-ktw-heading';
        const title = root.document.createElement('h2');
        title.textContent = 'Recommended for you';
        const status = root.document.createElement('p');
        status.className = 'wr-ktw-status';
        const reload = root.document.createElement('button');
        reload.className = 'raised button-submit wr-ktw-reload';
        reload.type = 'button';
        reload.textContent = 'Reload';
        heading.append(title, status, reload);

        const track = root.document.createElement('div');
        track.className = 'wr-ktw-track';
        section.append(heading, track);
        container.appendChild(section);

        reload.addEventListener('click', () => {
            if (activeContainer === container) void loadRecommendations(container, currentUserId(), true);
        });
        return section;
    }

    function setStatus(section, message) {
        const status = section.querySelector('.wr-ktw-status');
        status.textContent = message || '';
        status.hidden = !message;
    }

    function addText(parent, tagName, className, value) {
        const element = root.document.createElement(tagName);
        if (className) element.className = className;
        element.textContent = value || '';
        parent.appendChild(element);
        return element;
    }

    function getItems(result) {
        const items = read(result, 'items', 'Items');
        return Array.isArray(items) ? items : [];
    }

    function getRecommendations(result) {
        const recommendations = read(result, 'recommendations', 'Recommendations');
        return Array.isArray(recommendations) ? recommendations : [];
    }

    function getImageUrl(entry) {
        const apiClient = root.ApiClient;
        const libraryItem = entry.libraryItem;
        if (entry.isInLibrary && libraryItem && typeof apiClient?.getImageUrl === 'function') {
            const id = read(libraryItem, 'id', 'Id');
            if (id) return apiClient.getImageUrl(id, 'Primary', { maxWidth: 420 });
        }
        return null;
    }

    function addEntryCard(track, entry, version, userId) {
        const item = entry.item;
        const card = root.document.createElement('article');
        card.className = 'wr-ktw-card';

        const posterLink = root.document.createElement('a');
        posterLink.className = 'wr-ktw-poster-link';
        const image = root.document.createElement('img');
        image.className = 'wr-ktw-poster';
        image.loading = 'lazy';
        image.alt = '';
        image.addEventListener('error', () => image.remove());
        const localImageUrl = getImageUrl(entry);
        if (localImageUrl) image.src = localImageUrl;
        posterLink.appendChild(image);

        if (entry.isInLibrary) {
            const id = read(entry.libraryItem, 'id', 'Id');
            const serverId = root.ApiClient?.serverId?.();
            if (id && serverId) {
                posterLink.href = `#/details?id=${encodeURIComponent(id)}&serverId=${encodeURIComponent(serverId)}`;
            } else {
                posterLink.removeAttribute('href');
            }
        } else {
            posterLink.removeAttribute('href');
        }
        card.appendChild(posterLink);

        const body = root.document.createElement('div');
        body.className = 'wr-ktw-body';
        const name = read(entry.libraryItem, 'name', 'Name') || read(item, 'name', 'Name') || 'Untitled';
        addText(body, 'h3', 'wr-ktw-title', name);
        const scoreText = entry.score == null ? 'Recommended' : `${Math.round(entry.score * 100)}% match`;
        const label = entry.isInLibrary ? 'In your library' : 'Available to request';
        addText(body, 'p', 'wr-ktw-meta', `${label} · ${scoreText}`);
        const overview = read(item, 'overview', 'Overview');
        if (overview) addText(body, 'p', 'wr-ktw-overview', overview);
        card.appendChild(body);

        if (!entry.isInLibrary) {
            const button = root.document.createElement('button');
            button.className = 'raised button-submit wr-ktw-request';
            button.type = 'button';
            button.textContent = 'Request via Jellyfin Enhanced';
            button.addEventListener('click', async event => {
                event.preventDefault();
                event.stopPropagation();
                const jeApi = currentJeApi();
                if (!jeApi?.requestMedia || !entry.tmdbId || !isCurrent(version, userId, activeContainer)) return;
                button.disabled = true;
                button.textContent = 'Requesting…';
                try {
                    const result = await jeApi.requestMedia(entry.tmdbId, entry.mediaType);
                    if (!result) throw new Error('The request was not accepted.');
                    if (isCurrent(version, userId, activeContainer)) button.textContent = 'Request submitted';
                } catch (error) {
                    console.warn('Watch Recommendations: Seerr request failed', error);
                    if (isCurrent(version, userId, activeContainer)) {
                        button.textContent = 'Request failed';
                        button.disabled = false;
                    }
                }
            });
            card.appendChild(button);
        }

        track.appendChild(card);
        if (!entry.isInLibrary) return () => loadExternalPoster(image, entry, version, userId, activeContainer);
        return null;
    }

    async function loadExternalPoster(image, entry, version, userId, container) {
        const jeApi = currentJeApi();
        const getDetails = entry.mediaType === 'movie' ? jeApi?.fetchMovieDetails : jeApi?.fetchTvShowDetails;
        if (typeof getDetails !== 'function') return;
        try {
            const details = await getDetails.call(jeApi, entry.tmdbId);
            if (!isCurrent(version, userId, container)) return;
            const posterPath = details?.posterPath || details?.poster_path;
            if (typeof posterPath !== 'string' || !/^\/?[A-Za-z0-9._/-]+$/.test(posterPath)) {
                image.remove();
                return;
            }
            const safePath = posterPath.startsWith('/') ? posterPath : `/${posterPath}`;
            image.src = `https://image.tmdb.org/t/p/w500${safePath}`;
        } catch (_) {
            if (isCurrent(version, userId, container)) image.remove();
        }
    }

    async function loadRecommendations(container, userId, force = false) {
        if (!container || !userId || !root.ApiClient?.getJSON || !root.ApiClient?.getUrl) return;
        if (!force && activeContainer === container && activeUserId === userId && Date.now() - lastLoadedAt < REFRESH_INTERVAL_MS) return;

        activeContainer = container;
        activeUserId = userId;
        lastLoadedAt = Date.now();
        const version = ++requestVersion;
        const section = createSection(container);
        const track = section.querySelector('.wr-ktw-track');
        const reload = section.querySelector('.wr-ktw-reload');
        track.replaceChildren();
        setStatus(section, 'Loading recommendations…');
        reload.disabled = true;

        try {
            const itemsUrl = new URL(root.ApiClient.getUrl('WatchRecommendations/recommendations/items'), root.location.origin);
            itemsUrl.searchParams.set('limit', String(MAX_RESULTS));
            const results = await Promise.allSettled([
                root.ApiClient.getJSON(root.ApiClient.getUrl('WatchRecommendations/recommendations')),
                root.ApiClient.getJSON(itemsUrl.toString())
            ]);
            if (!isCurrent(version, userId, container)) return;

            const cache = results[0].status === 'fulfilled' ? results[0].value : null;
            const libraryResult = results[1].status === 'fulfilled' ? results[1].value : null;
            const libraryItems = getItems(libraryResult);
            let recommendations = getRecommendations(cache);
            if (!cache && libraryItems.length) {
                recommendations = libraryItems.map(libraryItem => ({
                    score: null,
                    item: {
                        id: read(libraryItem, 'id', 'Id'),
                        mediaType: read(libraryItem, 'type', 'Type'),
                        name: read(libraryItem, 'name', 'Name'),
                        providerIds: getProviderIds(libraryItem),
                        isInLibrary: true
                    }
                }));
            }

            const hasExternal = recommendations.some(result => {
                const item = read(result, 'item', 'Item');
                return item && !read(item, 'isInLibrary', 'IsInLibrary');
            });
            let jeReady = false;
            const jeApi = currentJeApi();
            if (hasExternal && typeof jeApi?.checkUserStatus === 'function' && typeof jeApi?.requestMedia === 'function') {
                try {
                    const status = await jeApi.checkUserStatus();
                    jeReady = status?.active === true && status?.userFound === true;
                } catch (_) {
                    jeReady = false;
                }
            }
            if (!isCurrent(version, userId, container)) return;

            const entries = buildRecommendationEntries(recommendations, libraryItems, jeReady);
            if (!entries.length) {
                const message = hasExternal && !jeReady
                    ? 'Your cached Seerr recommendations need Jellyfin Enhanced with an active, linked account.'
                    : cache?.refreshedAtUtc || cache?.RefreshedAtUtc
                        ? 'No recommendations are available for this user.'
                        : 'No cached recommendations yet. Wait for the scheduled refresh to complete.';
                setStatus(section, message);
                return;
            }

            const refreshedAt = cache?.refreshedAtUtc || cache?.RefreshedAtUtc;
            const isStale = refreshedAt && Date.now() - new Date(refreshedAt).getTime() > 48 * 60 * 60 * 1000;
            const jeFallback = hasExternal && !jeReady ? ' Seerr titles need an active linked Jellyfin Enhanced account.' : '';
            setStatus(section, `${isStale ? `Showing the last successful refresh from ${new Date(refreshedAt).toLocaleString()}.` : 'Showing your latest cached recommendations.'}${jeFallback}`);

            const posterLoads = [];
            for (const entry of entries) {
                const loadPoster = addEntryCard(track, entry, version, userId);
                if (loadPoster) posterLoads.push(loadPoster);
            }
            for (let index = 0; index < posterLoads.length; index += 4) {
                if (!isCurrent(version, userId, container)) return;
                await Promise.all(posterLoads.slice(index, index + 4).map(loadPoster => loadPoster()));
            }
        } catch (error) {
            if (!isCurrent(version, userId, container)) return;
            console.warn('Watch Recommendations: failed to load KefinTweaks row', error);
            setStatus(section, 'Recommendations could not be loaded. Please try again later.');
        } finally {
            if (isCurrent(version, userId, container)) reload.disabled = false;
        }
    }

    function findHomeContainer() {
        if (!root.cardBuilder?.renderCards) return null;
        return root.document.querySelector('.libraryPage:not(.hide) .homeSectionsContainer');
    }

    function syncHomeScreen() {
        const container = findHomeContainer();
        const userId = currentUserId();
        if (!container || !userId) {
            if (activeUserId && userId !== activeUserId) {
                requestVersion++;
                activeContainer?.querySelector(`[data-wr-ktw-id="${SECTION_ID}"]`)?.remove();
                activeContainer = null;
                activeUserId = null;
                lastLoadedAt = 0;
            }
            return;
        }
        void loadRecommendations(container, userId);
    }

    function scheduleSync() {
        if (syncTimer) root.clearTimeout(syncTimer);
        syncTimer = root.setTimeout(syncHomeScreen, 300);
    }

    function initialize() {
        addStyles();
        if (root.MutationObserver) {
            observer = new root.MutationObserver(scheduleSync);
            observer.observe(root.document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
        }
        root.setInterval(syncHomeScreen, 2500);
        root.addEventListener('hashchange', scheduleSync);
        root.addEventListener('pageshow', scheduleSync);
        scheduleSync();
    }

    initialize();
})(typeof window !== 'undefined' ? window : globalThis);