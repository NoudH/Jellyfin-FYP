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

    function toJellyseerrCardItem(entry, details) {
        if (!entry?.tmdbId || !details) return null;

        const recommendation = entry.item || {};
        const rawVoteAverage = details.voteAverage ?? details.vote_average ?? read(recommendation, 'communityRating', 'CommunityRating');
        const voteAverage = Number(rawVoteAverage);
        const mediaInfo = details.mediaInfo && typeof details.mediaInfo === 'object' ? { ...details.mediaInfo } : {};
        delete mediaInfo.jellyfinMediaId;
        delete mediaInfo.jellyfinMediaId4k;

        const premiereDate = read(recommendation, 'premiereDate', 'PremiereDate');
        const date = premiereDate ? String(premiereDate).slice(0, 10) : undefined;
        const name = details.title || details.name || read(recommendation, 'name', 'Name') || 'Untitled';

        return {
            ...details,
            id: entry.tmdbId,
            mediaType: entry.mediaType,
            title: name,
            name,
            overview: details.overview || read(recommendation, 'overview', 'Overview') || '',
            posterPath: details.posterPath || details.poster_path || null,
            voteAverage: Number.isFinite(voteAverage) ? voteAverage : 0,
            releaseDate: details.releaseDate || details.release_date || (entry.mediaType === 'movie' ? date : undefined),
            firstAirDate: details.firstAirDate || details.first_air_date || (entry.mediaType === 'tv' ? date : undefined),
            mediaInfo
        };
    }

    function attachScrollButtons(section, scroller, documentRef = root.document, ResizeObserverImpl = root.ResizeObserver) {
        const scrollButtons = documentRef.createElement('div');
        scrollButtons.className = 'emby-scrollbuttons padded-right hide';

        function createButton(direction) {
            const button = documentRef.createElement('button');
            button.type = 'button';
            button.className = 'emby-scrollbuttons-button paper-icon-button-light';
            button.setAttribute('data-ripple', 'false');
            button.setAttribute('data-direction', direction);
            button.setAttribute('title', direction === 'left' ? 'Previous' : 'Next');
            button.setAttribute('aria-label', direction === 'left' ? 'Scroll recommendations left' : 'Scroll recommendations right');
            button.disabled = true;

            const icon = documentRef.createElement('span');
            icon.className = `material-icons chevron_${direction}`;
            icon.setAttribute('aria-hidden', 'true');
            button.appendChild(icon);
            button.addEventListener('click', () => {
                scroller.scrollBy({
                    left: (direction === 'left' ? -1 : 1) * scroller.clientWidth * 0.8,
                    behavior: 'smooth'
                });
            });
            return button;
        }

        const leftButton = createButton('left');
        const rightButton = createButton('right');
        scrollButtons.appendChild(leftButton);
        scrollButtons.appendChild(rightButton);

        function update() {
            const maxScroll = Math.max(0, scroller.scrollWidth - scroller.clientWidth);
            leftButton.disabled = scroller.scrollLeft <= 1;
            rightButton.disabled = scroller.scrollLeft >= maxScroll - 1;
            scrollButtons.classList.toggle('hide', maxScroll <= 1);
        }

        section.appendChild(scrollButtons);
        scroller.addEventListener('scroll', update, { passive: true });

        let resizeObserver;
        if (typeof ResizeObserverImpl === 'function') {
            resizeObserver = new ResizeObserverImpl(update);
            resizeObserver.observe(scroller);
            if (scroller.firstElementChild) resizeObserver.observe(scroller.firstElementChild);
        }

        update();
        return {
            element: scrollButtons,
            update,
            disconnect() {
                resizeObserver?.disconnect();
                scroller.removeEventListener('scroll', update);
            }
        };
    }

    const helpers = { attachScrollButtons, buildRecommendationEntries, getTmdbId, normalizeMediaType, toJellyseerrCardItem };
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
        style.textContent = `.wr-ktw-section { order: 1000; }`;
        root.document.head.appendChild(style);
    }

    function createSection(container) {
        let section = container.querySelector(`[data-wr-ktw-id="${SECTION_ID}"]`);
        if (section) return section;

        section = root.document.createElement('div');
        section.className = 'verticalSection emby-scroller-container jellyseerr-details-section wr-ktw-section';
        section.dataset.wrKtwId = SECTION_ID;
        section.dataset.jellyseerrSection = 'true';

        const title = root.document.createElement('h2');
        title.className = 'sectionTitle sectionTitle-cards focuscontainer-x padded-right';
        title.textContent = 'Recommended';

        const scroller = root.document.createElement('div');
        scroller.className = 'padded-top-focusscale padded-bottom-focusscale no-padding emby-scroller';
        scroller.dataset.horizontal = 'true';
        scroller.dataset.centerfocus = 'card';
        scroller.dataset.scrollModeX = 'custom';
        scroller.style.scrollSnapType = 'none';
        scroller.style.touchAction = 'auto';
        scroller.style.overscrollBehaviorX = 'contain';
        scroller.style.overscrollBehaviorY = 'auto';
        scroller.style.webkitOverflowScrolling = 'touch';

        const track = root.document.createElement('div');
        track.className = 'focuscontainer-x itemsContainer scrollSlider animatedScrollX wr-ktw-track';
        track.style.whiteSpace = 'nowrap';
        scroller.appendChild(track);
        section.append(title, scroller);
        section._wrScrollControls = attachScrollButtons(section, scroller);
        container.appendChild(section);
        return section;
    }

    function removeRecommendationSection(container) {
        const section = container?.querySelector(`[data-wr-ktw-id="${SECTION_ID}"]`);
        if (!section) return;
        section._wrScrollControls?.disconnect();
        section.remove();
    }

    function getItems(result) {
        const items = read(result, 'items', 'Items');
        return Array.isArray(items) ? items : [];
    }

    function getRecommendations(result) {
        const recommendations = read(result, 'recommendations', 'Recommendations');
        return Array.isArray(recommendations) ? recommendations : [];
    }

    function buildLocalCard(entry) {
        try {
            return root.cardBuilder?.buildCard?.(entry.libraryItem, false, 'portrait') || null;
        } catch (error) {
            console.warn('Watch Recommendations: failed to build a library card', error);
            return null;
        }
    }

    async function buildExternalCard(entry, version, userId, container) {
        const je = root.JellyfinEnhanced;
        const jeApi = currentJeApi();
        const createCard = je?.jellyseerrUI?.createJellyseerrCard;
        const getDetails = entry.mediaType === 'movie' ? jeApi?.fetchMovieDetails : jeApi?.fetchTvShowDetails;
        if (typeof createCard !== 'function' || typeof getDetails !== 'function') return null;

        try {
            const details = await getDetails.call(jeApi, entry.tmdbId);
            if (!isCurrent(version, userId, container)) return null;
            const cardItem = toJellyseerrCardItem(entry, details);
            return cardItem ? createCard.call(je.jellyseerrUI, cardItem, true, true) : null;
        } catch (error) {
            console.warn('Watch Recommendations: failed to build a Seerr card', error);
            return null;
        }
    }

    async function buildEntryCard(entry, version, userId, container) {
        if (!isCurrent(version, userId, container)) return null;
        return entry.isInLibrary
            ? buildLocalCard(entry)
            : buildExternalCard(entry, version, userId, container);
    }

    async function loadRecommendations(container, userId, force = false) {
        if (!container || !userId || !root.ApiClient?.getJSON || !root.ApiClient?.getUrl) return;
        if (!force && activeContainer === container && activeUserId === userId && Date.now() - lastLoadedAt < REFRESH_INTERVAL_MS) return;

        if (activeUserId && activeUserId !== userId) {
            requestVersion++;
            removeRecommendationSection(activeContainer);
            lastLoadedAt = 0;
        }
        activeContainer = container;
        activeUserId = userId;
        lastLoadedAt = Date.now();
        const version = ++requestVersion;

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
            const jeCards = root.JellyfinEnhanced?.jellyseerrUI;
            if (hasExternal
                && typeof jeCards?.createJellyseerrCard === 'function'
                && typeof jeApi?.checkUserStatus === 'function'
                && typeof jeApi?.requestMedia === 'function') {
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
                removeRecommendationSection(container);
                return;
            }

            const section = createSection(container);
            const track = section.querySelector('.wr-ktw-track');
            track.replaceChildren();
            for (let index = 0; index < entries.length; index += 4) {
                if (!isCurrent(version, userId, container)) return;
                const cards = await Promise.all(entries.slice(index, index + 4)
                    .map(entry => buildEntryCard(entry, version, userId, container)));
                if (!isCurrent(version, userId, container)) return;
                cards.filter(Boolean).forEach(card => track.appendChild(card));
                section._wrScrollControls?.update();
            }

            if (!track.childElementCount) removeRecommendationSection(container);
        } catch (error) {
            if (!isCurrent(version, userId, container)) return;
            console.warn('Watch Recommendations: failed to load KefinTweaks row', error);
        }
    }

    function findHomeContainer() {
        if (!root.cardBuilder?.buildCard) return null;
        return root.document.querySelector('.libraryPage:not(.hide) .homeSectionsContainer');
    }

    function syncHomeScreen() {
        const container = findHomeContainer();
        const userId = currentUserId();
        if (!container || !userId) {
            if (activeUserId && userId !== activeUserId) {
                requestVersion++;
                removeRecommendationSection(activeContainer);
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