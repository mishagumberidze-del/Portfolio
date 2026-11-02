// UI capability hint only. The sharing gateway independently enforces every route.
export const sharedAccess = document.querySelector('meta[name="spotify-taste-access"]')?.getAttribute('content') === 'shared'
