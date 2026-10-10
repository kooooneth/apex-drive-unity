// Apex Drive service worker: lets the game be installed as an app and keeps it playable offline once it has loaded.
const CACHE = "apex-drive-v1";
const CORE = ["./", "./index.html", "./manifest.webmanifest", "./icons/icon-192.png", "./icons/icon-512.png", "./unity/", "./unity/index.html"];

self.addEventListener("install", (e) => {
  e.waitUntil((async () => {
    const c = await caches.open(CACHE);
    await Promise.all(CORE.map((u) => c.add(u).catch(() => {})));
    await self.skipWaiting();
  })());
});

self.addEventListener("activate", (e) => {
  e.waitUntil((async () => {
    for (const k of await caches.keys()) if (k !== CACHE && k.startsWith("apex-drive-")) await caches.delete(k);
    await self.clients.claim();
  })());
});

self.addEventListener("fetch", (e) => {
  const req = e.request;
  if (req.method !== "GET") return;
  const url = new URL(req.url);
  if (url.origin !== location.origin) return;

  // the big game files: cache first. Their address carries the build version, so a new build is fetched once and the old copy is dropped.
  if (url.pathname.includes("/unity/Build/")) {
    e.respondWith((async () => {
      const c = await caches.open(CACHE);
      const hit = await c.match(req);
      if (hit) return hit;
      const res = await fetch(req);
      if (res.ok) {
        c.put(req, res.clone());
        for (const k of await c.keys()) {
          const u = new URL(k.url);
          if (u.pathname === url.pathname && u.search !== url.search) c.delete(k);
        }
      }
      return res;
    })());
    return;
  }

  // pages and small files: the network first (so updates show up), the saved copy when offline
  e.respondWith(
    fetch(req)
      .then((res) => {
        if (res.ok) { const copy = res.clone(); caches.open(CACHE).then((c) => c.put(req, copy)); }
        return res;
      })
      .catch(async () => (await caches.match(req)) || (await caches.match("./unity/index.html")))
  );
});
