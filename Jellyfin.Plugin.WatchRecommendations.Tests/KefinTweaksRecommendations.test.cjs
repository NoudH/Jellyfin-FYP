const assert = require('node:assert/strict');
const test = require('node:test');
const { attachScrollButtons, buildRecommendationEntries, toJellyseerrCardItem } = require('../Jellyfin.Plugin.WatchRecommendations/Integrations/KefinTweaksRecommendations.js');

class FakeElement {
    constructor(tagName) {
        this.tagName = tagName;
        this.children = [];
        this.attributes = {};
        this.listeners = {};
        this.classList = {
            values: new Set(),
            add: value => this.classList.values.add(value),
            contains: value => this.classList.values.has(value),
            toggle: (value, force) => {
                const enabled = force ?? !this.classList.values.has(value);
                if (enabled) this.classList.values.add(value);
                else this.classList.values.delete(value);
                return enabled;
            }
        };
    }

    appendChild(child) {
        this.children.push(child);
        child.parentElement = this;
        return child;
    }

    setAttribute(name, value) {
        this.attributes[name] = value;
    }

    addEventListener(name, listener) {
        this.listeners[name] = listener;
    }

    removeEventListener(name) {
        delete this.listeners[name];
    }

    scrollBy({ left }) {
        this.scrollLeft += left;
        this.listeners.scroll?.();
    }
}

function recommendation(id, mediaType, { inLibrary = false, tmdbId, watched = false, started = false, score = 0 } = {}) {
    return {
        score,
        item: {
            id,
            mediaType,
            name: id,
            isInLibrary: inLibrary,
            isWatched: watched,
            isStarted: started,
            providerIds: tmdbId ? { Tmdb: String(tmdbId) } : {}
        }
    };
}

test('adds Jellyfin scroll arrows that track and control horizontal scrolling', () => {
    const section = new FakeElement('section');
    const scroller = new FakeElement('div');
    scroller.clientWidth = 100;
    scroller.scrollWidth = 300;
    scroller.scrollLeft = 0;

    const controls = attachScrollButtons(section, scroller, { createElement: tagName => new FakeElement(tagName) });
    const [leftButton, rightButton] = controls.element.children;

    assert.equal(controls.element.className.includes('emby-scrollbuttons'), true);
    assert.equal(leftButton.attributes['data-direction'], 'left');
    assert.equal(rightButton.attributes['data-direction'], 'right');
    assert.equal(leftButton.disabled, true);
    assert.equal(rightButton.disabled, false);

    rightButton.listeners.click();
    assert.equal(scroller.scrollLeft, 80);
    assert.equal(leftButton.disabled, false);

    leftButton.listeners.click();
    assert.equal(scroller.scrollLeft, 0);
    assert.equal(leftButton.disabled, true);

    scroller.scrollWidth = scroller.clientWidth;
    controls.update();
    assert.equal(controls.element.classList.contains('hide'), true);
    controls.disconnect();
});

test('preserves ranked mixed order and resolves library recommendations to DTOs', () => {
    const localId = 'c0d99bc7664544f6b8f2f47d8829c9b0';
    const localDto = { Id: localId, Type: 'Series', Name: 'Local show' };
    const entries = buildRecommendationEntries([
        recommendation('external', 'Movie', { tmdbId: 100, score: 0.9 }),
        recommendation(localId, 'Series', { inLibrary: true, score: 0.8 })
    ], [localDto], true);

    assert.deepEqual(entries.map(entry => entry.item.id), ['external', localId]);
    assert.equal(entries[0].isInLibrary, false);
    assert.equal(entries[0].tmdbId, 100);
    assert.equal(entries[1].libraryItem, localDto);
});

test('adapts external recommendations to Jellyfin Enhanced Seerr card data', () => {
    const cardItem = toJellyseerrCardItem({
        item: { name: 'A Local Name', overview: 'A short overview' },
        mediaType: 'tv',
        tmdbId: 321,
        isInLibrary: false
    }, {
        name: 'A Show',
        poster_path: '/poster.jpg',
        vote_average: 8.2,
        first_air_date: '2024-01-02',
        mediaInfo: { jellyfinMediaId: 'stale-id', status: 1 }
    });

    assert.equal(cardItem.id, 321);
    assert.equal(cardItem.mediaType, 'tv');
    assert.equal(cardItem.title, 'A Show');
    assert.equal(cardItem.posterPath, '/poster.jpg');
    assert.equal(cardItem.voteAverage, 8.2);
    assert.equal(cardItem.firstAirDate, '2024-01-02');
    assert.equal(cardItem.overview, 'A short overview');
    assert.equal(cardItem.mediaInfo.status, 1);
    assert.equal(cardItem.mediaInfo.jellyfinMediaId, undefined);
});

test('keeps library results available and omits off-library titles when JE is unavailable', () => {
    const localId = 'c0d99bc7664544f6b8f2f47d8829c9b0';
    const localDto = { Id: localId, Type: 'Movie', Name: 'Local movie' };
    const entries = buildRecommendationEntries([
        recommendation('external', 'Movie', { tmdbId: 100, score: 0.9 }),
        recommendation(localId, 'Movie', { inLibrary: true, score: 0.8 })
    ], [localDto], false);

    assert.deepEqual(entries.map(entry => entry.libraryItem), [localDto]);
});

test('prefers a newly imported library match over a stale external cache entry', () => {
    const localDto = { Id: '7d1d1d1d7d1d1d1d7d1d1d1d7d1d1d1d', Type: 'Movie', ProviderIds: { Tmdb: '100' } };
    const entries = buildRecommendationEntries([
        recommendation('external', 'Movie', { tmdbId: 100, score: 0.9 })
    ], [localDto], false);

    assert.equal(entries.length, 1);
    assert.equal(entries[0].isInLibrary, true);
    assert.equal(entries[0].libraryItem, localDto);
});

test('excludes watched, started, unsupported, unresolvable, and duplicate recommendations', () => {
    const localId = 'c0d99bc7664544f6b8f2f47d8829c9b0';
    const localDto = { Id: localId, Type: 'Movie', ProviderIds: { Tmdb: '100' } };
    const entries = buildRecommendationEntries([
        recommendation('watched', 'Movie', { tmdbId: 1, watched: true }),
        recommendation('started', 'Series', { tmdbId: 2, started: true }),
        recommendation('music', 'Audio', { tmdbId: 3 }),
        { score: 0.7, item: { id: 'unknown', mediaType: 'Movie', providerIds: { Tmdb: '4' } } },
        recommendation('missing-tmdb', 'Movie'),
        recommendation('external', 'Movie', { tmdbId: 100, score: 0.9 }),
        recommendation(localId, 'Movie', { inLibrary: true, tmdbId: 100, score: 0.8 })
    ], [localDto], true);

    assert.equal(entries.length, 1);
    assert.equal(entries[0].libraryItem, localDto);
});