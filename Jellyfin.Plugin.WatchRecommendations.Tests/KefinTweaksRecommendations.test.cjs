const assert = require('node:assert/strict');
const test = require('node:test');
const { buildRecommendationEntries } = require('../Jellyfin.Plugin.WatchRecommendations/Integrations/KefinTweaksRecommendations.js');

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