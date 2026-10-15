'use strict';
// Service Worker der Handy-Seite: zeigt Push-Benachrichtigungen vom Server an und öffnet beim Antippen die passende Seite.
// Er speichert bewusst nichts zwischen (kein fetch-Handler) – die Seite kommt immer frisch vom Server.

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', e => e.waitUntil(self.clients.claim()));

self.addEventListener('push', e => {
  let n = {};
  try { n = e.data ? e.data.json() : {}; } catch { n = { body: e.data ? e.data.text() : '' }; }
  e.waitUntil(self.registration.showNotification(n.title || 'Filament-Verwaltung', {
    body: n.body || '',
    tag: n.tag || undefined,
    data: { url: n.url || '/' },
    icon: 'icon-192.png',
    badge: 'icon-192.png'
  }));
});

self.addEventListener('notificationclick', e => {
  e.notification.close();
  const ziel = new URL(e.notification.data?.url || '/', self.location.origin).href;
  e.waitUntil((async () => {
    const fenster = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const f of fenster) {
      if (new URL(f.url).origin === self.location.origin) {
        await f.focus();
        return f.navigate(ziel);
      }
    }
    return self.clients.openWindow(ziel);
  })());
});
