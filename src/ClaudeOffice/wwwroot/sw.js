'use strict';
/*
 * 휴대폰 앱(설치형 웹앱)용 서비스 워커.
 * 화면 파일만 네트워크 우선으로 받고, 연결이 끊겼을 때는 마지막으로 받은 것을 보여 준다(앱이 하얗게 비지 않게).
 * 데이터(Firebase 실시간 스트림)는 다른 주소라 건드리지 않는다.
 */

const CACHE = 'claude-office-shell';

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (e) => e.waitUntil(self.clients.claim()));

self.addEventListener('fetch', (e) => {
  const req = e.request;
  if (req.method !== 'GET' || new URL(req.url).origin !== self.location.origin) return;
  e.respondWith(
    fetch(req)
      .then((res) => {
        if (res.ok) {
          const copy = res.clone();
          caches.open(CACHE).then((c) => c.put(req, copy));
        }
        return res;
      })
      .catch(() => caches.match(req, { ignoreSearch: true }).then((hit) => hit || Response.error())),
  );
});
