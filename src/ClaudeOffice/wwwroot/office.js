'use strict';
/*
 * Claude Office — 미니어처 사무실 화면
 *
 * 그리는 방식
 *   - 픽셀아트(바닥·벽·책상·사람)는 낮은 해상도의 보조 캔버스(art)에 1:1로 그린 뒤
 *     정수 배율(K)로 확대해 메인 캔버스에 붙인다 → 픽셀이 뭉개지지 않는다.
 *   - 글자(이름표·말풍선·팀 이름)는 메인 캔버스에 화면 해상도로 따로 그린다 → 한글이 선명하다.
 *
 * 데이터
 *   - 앱(C#)이 대화 기록을 감시해 만든 스냅샷을 window.chrome.webview 메시지로 보내 준다.
 *   - 휴대폰 보기: PC가 보여 준 QR 주소(# 뒤에 db·u·k)로 열면, PC가 Firebase 에 올린 암호화 요약을 받아 해독해 그린다(보기 전용).
 *   - 브라우저로 직접 열거나 ?demo 를 붙이면 견본 데이터로 모든 상태를 돌아가며 보여 준다.
 */

// ───────────────────────────── 연결 ─────────────────────────────

const host = window.chrome && window.chrome.webview ? window.chrome.webview : null;
const params = new URLSearchParams(location.search);
// 휴대폰 보기 연결 정보: PC가 보여 준 QR 주소의 # 뒤(데이터 주소 db·경로 u·해독 키 k). # 뒤 값은 웹 서버로 전송되지 않는다.
// 설치한 앱(홈 화면)은 # 없이 시작 주소로 열리므로, QR로 처음 열 때 이 기기에 저장해 두고 다음부터는 그것을 쓴다.
const LINK_KEY = 'co.m.link';

// 데이터 주소는 Firebase Realtime Database(또는 이 PC의 시험 서버)만 — 공용 페이지라 남이 만든 링크로 엉뚱한 서버에 붙지 않게
function okDbUrl(s) {
  try {
    const u = new URL(s);
    if (u.protocol === 'http:' || u.protocol === 'https:') {
      if (['127.0.0.1', 'localhost'].includes(u.hostname)) return true;
      return u.protocol === 'https:' && /\.(firebaseio\.com|firebasedatabase\.app)$/.test(u.hostname);
    }
  } catch { /* 주소 모양이 아님 */ }
  return false;
}

function readRemoteLink() {
  const h = new URLSearchParams(location.hash.slice(1));
  if (h.has('db') && h.has('u') && h.has('k') && okDbUrl(h.get('db'))) {
    const link = { db: h.get('db'), u: h.get('u'), k: h.get('k') };
    try { localStorage.setItem(LINK_KEY, JSON.stringify(link)); } catch { /* 저장 못 해도 이번에는 볼 수 있다 */ }
    history.replaceState(null, '', location.pathname + location.search);   // 주소창·방문 기록에서 키를 지운다
    return link;
  }
  if (location.hostname === 'office.local') return null;   // PC 앱 자체 화면(WebView2 가상 주소)은 저장된 연결 정보를 쓰지 않는다
  try {
    const s = JSON.parse(localStorage.getItem(LINK_KEY));
    return s && s.db && s.u && s.k && okDbUrl(s.db) ? s : null;
  } catch {
    return null;
  }
}

const remoteLink = params.has('demo') ? null : readRemoteLink();
// 이미 열린 화면에서 QR 주소(# 만 다름)로 가면 브라우저가 페이지를 다시 읽지 않는다 — 새 연결 정보로 다시 연다(키를 새로 만든 뒤 등)
window.addEventListener('hashchange', () => {
  if (new URLSearchParams(location.hash.slice(1)).has('k')) location.reload();
});
// 웹 서버(Firebase Hosting)에 올린 휴대폰 화면 — 연결 정보가 없으면 견본 대신 "QR을 찍어 주세요"를 보여 준다
const HOSTED = location.protocol === 'https:' && location.hostname !== 'office.local';
const REMOTE = !params.has('demo') && (!!remoteLink || HOSTED);
const DEMO = !REMOTE && (!host || params.has('demo'));

/** 앱(C#)에 알리기. 휴대폰 보기에서는 앱이 없으니 보내지 않는다. */
const send = (msg) => { if (host && !REMOTE) host.postMessage(msg); };

let data = { now: Date.now(), sessions: [] };
let skew = 0;                                   // 앱 시계와 화면 시계 차이(같은 PC라 보통 0)
const now = () => Date.now() + skew;

// 견본 화면은 실제 화면과 같은 저장소(같은 주소)를 쓰므로 이름표를 따로 붙인다.
// 그렇지 않으면 견본에서 바꾼 설정이 실제 화면에 남고, 실제로 체크한 세션 ID 때문에 견본이 빈 사무실로 보인다.
// 휴대폰 보기도 따로(휴대폰에서 고른 보일 인원 등은 그 휴대폰에만).
const STORE_PREFIX = REMOTE ? 'co.m.' : DEMO ? 'co.demo.' : 'co.';
const store = {
  get(key, def) {
    try { const v = localStorage.getItem(STORE_PREFIX + key); return v === null ? def : JSON.parse(v); }
    catch { return def; }
  },
  set(key, v) { try { localStorage.setItem(STORE_PREFIX + key, JSON.stringify(v)); } catch { /* 저장 못 해도 동작엔 지장 없음 */ } },
};

let showAway = store.get('showAway', true);
let waitOnly = store.get('waitOnly', false);   // ✋ 기다리는 직원만 보기
let pinned = store.get('pinned', false);

// 직원 표시: 최대 인원(maxShown)과 고르는 방식(pickMode: recent=최근 사용순 / manual=직접 고르기, pickedIds=체크한 세션)
const SHOWN_MIN = 1, SHOWN_MAX = 30;
let maxShown = Math.min(SHOWN_MAX, Math.max(SHOWN_MIN, Number(store.get('maxShown', 8)) || 8));
let pickMode = store.get('pickMode', 'recent') === 'manual' ? 'manual' : 'recent';
const pickedIds = new Set(store.get('picked', []));

// 표시 기간(시간): 마지막 대화가 이 안에 있는 세션만 앱(C#)이 보내 준다. 값은 화면이 저장하고 시작할 때마다 앱에 알린다
const WINDOW_CHOICES = [[12, '12시간'], [24, '24시간'], [72, '3일'], [168, '7일']];
let windowHours = Number(store.get('windowHours', 12));
if (!WINDOW_CHOICES.some(([h]) => h === windowHours)) windowHours = 12;
const windowLabel = () => WINDOW_CHOICES.find(([h]) => h === windowHours)[1];

// 사무실 이름(간판·창 제목). 비워 두면 기본 이름
const DEFAULT_OFFICE = 'Claude Office';
let officeName = String(store.get('officeName', '') || '').trim();
const officeTitle = () => officeName || DEFAULT_OFFICE;
const officeSign = () => officeName || 'CLAUDE OFFICE';

// 세션별 별명·메모 — { [세션 ID]: { nick, memo, t } }
// 세션 ID(기록 파일 이름)가 키라 /rename 으로 세션명이 바뀌어도 그대로 남는다. 사무실에서 사라진 세션 것도 지우지 않는다(다시 나타나면 그대로).
const NOTES_MAX = 300;
const notes = store.get('notes', {}) || {};
let remoteShare = null;   // 휴대폰 보기: PC가 함께 올린 { office, notes } — 별명·메모·사무실 이름은 PC에서 정한 그대로
const noteOf = (id) => (REMOTE ? remoteShare?.notes?.[id] : notes[id]) || {};
const displayName = (s) => noteOf(s.id).nick || s.name;

/** 사무실 이름·별명·메모를 앱에 넘긴다 — 휴대폰 보기를 켰으면 요약과 함께 암호화해 올라간다. */
const shareToApp = () => send({ cmd: 'share', data: { office: officeName, notes } });

function setNote(id, patch) {
  const n = { ...noteOf(id), ...patch, t: Date.now() };
  if (!n.nick) delete n.nick;
  if (!n.memo || !n.memo.trim()) delete n.memo;
  if (n.nick || n.memo) notes[id] = n;
  else delete notes[id];
  const ids = Object.keys(notes);
  if (ids.length > NOTES_MAX)   // 오래 안 고친 것부터 정리(끝없이 쌓이지 않게)
    ids.sort((a, b) => notes[a].t - notes[b].t).slice(0, ids.length - NOTES_MAX).forEach((k) => delete notes[k]);
  store.set('notes', notes);
  shareToApp();
}

/**
 * 앱이 파일(데이터 폴더 share.json)에 남겨 둔 사본과 합친다 — 이 화면에 없거나 사본 쪽을 더 나중에 고친 별명·메모는 사본 것으로
 * (화면 저장소가 지워져도 되살아난다). 합친 결과를 다시 앱에 보내 사본도 맞춘다.
 */
function onSavedShare(saved) {
  if (saved && typeof saved === 'object') {
    for (const [id, n] of Object.entries(saved.notes || {})) {
      if (!n || typeof n !== 'object' || (notes[id] && (notes[id].t || 0) >= (n.t || 0))) continue;
      const nick = typeof n.nick === 'string' ? n.nick.slice(0, 30) : '';
      const memo = typeof n.memo === 'string' ? n.memo.slice(0, 2000) : '';
      if (nick || memo) notes[id] = { ...(nick && { nick }), ...(memo && { memo }), t: Number(n.t) || Date.now() };
    }
    store.set('notes', notes);
    if (!officeName && typeof saved.office === 'string' && saved.office.trim()) setOfficeName(saved.office.slice(0, 30));
  }
  shareToApp();
  if (selectedId) { noteFor = null; renderPanel(); }
  refreshSettings();
}

/** 알림을 눌러 창을 열었을 때: 그 직원의 상세 패널을 연다. */
function selectSession(id) {
  if (!data.sessions.some((s) => s.id === id)) return;
  if (!settingsEl.hidden) setSettingsOpen(false);
  selectedId = id;
  drawerFolded = false;
  renderPanel();
}
let selectedId = params.get('select');   // 화면 점검용: ?demo&select=demo-b1 이면 상세 패널을, select=settings 면 설정을 연 채로 시작
let hoverId = null;

function onSnapshot(snap) {
  if (!snap || !Array.isArray(snap.sessions)) return;
  data = snap;
  skew = (snap.now || Date.now()) - Date.now();
  refreshHud();
  refreshHookButton();
  refreshSettings();
  if (selectedId) renderPanel();
}

// ───────────────────────────── 상태별 표현 ─────────────────────────────
// pose: 사람 자세 / screen: 모니터 화면 / bubble: 말풍선 모양 / tone: 상태 글자색

const ACT = {
  thinking:   { label: '생각 중',     pose: 'think', screen: 'cursor', bubble: 'thought', tone: '#a9c8ff', busy: true },
  talking:    { label: '답변 작성',   pose: 'type',  screen: 'doc',    bubble: 'normal',  tone: '#a9c8ff', busy: true },
  reading:    { label: '읽는 중',     pose: 'read',  screen: 'doc',    bubble: 'normal',  tone: '#a8e6a1', busy: true },
  searching:  { label: '찾는 중',     pose: 'type',  screen: 'search', bubble: 'normal',  tone: '#a8e6a1', busy: true },
  writing:    { label: '코드 작성',   pose: 'type',  screen: 'code',   bubble: 'normal',  tone: '#ffd27a', busy: true },
  running:    { label: '명령 실행',   pose: 'run',   screen: 'term',   bubble: 'normal',  tone: '#7dffa0', busy: true },
  browsing:   { label: '웹 검색',     pose: 'type',  screen: 'web',    bubble: 'normal',  tone: '#a9c8ff', busy: true },
  delegating: { label: '인턴 지휘',   pose: 'type',  screen: 'team',   bubble: 'normal',  tone: '#d7a6ff', busy: true },
  planning:   { label: '계획 정리',   pose: 'think', screen: 'todo',   bubble: 'thought', tone: '#ffd27a', busy: true },
  working:    { label: '작업 중',     pose: 'type',  screen: 'code',   bubble: 'normal',  tone: '#ffd27a', busy: true },
  asking:     { label: '답변 기다림', pose: 'raise', screen: 'alert',  bubble: 'alert',   tone: '#ffb347', wait: true },
  waiting:    { label: '승인 대기',   pose: 'raise', screen: 'alert',  bubble: 'alert',   tone: '#ffb347', wait: true },
  idle:       { label: '대기',        pose: 'idle',  screen: 'saver',  bubble: 'calm',    tone: '#c9c3d9' },
  sleeping:   { label: '졸음',        pose: 'sleep', screen: 'off',    bubble: 'zzz',     tone: '#8f88a8' },
  away:       { label: '퇴근',        pose: 'away',  screen: 'off',    bubble: 'none',    tone: '#6f6987' },
};
const actOf = (s) => ACT[s.act] || ACT.working;

const BUBBLE = {
  normal:  { bg: '#ffffff', border: '#2a2440', fg: '#221d33' },
  thought: { bg: '#f1f0ff', border: '#5a5180', fg: '#3a3360', thought: true },
  alert:   { bg: '#ffd36b', border: '#8a4b00', fg: '#3a2000', pulse: true },
  calm:    { bg: 'rgba(255,255,255,0.80)', border: 'rgba(42,36,64,0.55)', fg: '#4a4560' },
};

// ───────────────────────────── 색 ─────────────────────────────

const PAL = {
  wall: '#3d3558', wallStripe: '#38304f', wainscot: '#2f2843', wainscotHi: '#4a4068', base: '#221d33',
  floorA: '#6f5139', floorB: '#674b35', floorLine: '#57402e',
  frame: '#e9e2cf', frameDark: '#b9ae93', sill: '#d8cfb6',
  plaque: '#5a3d26', plaqueIn: '#7d5536',
  deskTop: '#b0814f', deskTopHi: '#c99a66', deskFront: '#8a5f39', deskShade: '#6e4a2c', deskLeg: '#5a3d24',
  monitor: '#2a2a36', monitorHi: '#45455a',
  chair: '#40507a', chairHi: '#566aa0', chairDark: '#2b3553',
  shoe: '#2b2b33', keyboard: '#d5d9e2',
};
const SKINS = ['#f6d5b8', '#efc39c', '#d9a57a', '#b97f55', '#8d5a3a'];
const HAIRS = ['#2b1d16', '#4a2f1e', '#7a4a25', '#c48a3a', '#1e1e2a', '#9b3b2b', '#77778a', '#e3c78f', '#3b2a4a'];
const SHIRTS = ['#4f86c6', '#d9665b', '#5fa37a', '#d1a530', '#8e6cc9', '#e07a9b', '#3fa9a6', '#d98c3f', '#6b7a8f', '#5c6bd6'];
const PANTS = ['#2e3a55', '#3a3a44', '#4a3b2e', '#27344a', '#3f2f45'];
const RUGS = ['#4b5c86', '#6e4c70', '#4a6f5b', '#7a6142', '#5a4c80', '#7a4c4c', '#3f6a78'];

function hash(str) {
  let h = 2166136261;
  for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 16777619); }
  return h >>> 0;
}

const shadeCache = new Map();
function shade(hex, f) {
  const key = hex + f;
  let v = shadeCache.get(key);
  if (v) return v;
  const n = parseInt(hex.slice(1), 16);
  const c = (x) => Math.max(0, Math.min(255, Math.round(x * f)));
  v = '#' + ((c((n >> 16) & 255) << 16) | (c((n >> 8) & 255) << 8) | c(n & 255)).toString(16).padStart(6, '0');
  shadeCache.set(key, v);
  return v;
}

// 세션 ID로 정해지는 생김새(같은 세션은 늘 같은 얼굴)
const lookCache = new Map();
function looks(id) {
  let L = lookCache.get(id);
  if (L) return L;
  const h = hash(id), h2 = hash(id + '#');
  L = {
    skin: SKINS[h % SKINS.length],
    hair: HAIRS[(h >>> 4) % HAIRS.length],
    shirt: SHIRTS[(h >>> 9) % SHIRTS.length],
    pants: PANTS[(h >>> 14) % PANTS.length],
    hairStyle: (h2 >>> 2) % 3,
    glasses: (h2 >>> 6) % 4 === 0,
    deco: (h2 >>> 9) % 4,
    blink: h2 % 3200,
  };
  lookCache.set(id, L);
  return L;
}

// ───────────────────────────── 캔버스 / 레이아웃 ─────────────────────────────

const canvas = document.getElementById('office');
const ctx = canvas.getContext('2d');
const hudEl = document.getElementById('hud');
const art = document.createElement('canvas');
const g = art.getContext('2d');

const FONT = '"Malgun Gothic", "Segoe UI", "Segoe UI Emoji", sans-serif';
const CELL_W = 66, CELL_H = 64;       // 책상 한 칸(아트 픽셀)
const WALL_H = 46;
const ZONE_PAD = 6, ZONE_HEAD = 15, ZONE_GAP = 8, ROW_GAP = 8, MAX_COLS = 5;

let dpr = 1, K = 3, U = 3;            // K: 화면픽셀/아트픽셀(정수), U: CSS픽셀/아트픽셀
let logicalW = 0, logicalH = 0, cssW = 0;
let zones = [], desks = [];
let layoutKey = '';
let fontName = 12, fontSmall = 11, fontBubble = 12;

// 책상에 앉힐 직원: 퇴근 숨김 → (직접 고르기면) 체크한 직원만 → 최대 인원을 넘으면 최근에 일한 순으로 자른다.
// 자르기만 하고 순서는 바꾸지 않는다 — 자리는 언제나 출근 순서(C#이 보낸 순서)라 책상이 뒤섞이지 않는다.
function visibleSessions() {
  let list = data.sessions.filter((s) => showAway || s.act !== 'away');
  if (waitOnly) list = list.filter(isWaiting);
  if (pickMode === 'manual') list = list.filter((s) => pickedIds.has(s.id));
  if (list.length <= maxShown) return list;
  const keep = new Set([...list].sort((a, b) => (b.last || 0) - (a.last || 0)).slice(0, maxShown).map((s) => s.id));
  return list.filter((s) => keep.has(s.id));
}

function isWaiting(s) { return s.act === 'waiting' || s.act === 'asking'; }

/** 체크한 직원 중 지금 기록이 있는 사람 수(12시간이 지나 사라진 세션은 세지 않는다). */
const pickedCount = () => data.sessions.filter((s) => pickedIds.has(s.id)).length;

function ensureLayout() {
  const list = visibleSessions();
  const vw = viewW();   // 펼친 서랍(상세 패널·설정) 자리를 뺀 폭 — 서랍이 직원을 가리지 않게
  const key = [vw, innerHeight, devicePixelRatio, ...list.map((s) => s.id + '|' + s.team)].join(',');
  if (key === layoutKey) return;
  layoutKey = key;

  dpr = window.devicePixelRatio || 1;
  // 팀(작업 폴더)별로 묶기 — 먼저 출근한 순서 유지
  const teams = new Map();
  for (const s of list) {
    if (!teams.has(s.team)) teams.set(s.team, []);
    teams.get(s.team).push(s);
  }

  // 화면에 다 들어가는 가장 큰 배율을 고른다(아트 1픽셀 = 화면 k픽셀, k는 정수). 그래도 넘치면 세로 스크롤.
  // 고해상도(배율 125% 등)에서는 k를 1씩 내려가며 더 촘촘하게 맞춘다.
  let best = null;
  const kMax = Math.max(2, Math.round(4 * dpr)), kMin = Math.max(2, Math.round(2 * dpr));
  for (let k = kMax; k >= kMin; k--) {
    const lw = Math.floor((vw * dpr) / k);
    const viewLh = Math.floor((innerHeight * dpr) / k);
    const plan = planZones([...teams.entries()], lw);
    best = { k, lw, viewLh, plan };
    if (plan.height <= viewLh) break;
  }

  K = best.k;
  U = K / dpr;
  logicalW = best.lw;
  logicalH = Math.max(best.viewLh, best.plan.height);
  zones = best.plan.zones;
  desks = best.plan.desks;
  cssW = logicalW * U;

  art.width = logicalW;
  art.height = logicalH;
  canvas.width = logicalW * K;
  canvas.height = logicalH * K;
  canvas.style.width = cssW + 'px';
  canvas.style.height = logicalH * U + 'px';

  fontName = clamp(Math.round(4.2 * U), 11, 16);
  fontSmall = clamp(Math.round(3.7 * U), 10, 14);
  fontBubble = clamp(Math.round(4 * U), 11, 15);
}

function planZones(teams, lw) {
  const avail = lw - 16;
  const maxCols = Math.max(1, Math.min(MAX_COLS, Math.floor((avail - ZONE_PAD * 2) / CELL_W)));

  const boxes = teams.map(([team, members]) => {
    const cols = Math.min(members.length, maxCols);
    const rows = Math.ceil(members.length / cols);
    return { team, members, cols, w: cols * CELL_W + ZONE_PAD * 2, h: ZONE_HEAD + rows * CELL_H + ZONE_PAD };
  });

  // 한 줄에 들어가는 만큼 팀 영역을 나란히 놓고, 줄마다 가운데 정렬
  const rows = [];
  let cur = [], curW = 0;
  for (const b of boxes) {
    const need = (cur.length ? ZONE_GAP : 0) + b.w;
    if (cur.length && curW + need > avail) { rows.push(cur); cur = []; curW = 0; }
    curW += (cur.length ? ZONE_GAP : 0) + b.w;
    cur.push(b);
  }
  if (cur.length) rows.push(cur);

  const zonesOut = [], desksOut = [];
  let y = WALL_H + ROW_GAP;
  for (const row of rows) {
    const rowW = row.reduce((sum, b) => sum + b.w, 0) + ZONE_GAP * (row.length - 1);
    let x = Math.max(8, Math.floor((lw - rowW) / 2));
    let rowH = 0;
    for (const b of row) {
      zonesOut.push({ team: b.team, x, y, w: b.w, h: b.h, count: b.members.length, color: RUGS[hash(b.team) % RUGS.length] });
      b.members.forEach((s, i) => {
        desksOut.push({ s, x: x + ZONE_PAD + (i % b.cols) * CELL_W, y: y + ZONE_HEAD + Math.floor(i / b.cols) * CELL_H });
      });
      x += b.w + ZONE_GAP;
      rowH = Math.max(rowH, b.h);
    }
    y += rowH + ROW_GAP;
  }
  return { zones: zonesOut, desks: desksOut, height: y };
}

// 배치는 직원 구성이 바뀔 때만 다시 계산하지만, 상태는 스냅샷마다 바뀌므로
// 책상이 붙들고 있는 세션 객체를 매번 최신 것으로 바꿔 끼운다.
function syncDesks() {
  const byId = new Map(data.sessions.map((s) => [s.id, s]));
  for (const d of desks) {
    const s = byId.get(d.s.id);
    if (s) d.s = s;
  }
}

const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));

// ───────────────────────────── 픽셀아트 기본 도구 ─────────────────────────────

function R(x, y, w, h, c) { g.fillStyle = c; g.fillRect(x, y, w, h); }
function P(x, y, c) { g.fillStyle = c; g.fillRect(x, y, 1, 1); }
const tick = (t, ms, n = 2) => ((t / ms) | 0) % n;

function line(x0, y0, x1, y1, c) {
  x0 |= 0; y0 |= 0; x1 |= 0; y1 |= 0;
  const dx = Math.abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
  const dy = -Math.abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
  let err = dx + dy;
  for (;;) {
    P(x0, y0, c);
    if (x0 === x1 && y0 === y1) break;
    const e2 = 2 * err;
    if (e2 >= dy) { err += dy; x0 += sx; }
    if (e2 <= dx) { err += dx; y0 += sy; }
  }
}

// ───────────────────────────── 방: 바닥·벽·창문·장식 ─────────────────────────────

function drawFloor() {
  R(0, WALL_H, logicalW, logicalH - WALL_H, PAL.floorA);
  for (let y = WALL_H, row = 0; y < logicalH; y += 8, row++) {
    if (row % 2) R(0, y + 1, logicalW, 7, PAL.floorB);
    R(0, y, logicalW, 1, PAL.floorLine);
    const off = (row * 23) % 40;
    for (let x = -off; x < logicalW; x += 40) R(x, y + 1, 1, 7, PAL.floorLine);
  }
}

function skyAt(h) {
  if (h >= 7 && h < 17) return { top: '#7fc0ec', bottom: '#c4e6f8', kind: 'day' };
  if (h >= 17 && h < 19.5) return { top: '#7a5e9c', bottom: '#f2a46a', kind: 'dusk' };
  if (h >= 5.5 && h < 7) return { top: '#9fc6e8', bottom: '#f5c79c', kind: 'dawn' };
  return { top: '#141a38', bottom: '#26305c', kind: 'night' };
}

function drawWall(t) {
  R(0, 0, logicalW, WALL_H, PAL.wall);
  for (let x = 0; x < logicalW; x += 10) R(x, 0, 1, WALL_H - 10, PAL.wallStripe);
  R(0, WALL_H - 10, logicalW, 8, PAL.wainscot);
  R(0, WALL_H - 10, logicalW, 1, PAL.wainscotHi);
  R(0, WALL_H - 2, logicalW, 2, PAL.base);

  // 회사 간판 — 폭은 사무실 이름에 맞추고, 시계·화분·창문은 그 오른쪽에 둔다
  const pw = plaqueWidth();
  R(6, 8, pw, 14, PAL.plaque);
  R(7, 9, pw - 2, 12, PAL.plaqueIn);
  P(9, 11, '#c9a46a'); P(pw + 2, 11, '#c9a46a'); P(9, 18, '#c9a46a'); P(pw + 2, 18, '#c9a46a');

  const d = new Date();
  drawClock(pw + 12, 8, d);

  // 창문(지금 시각에 맞춘 하늘). 오른쪽 위 상태 표시줄에 가리지 않는 곳까지만 낸다.
  const sky = skyAt(d.getHours() + d.getMinutes() / 60);
  const hudW = Math.ceil((hudEl.offsetWidth + 24) / U);
  const left = pw + 36, right = logicalW - hudW;
  const n = Math.max(0, Math.floor((right - left) / 72));
  for (let i = 0; i < n; i++) {
    const x = Math.round(left + ((right - left) / n) * (i + 0.5)) - 17;
    drawWindow(x, 6, sky, t, i);
  }

  drawPlant(pw + 30, WALL_H - 14);
  drawCooler(logicalW - 20, WALL_H - 26);
}

function drawWindow(x, y, sky, t, i) {
  R(x - 1, y - 1, 36, 26, PAL.frameDark);
  R(x, y, 34, 24, PAL.frame);
  R(x + 2, y + 2, 30, 10, sky.top);
  R(x + 2, y + 12, 30, 10, sky.bottom);

  g.save();
  g.beginPath(); g.rect(x + 2, y + 2, 30, 20); g.clip();
  if (sky.kind === 'night') {
    for (let k = 0; k < 5; k++) {
      if (tick(t, 700 + k * 90, 3) === 0) continue;   // 반짝반짝
      P(x + 3 + (hash('s' + i + k) % 28), y + 3 + (hash('t' + k + i) % 17), '#f4f1d0');
    }
    if (i === 0) { R(x + 23, y + 5, 4, 4, '#f4f1d0'); R(x + 25, y + 5, 2, 2, sky.top); }   // 초승달
  } else {
    if (i === 0 && sky.kind === 'day') R(x + 25, y + 5, 4, 4, '#fff1a8');
    const cx = Math.round(((t / 260 + i * 19) % 48) - 8);   // 흘러가는 구름
    R(x + 2 + cx, y + 7 + (i % 2) * 5, 8, 2, 'rgba(255,255,255,0.85)');
    R(x + 4 + cx, y + 6 + (i % 2) * 5, 4, 1, 'rgba(255,255,255,0.85)');
  }
  g.restore();

  R(x + 16, y + 2, 2, 20, PAL.frame);
  R(x + 2, y + 11, 30, 2, PAL.frame);
  R(x - 2, y + 24, 38, 2, PAL.sill);
}

function drawClock(x, y, d) {
  R(x, y, 15, 15, PAL.frameDark);
  R(x + 1, y + 1, 13, 13, '#f5f0e2');
  P(x + 7, y + 2, '#666'); P(x + 12, y + 7, '#666'); P(x + 7, y + 12, '#666'); P(x + 2, y + 7, '#666');
  const m = d.getMinutes(), h = (d.getHours() % 12) + m / 60;
  const cx = x + 7, cy = y + 7;
  const ma = (m / 60) * Math.PI * 2 - Math.PI / 2;
  const ha = (h / 12) * Math.PI * 2 - Math.PI / 2;
  line(cx, cy, cx + Math.round(Math.cos(ma) * 5), cy + Math.round(Math.sin(ma) * 5), '#333');
  line(cx, cy, cx + Math.round(Math.cos(ha) * 3), cy + Math.round(Math.sin(ha) * 3), '#b03a2e');
}

function drawCooler(x, y) {
  R(x + 1, y, 8, 9, '#8fc9ef');
  R(x + 2, y + 1, 2, 6, '#c4e6f8');
  R(x, y + 9, 10, 17, '#e8e8ee');
  R(x, y + 9, 10, 1, '#ffffff');
  R(x + 2, y + 13, 2, 2, '#4f86c6'); R(x + 6, y + 13, 2, 2, '#d9665b');
  R(x + 1, y + 25, 8, 1, '#b8b8c4');
}

function drawPlant(x, y) {
  R(x + 1, y + 8, 8, 7, '#b5653e');
  R(x, y + 8, 10, 2, '#c9774b');
  R(x + 2, y + 1, 6, 7, '#4f9a52');
  R(x, y + 3, 3, 4, '#5fae5f');
  R(x + 7, y + 2, 3, 4, '#5fae5f');
  P(x + 4, y, '#6cbf6a'); P(x + 5, y - 1, '#6cbf6a');
}

function drawZone(z) {
  const edge = shade(z.color, 0.78), dots = shade(z.color, 1.25);
  R(z.x, z.y, z.w, z.h, edge);
  R(z.x + 1, z.y + 1, z.w - 2, z.h - 2, z.color);
  for (let i = z.x + 3; i < z.x + z.w - 3; i += 4) { P(i, z.y + 3, dots); P(i, z.y + z.h - 4, dots); }
  for (let j = z.y + 3; j < z.y + z.h - 3; j += 4) { P(z.x + 3, j, dots); P(z.x + z.w - 4, j, dots); }
}

// ───────────────────────────── 책상 한 칸 ─────────────────────────────
// 한 칸(66×68) 안의 배치: 의자·사람(왼쪽, 오른쪽을 봄) → 책상 → 모니터 → 이름표(아래, 글자)

function drawDesk(d, t) {
  const s = d.s, a = actOf(s), L = looks(s.id);
  const x = d.x, y = d.y;

  R(x + 6, y + 51, 56, 2, 'rgba(0,0,0,0.18)');
  drawChair(x, y);
  if (a.pose !== 'away') drawPerson(x, y, L, a.pose, t);
  drawDeskBody(x, y);
  drawMonitor(x + 36, y + 18, a.screen, t, hash(s.id));
  R(x + 26, y + 34, 9, 1, PAL.keyboard);
  drawDeco(x, y, L.deco);

  const interns = s.interns || [];
  interns.slice(0, 3).forEach((it, i) => drawIntern(internX(d, i), d.y + 33, it, t, i));
}

const internX = (d, i) => d.x - 3 - i * 9;

function drawChair(x, y) {
  R(x + 7, y + 26, 4, 15, PAL.chair);
  R(x + 7, y + 26, 4, 1, PAL.chairHi);
  R(x + 8, y + 40, 15, 3, PAL.chair);
  R(x + 8, y + 40, 15, 1, PAL.chairHi);
  R(x + 14, y + 43, 2, 6, PAL.chairDark);
  R(x + 10, y + 49, 10, 1, PAL.chairDark);
  P(x + 10, y + 50, '#1f1f28'); P(x + 19, y + 50, '#1f1f28');
}

function drawDeskBody(x, y) {
  R(x + 22, y + 35, 38, 1, PAL.deskTopHi);
  R(x + 22, y + 36, 38, 3, PAL.deskTop);
  R(x + 24, y + 39, 34, 10, PAL.deskFront);
  R(x + 24, y + 39, 34, 1, PAL.deskShade);
  R(x + 44, y + 43, 6, 1, PAL.deskLeg);
  R(x + 25, y + 49, 2, 2, PAL.deskLeg);
  R(x + 55, y + 49, 2, 2, PAL.deskLeg);
}

function drawDeco(x, y, kind) {
  switch (kind) {
    case 0: R(x + 56, y + 31, 3, 4, '#ececec'); R(x + 56, y + 31, 3, 1, '#6b3e22'); P(x + 59, y + 32, '#ececec'); break;   // 머그
    case 1: R(x + 55, y + 31, 4, 4, '#b5653e'); R(x + 54, y + 27, 6, 4, '#4f9a52'); P(x + 56, y + 26, '#6cbf6a'); break;   // 화분
    case 2: R(x + 54, y + 32, 6, 3, '#c0504d'); R(x + 55, y + 30, 5, 2, '#4f81bd'); break;                                // 책
    default: R(x + 55, y + 29, 5, 6, '#8a6040'); R(x + 56, y + 30, 3, 4, '#a8d0f0'); break;                               // 액자
  }
}

function drawMonitor(mx, my, screen, t, seed) {
  R(mx, my, 18, 13, PAL.monitor);
  R(mx, my, 18, 1, PAL.monitorHi);
  R(mx + 8, my + 13, 2, 3, PAL.monitor);
  R(mx + 5, my + 16, 8, 1, PAL.monitor);
  const sx = mx + 2, sy = my + 2, sw = 14, sh = 9;

  switch (screen) {
    case 'off':
      R(sx, sy, sw, sh, '#14161f');
      break;
    case 'saver': {   // 화면 보호기: 떠다니는 별
      R(sx, sy, sw, sh, '#1c2440');
      P(sx + (((t / 260 + seed) % sw) | 0), sy + (((t / 410 + seed * 3) % sh) | 0), '#8fb0ff');
      break;
    }
    case 'cursor':
      R(sx, sy, sw, sh, '#1d2333');
      R(sx + 1, sy + 1, 6, 1, '#4a5578');
      if (tick(t, 500)) R(sx + 1, sy + 3, 1, 3, '#cfe3ff');
      break;
    case 'doc': {
      R(sx, sy, sw, sh, '#eef1f6');
      const off = tick(t, 600, 4);
      for (let i = 0; i < 4; i++) R(sx + 1, sy + 1 + i * 2, 6 + ((seed >> (i + off)) % 7), 1, '#9aa6b8');
      break;
    }
    case 'code': {
      R(sx, sy, sw, sh, '#1e2230');
      const n = tick(t, 260, 5) + 1;
      const colors = ['#ff8fb0', '#ffd27a', '#8fd0ff', '#a8e6a1'];
      for (let i = 0; i < Math.min(4, n); i++) R(sx + 1 + (i % 2) * 2, sy + 1 + i * 2, 4 + ((seed >> i) % 7), 1, colors[i % 4]);
      break;
    }
    case 'term': {
      R(sx, sy, sw, sh, '#0b0f0b');
      const off = tick(t, 160, 4);
      for (let i = 0; i < 4; i++) R(sx + 1, sy + 1 + ((i * 2 + off) % 8), 3 + ((seed >> (i + off)) % 9), 1, '#3cff7a');
      break;
    }
    case 'search': {
      R(sx, sy, sw, sh, '#eef1f6');
      R(sx, sy + 1 + tick(t, 350, 4) * 2, sw, 2, '#ffe28a');
      for (let i = 0; i < 4; i++) R(sx + 1, sy + 1 + i * 2, 5 + ((seed >> i) % 6), 1, '#9aa6b8');
      break;
    }
    case 'web':
      R(sx, sy, sw, sh, '#f5f7fb');
      R(sx, sy, sw, 2, '#4f86c6');
      R(sx + 1, sy + 3, 5, 4, '#c9d3e3');
      R(sx + 7, sy + 3, 6, 1, '#9aa6b8'); R(sx + 7, sy + 5, 5, 1, '#9aa6b8');
      break;
    case 'team': {
      R(sx, sy, sw, sh, '#2a2140');
      const b = tick(t, 300, 3);
      R(sx + 2, sy + 3 - (b === 0 ? 1 : 0), 2, 2, '#ffd27a');
      R(sx + 6, sy + 3 - (b === 1 ? 1 : 0), 2, 2, '#8fd0ff');
      R(sx + 10, sy + 3 - (b === 2 ? 1 : 0), 2, 2, '#ff8fb0');
      R(sx + 1, sy + 7, 12, 1, '#5a4c80');
      break;
    }
    case 'todo':
      R(sx, sy, sw, sh, '#f7f3e8');
      for (let i = 0; i < 3; i++) {
        R(sx + 1, sy + 1 + i * 3, 2, 2, i < tick(t, 700, 4) ? '#5fa37a' : '#c9c3b0');
        R(sx + 4, sy + 1 + i * 3 + 1, 7, 1, '#9aa6b8');
      }
      break;
    case 'alert':
      R(sx, sy, sw, sh, tick(t, 400) ? '#ff9f43' : '#ffcf6b');
      R(sx + 7, sy + 1, 1, 5, '#3a1c00');
      P(sx + 7, sy + 7, '#3a1c00');
      break;
  }
}

// 사람: 머리(8×8)·몸통(9×11)·다리, 오른쪽(모니터 쪽)을 보고 앉아 있다
function drawPerson(x, y, L, pose, t) {
  let hx = x + 12, hy = y + 20;
  let bx = x + 11, by = y + 28;
  if (pose === 'sleep') { hx = x + 18; hy = y + 27; bx = x + 12; by = y + 29; }
  if (pose === 'idle') { hx -= 1; bx -= 1; }
  if (pose === 'run') hx += 1;
  if (pose === 'think' && tick(t, 900)) hy -= 1;

  // 다리: 좌판 위 허벅지 → 정강이 → 신발
  R(x + 14, y + 38, 9, 3, L.pants);
  R(x + 20, y + 40, 3, 9, L.pants);
  R(x + 20, y + 49, 5, 2, PAL.shoe);

  // 몸통
  R(bx, by, 9, 11, L.shirt);
  R(bx, by, 9, 1, shade(L.shirt, 1.18));
  R(bx + 6, by + 1, 1, 9, shade(L.shirt, 0.82));

  drawHead(hx, hy, L, pose, t);
  drawArms(bx, by, L, pose, t);
}

function drawHead(hx, hy, L, pose, t) {
  R(hx, hy, 8, 8, L.skin);
  R(hx, hy - 1, 8, 3, L.hair);
  R(hx - 1, hy, 3, 6, L.hair);
  if (L.hairStyle === 1) R(hx + 5, hy + 2, 3, 1, L.hair);   // 앞머리
  if (L.hairStyle === 2) R(hx - 1, hy + 6, 2, 3, L.hair);   // 긴 머리

  const closed = pose === 'sleep' || (t + L.blink) % 3200 < 130;
  if (closed) R(hx + 5, hy + 4, 2, 1, '#3a2a22');
  else R(hx + 6, hy + 3, 1, 2, '#2a1d17');

  if (L.glasses) {
    R(hx + 5, hy + 2, 3, 1, '#2d2d38');
    R(hx + 5, hy + 5, 3, 1, '#2d2d38');
    P(hx + 5, hy + 3, '#2d2d38'); P(hx + 5, hy + 4, '#2d2d38');
  }
  P(hx + 6, hy + 6, pose === 'raise' ? '#b0473b' : shade(L.skin, 0.8));   // 입
}

function drawArms(bx, by, L, pose, t) {
  const sleeve = shade(L.shirt, 0.9), skin = L.skin;
  switch (pose) {
    case 'type':
    case 'run': {
      const b = tick(t, pose === 'run' ? 90 : 170);
      R(bx + 6, by + 3, 8, 2, sleeve);
      R(bx + 14, by + 3 + b, 2, 2, skin);   // 톡톡 치는 손
      break;
    }
    case 'read': {
      const b = tick(t, 800);
      R(bx + 6, by + 2, 4, 2, sleeve);
      R(bx + 9, by - 7 + b, 7, 9, '#f4f4ef');
      R(bx + 10, by - 6 + b, 5, 1, '#9aa6b8');
      R(bx + 10, by - 4 + b, 4, 1, '#9aa6b8');
      R(bx + 10, by - 2 + b, 5, 1, '#9aa6b8');
      R(bx + 9, by + 1, 2, 2, skin);
      break;
    }
    case 'think':
      R(bx + 6, by + 3, 2, 2, sleeve);
      R(bx + 7, by - 1, 2, 4, sleeve);
      R(bx + 7, by - 2, 2, 2, skin);   // 턱 괸 손
      break;
    case 'idle': {
      R(bx + 6, by + 4, 4, 2, sleeve);
      R(bx + 10, by + 1, 3, 4, '#f2f2f2');
      R(bx + 10, by + 1, 3, 1, '#6b3e22');
      P(bx + 13, by + 2, '#f2f2f2'); P(bx + 13, by + 3, '#f2f2f2');
      const ph = (t / 380) | 0;
      for (let i = 0; i < 2; i++) P(bx + 11 + ((ph + i) % 2), by - 1 - ((ph + i * 2) % 4), 'rgba(255,255,255,0.55)');   // 김
      break;
    }
    case 'raise': {
      const b = tick(t, 260);
      R(bx + 5 + b, by - 10, 2, 12, sleeve);
      R(bx + 5 + b, by - 12, 2, 2, skin);   // 흔드는 손
      break;
    }
    case 'sleep':
      R(bx + 5, by + 5, 10, 2, sleeve);
      break;
  }
}

// 인턴(서브에이전트): 책상 옆에 서서 클립보드를 든 작은 사람
function drawIntern(ix, iy, it, t, i) {
  const L = looks(it.id);
  const done = it.act === 'idle';
  const hop = done ? tick(t, 200 + i * 30) * 2 : tick(t, 380 + i * 40);
  const y = iy - hop;
  R(ix + 1, iy + 16, 8, 1, 'rgba(0,0,0,0.22)');
  R(ix + 2, y + 11, 2, 5, L.pants);
  R(ix + 5, y + 11, 2, 5, L.pants);
  R(ix + 1, y + 5, 7, 6, L.shirt);
  R(ix + 2, y, 6, 5, L.skin);
  R(ix + 2, y - 1, 6, 2, L.hair);
  R(ix + 1, y, 2, 3, L.hair);
  P(ix + 6, y + 2, '#2a1d17');
  R(ix + 7, y + 6, 3, 4, '#f4f4ef');
  P(ix + 8, y + 5, '#8a8a8a');
}

// ───────────────────────────── 글자 층(이름표·말풍선) ─────────────────────────────

function roundRect(x, y, w, h, r) {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}

function circle(x, y, r) {
  ctx.beginPath();
  ctx.arc(x, y, r, 0, Math.PI * 2);
  ctx.fill();
  ctx.stroke();
}

function ellipsize(text, maxW) {
  if (ctx.measureText(text).width <= maxW) return text;
  const chars = Array.from(text);   // 이모지가 반으로 잘리지 않도록 글자 단위로
  let lo = 0, hi = chars.length;
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if (ctx.measureText(chars.slice(0, mid).join('') + '…').width <= maxW) lo = mid;
    else hi = mid - 1;
  }
  return chars.slice(0, lo).join('') + '…';
}

const signFont = () => `800 ${clamp(Math.round(4.4 * U), 10, 17)}px ${FONT}`;
const PLAQUE_MIN = 60, PLAQUE_MAX = 130;   // 간판 폭(아트 픽셀). 기본 이름이 60에 딱 맞는다

/** 간판 폭(아트 픽셀): 이름 글자 폭 + 여백. 너무 길면 PLAQUE_MAX 에서 자르고 글자는 … 처리 */
function plaqueWidth() {
  ctx.font = signFont();
  return clamp(Math.ceil(ctx.measureText(officeSign()).width / U) + 14, PLAQUE_MIN, PLAQUE_MAX);
}

function drawOverlay(t) {
  // 간판 글자
  const pw = plaqueWidth();
  const sign = ellipsize(officeSign(), (pw - 10) * U);
  const cx = (6 + pw / 2) * U;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.font = signFont();
  ctx.fillStyle = 'rgba(0,0,0,0.4)';
  ctx.fillText(sign, cx + 1, 15 * U + 1);
  ctx.fillStyle = '#f6e3bd';
  ctx.fillText(sign, cx, 15 * U);

  for (const z of zones) drawZoneLabel(z);
  for (const d of desks) drawNameplate(d);
  for (const d of desks) if (d.s.id === selectedId || d.s.id === hoverId) drawSelection(d, d.s.id === selectedId);
  for (const d of desks) drawInternChips(d);
  for (const d of desks) drawDeskBubble(d, t);   // 말풍선은 맨 위에
}

function drawZoneLabel(z) {
  ctx.font = `700 ${fontSmall}px ${FONT}`;
  ctx.textAlign = 'left';
  ctx.textBaseline = 'middle';
  const text = `📁 ${z.team}  ·  ${z.count}명`;
  const h = clamp(Math.round((ZONE_HEAD - 6) * U), 16, 24);
  const w = Math.min(ctx.measureText(text).width + 16, z.w * U - 8);
  const x = (z.x + 3) * U, y = (z.y + 3) * U;
  ctx.fillStyle = 'rgba(15,12,28,0.55)';
  roundRect(x, y, w, h, h / 2);
  ctx.fill();
  ctx.fillStyle = '#f1ecff';
  ctx.fillText(ellipsize(text, w - 16), x + 8, y + h / 2 + 1);
}

function drawNameplate(d) {
  const s = d.s, a = actOf(s);
  const cx = (d.x + CELL_W / 2) * U;
  const y = (d.y + 53) * U;
  const maxW = CELL_W * U - 8;

  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  ctx.font = `700 ${fontName}px ${FONT}`;
  const name = ellipsize(displayName(s) + (noteOf(s.id).memo ? ' 📝' : ''), maxW);   // 📝 = 메모 있음
  ctx.fillStyle = 'rgba(0,0,0,0.6)';
  ctx.fillText(name, cx + 1, y + 1);
  ctx.fillStyle = s.act === 'away' ? '#8d87a6' : '#ffffff';
  ctx.fillText(name, cx, y);

  ctx.font = `500 ${fontSmall}px ${FONT}`;
  const dur = s.since ? ' · ' + fmtDur(now() - s.since) : '';
  const status = ellipsize(a.label + dur, maxW);
  const y2 = y + fontName + 3;
  ctx.fillStyle = 'rgba(0,0,0,0.6)';
  ctx.fillText(status, cx + 1, y2 + 1);
  ctx.fillStyle = a.tone;
  ctx.fillText(status, cx, y2);
}

function drawSelection(d, selected) {
  ctx.save();
  ctx.lineWidth = 2;
  ctx.strokeStyle = selected ? '#ffe28a' : 'rgba(255,255,255,0.35)';
  ctx.setLineDash(selected ? [7, 4] : []);
  roundRect(d.x * U + 2, d.y * U + 2, CELL_W * U - 4, (CELL_H - 2) * U, 8);
  ctx.stroke();
  ctx.restore();
}

function headAnchor(d, pose) {
  if (pose === 'sleep') return { x: d.x + 22, y: d.y + 26 };
  if (pose === 'raise') return { x: d.x + 16, y: d.y + 15 };
  if (pose === 'read') return { x: d.x + 18, y: d.y + 19 };
  return { x: d.x + 16, y: d.y + 19 };
}

function drawDeskBubble(d, t) {
  const s = d.s, a = actOf(s);
  if (a.bubble === 'none') return;
  const h = headAnchor(d, a.pose);
  const cx = h.x * U, ay = h.y * U;
  if (a.bubble === 'zzz') { drawZzz(cx, ay, t); return; }

  let text = `${s.icon} ${s.text}`;
  if (a.bubble === 'thought') text += '.'.repeat(1 + tick(t, 450, 3));
  drawBubble(cx, ay, text, BUBBLE[a.bubble] || BUBBLE.normal, t, CELL_W * U - 6);
}

function drawBubble(cx, anchorY, text, st, t, maxW) {
  ctx.font = `600 ${fontBubble}px ${FONT}`;
  ctx.textAlign = 'left';
  ctx.textBaseline = 'middle';
  const padX = 8, h = fontBubble + 11;
  const label = ellipsize(text, maxW - padX * 2);
  const w = Math.ceil(ctx.measureText(label).width) + padX * 2;
  const lift = st.pulse ? Math.round(Math.abs(Math.sin(t / 220)) * 3) : 0;
  const x = clamp(Math.round(cx - w / 2), 4, Math.max(4, cssW - w - 4));
  const y = Math.round(anchorY - h - 9 - lift);

  ctx.lineWidth = 2;
  ctx.strokeStyle = st.border;
  ctx.fillStyle = st.bg;
  roundRect(x, y, w, h, 7);
  ctx.fill();
  ctx.stroke();

  if (st.thought) {
    circle(cx - 2, y + h + 4, 3);
    circle(cx - 5, y + h + 9, 2);
  } else {
    ctx.beginPath();
    ctx.moveTo(cx - 5, y + h - 1); ctx.lineTo(cx - 1, y + h + 7); ctx.lineTo(cx + 4, y + h - 1);
    ctx.closePath();
    ctx.fill();
    ctx.beginPath();
    ctx.moveTo(cx - 5, y + h); ctx.lineTo(cx - 1, y + h + 7); ctx.lineTo(cx + 4, y + h);
    ctx.stroke();
  }

  ctx.fillStyle = st.fg;
  ctx.fillText(label, x + padX, y + h / 2 + 1);
}

function drawZzz(cx, y, t) {
  ctx.textAlign = 'left';
  ctx.textBaseline = 'alphabetic';
  for (let i = 0; i < 3; i++) {
    const p = (t / 1500 + i / 3) % 1;
    ctx.globalAlpha = 1 - p;
    ctx.fillStyle = '#ddd8f2';
    ctx.font = `800 ${fontSmall + i * 3}px ${FONT}`;
    ctx.fillText('z', cx + 4 + p * 10 + i * 5, y - 2 - p * 22 - i * 4);
  }
  ctx.globalAlpha = 1;
}

// 인턴 머리 위 작은 아이콘
function drawInternChips(d) {
  const interns = d.s.interns || [];
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.font = `${fontSmall}px ${FONT}`;
  interns.slice(0, 3).forEach((it, i) => {
    const cx = (internX(d, i) + 5) * U, cy = (d.y + 29) * U;
    const r = fontSmall * 0.75;
    ctx.fillStyle = it.act === 'idle' ? '#dff5d8' : '#ffffff';
    ctx.strokeStyle = '#2a2440';
    ctx.lineWidth = 1.5;
    circle(cx, cy, r);
    ctx.fillStyle = '#221d33';
    ctx.fillText(it.icon, cx, cy + 1);
  });
  if (interns.length > 3) {
    ctx.fillStyle = '#f1ecff';
    ctx.fillText(`+${interns.length - 3}`, (internX(d, 3) + 6) * U, (d.y + 40) * U);
  }
}

// ───────────────────────────── 그리기 루프 ─────────────────────────────

let lastFrame = 0;
function frame(ts) {
  requestAnimationFrame(frame);
  if (ts - lastFrame < 33) return;   // 약 30fps면 충분
  lastFrame = ts;

  ensureLayout();
  syncDesks();

  drawFloor();
  drawWall(ts);
  for (const z of zones) drawZone(z);
  for (const d of desks) drawDesk(d, ts);

  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.imageSmoothingEnabled = false;
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.drawImage(art, 0, 0, art.width * K, art.height * K);

  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  drawOverlay(ts);
}

// ───────────────────────────── 시간 표시 ─────────────────────────────

function fmtDur(ms) {
  if (!(ms > 0)) return '방금';
  const sec = Math.floor(ms / 1000);
  if (sec < 10) return '방금';
  if (sec < 60) return `${sec}초`;
  const m = Math.floor(sec / 60);
  if (m < 60) return `${m}분`;
  const h = Math.floor(m / 60);
  if (h >= 24) return `${Math.floor(h / 24)}일`;
  return m % 60 ? `${h}시간 ${m % 60}분` : `${h}시간`;
}

function fmtTime(ms) {
  const d = new Date(ms);
  return [d.getHours(), d.getMinutes(), d.getSeconds()].map((v) => String(v).padStart(2, '0')).join(':');
}

function entryName(e) {
  if (!e) return '-';
  if (e === 'claude-vscode') return 'VS Code';
  if (e === 'cli') return '터미널';
  if (e.includes('desktop')) return '데스크톱 앱';
  if (e.startsWith('sdk')) return 'SDK';
  return e;
}

// ───────────────────────────── HUD / 툴팁 / 상세 패널 ─────────────────────────────

const $ = (id) => document.getElementById(id);
const summaryEl = $('summary'), emptyEl = $('empty'), tooltipEl = $('tooltip'), panelEl = $('panel');
const btnAway = $('btnAway'), btnPin = $('btnPin'), btnHook = $('btnHook'), btnSettings = $('btnSettings');
const settingsEl = $('settings'), settingsBody = $('sBody'), drawerTab = $('drawerTab');

function refreshHud() {
  let busy = 0, wait = 0, rest = 0, away = 0;
  for (const s of data.sessions) {
    const a = actOf(s);
    if (s.act === 'away') away++;
    else if (a.wait) wait++;
    else if (a.busy) busy++;
    else rest++;
  }
  const parts = [`👥 ${data.sessions.length - away}명`];
  if (REMOTE) {
    parts.unshift(remoteLabel());   // 휴대폰 보기: PC와 이어져 있는지 맨 앞에
    document.body.classList.toggle('stale', remoteStale());   // PC가 꺼졌거나 소식이 없으면 사무실을 흐리게(멈춘 모습임을 알게)
  }
  if (busy) parts.push(`⚙️ 작업 ${busy}`);
  if (wait) parts.push(`✋ 확인 필요 ${wait}`);
  if (rest) parts.push(`☕ 휴식 ${rest}`);
  if (away) parts.push(`🚪 퇴근 ${away}`);
  // 숫자는 숨긴 직원까지 센다(숨긴 직원이 승인을 기다려도 ✋ 개수·창 제목으로는 알 수 있게)
  const shown = visibleSessions().length;
  const cut = data.sessions.filter((s) => showAway || s.act !== 'away').length - shown;
  if (cut > 0) parts.push(`🙈 숨김 ${cut}`);
  summaryEl.textContent = parts.join('  ·  ');
  // 좁은 화면에서는 상단 표시가 맨 위 막대가 된다(office.css) — 그 높이만큼 사무실을 내려 간판을 덮지 않게
  document.body.style.setProperty('--hud-h', innerWidth <= 600 ? `${hudEl.offsetHeight}px` : '0px');
  refreshEmpty(shown);
  document.title = wait ? `(✋${wait}) ${officeTitle()}` : officeTitle();   // 앱 창 제목·작업표시줄에도 반영됨
}

/** 책상이 하나도 없을 때 왜 비었는지 알려 준다. */
function refreshEmpty(shown) {
  emptyEl.hidden = shown > 0;
  if (shown > 0) return;
  const [big, msg, dim] =
    REMOTE && remoteError && data.sessions.length === 0 ? ['📱 PC와 아직 이어지지 않았어요', remoteError, '']
    : data.sessions.length === 0 ? ['🏢 아직 출근한 직원이 없어요', 'Claude Code에서 대화를 시작하면 책상에 직원이 나타납니다.', `최근 ${windowLabel()} 안에 대화한 세션을 봅니다(⚙️ 설정 → 표시 기간).`]
    : waitOnly ? ['🙌 기다리는 직원이 없어요', '모두 알아서 일하거나 쉬는 중입니다.', "⚙️ 설정 → 직원 표시 → '✋ 기다리는 직원만 보기'를 끄면 모두 보입니다."]
    : pickMode === 'manual' && pickedCount() === 0 ? ['🗂️ 보일 직원을 아직 고르지 않았어요', '⚙️ 설정 → 직원 표시에서 체크하면 책상에 앉습니다.', '고르는 방식: 직접 고르기']
    : ['🚪 모두 퇴근했어요', "⚙️ 설정에서 '퇴근한 직원 보이기'를 켜면 빈 책상이 보입니다.", ''];
  $('emptyBig').textContent = big;
  $('emptyMsg').textContent = msg;
  $('emptyDim').textContent = dim;
}

function refreshButtons() {
  btnAway.classList.toggle('off', !showAway);
  btnAway.title = showAway ? '퇴근한 직원 숨기기' : '퇴근한 직원 보이기';
  btnPin.classList.toggle('on', pinned);
  btnPin.title = pinned ? '항상 위에 표시 끄기' : '항상 위에 표시';
}

// HUD 버튼과 설정 패널이 같은 함수를 써서 어느 쪽에서 바꿔도 양쪽 표시가 맞는다
function setShowAway(v) {
  showAway = v;
  store.set('showAway', v);
  refreshButtons();
  refreshHud();
  refreshSettings();
}

function setPinned(v) {
  pinned = v;
  store.set('pinned', v);
  send({ cmd: 'topmost', value: v });
  refreshButtons();
  refreshSettings();
}

function setOfficeName(v) {
  officeName = v.trim();
  store.set('officeName', officeName);
  refreshHud();   // 창 제목
  shareToApp();
}

function setWindowHours(v) {
  windowHours = v;
  store.set('windowHours', v);
  send({ cmd: 'window', hours: v });   // 바뀐 기간은 다음 스냅샷부터 반영된다
  refreshHud();
  refreshSettings();
}

function setMaxShown(v) {
  maxShown = Math.min(SHOWN_MAX, Math.max(SHOWN_MIN, v));
  store.set('maxShown', maxShown);
  pickNoteUntil = 0;   // 예전 인원 기준 안내가 남지 않게
  refreshHud();
  refreshSettings();
}

function setPickMode(v) {
  // 처음 직접 고르기로 바꿀 때는 지금 보이는 직원을 체크해 둔다(빈 사무실에서 시작하지 않게)
  if (v === 'manual' && pickedCount() === 0) visibleSessions().forEach((s) => pickedIds.add(s.id));
  pickMode = v;
  store.set('pickMode', v);
  pickNoteUntil = 0;
  savePicked();
  refreshHud();
  refreshSettings();
}

const savePicked = () => store.set('picked', [...pickedIds].slice(-200));   // 지난 세션 ID가 끝없이 쌓이지 않게

let pickNoteUntil = 0;   // "최대 N명만" 안내를 보여 주는 동안(밀리초 시각)

function togglePick(id, checked) {
  if (checked && pickedCount() >= maxShown) {
    // 막고 안내만 한다. 체크 상태는 아래 refreshSettings 가 pickedIds 대로 되돌린다
    pickNoteUntil = Date.now() + 4000;
    setTimeout(refreshSettings, 4100);
  } else {
    pickNoteUntil = 0;   // 제대로 고쳤으면 막혔다는 안내는 바로 내린다
    if (checked) pickedIds.add(id);
    else pickedIds.delete(id);
    savePicked();
    refreshHud();
  }
  refreshSettings();
}

// 훅 연결·해제는 전역 설정을 바꾸는 일이라 앱(C#)이 확인 창을 띄운 뒤 처리하고, 결과는 다음 스냅샷(data.hooks)으로 온다
const toggleHooks = () => send({ cmd: 'hooks' });

btnAway.addEventListener('click', () => setShowAway(!showAway));
btnPin.addEventListener('click', () => setPinned(!pinned));
btnHook.addEventListener('click', toggleHooks);

function hookStatus() {
  const h = data.hooks;
  if (!h) return { text: '확인 중…', tone: 'var(--dim)' };
  if (h.state === 'on') return { text: '🔔 연결됨' + (h.last ? ` · 마지막 수신 ${fmtTime(h.last)}` : ' · 아직 받은 알림 없음'), tone: '#7dffa0' };
  if (h.state === 'error') return { text: '⚠️ ' + (h.error || '알림을 받지 못하는 중'), tone: '#ffb347' };
  return { text: '🔕 연결 안 됨 — 승인 대기를 추정만 합니다', tone: 'var(--dim)' };
}

function refreshHookButton() {
  const h = data.hooks;
  btnHook.hidden = !h || REMOTE;   // 훅 연결은 PC에서만
  if (btnHook.hidden) return;
  btnHook.classList.toggle('off', h.state === 'off');
  btnHook.classList.toggle('warn', h.state === 'error');
  btnHook.textContent = h.state === 'on' ? '🔔' : h.state === 'error' ? '⚠️' : '🔕';
  btnHook.title = hookStatus().text + (h.state === 'off' ? '\n클릭: Claude Code 설정에 훅 연결' : '\n클릭: 연결 해제');
}

// ───────────────────────────── 휴대폰 보기(앱 쪽 설정) ─────────────────────────────
// 앱(C#)이 {type:"cloud"} 로 상태를 알려 준다: configured, enabled, state(off·setup·starting·on·error), error, notice, last, url(QR 주소)

let cloud = null;
let cloudWizardOpen = false;   // 설정 도우미(단계별 안내 + 점검)
let cloudQrShown = false;      // QR에는 해독 키가 들어 있어 누를 때만 보인다
let cloudSetupError = '';
let cloudCheck = null;         // 마지막 점검 결과 {at, steps:{project,db,rules,auth,page}, rules, error}
let cloudCheckAskedAt = 0;

function onCloud(msg) {
  cloud = msg;
  refreshSettings();
}

// 앱 쪽 설정(app.json·시작 프로그램 등록·알림·새 버전) — 앱이 {type:"app", ...} 로 알려 준다
let appSettings = null;

function onApp(msg) {
  appSettings = msg;
  btnSettings.classList.toggle('dot', !!msg.update?.newer);   // 새 버전이 나오면 ⚙️ 에 점
  refreshSettings();
}

/** 앱이 저장한 뒤 실제 값을 {type:"app"} 로 다시 알려 준다(못 바꿨으면 스위치가 되돌아간다). */
const setAppSetting = (key, value) => send({ cmd: 'app-setting', key, value });
const appReady = () => !DEMO && !!appSettings;
const openUrl = (url) => send({ cmd: 'open-url', url });   // 앱이 바깥 브라우저로 연다(정해 둔 주소만)

function onCloudSetup(msg) {
  cloudSetupError = msg.error || '';
  refreshSettings();
}

function onCloudCheck(msg) {
  cloudCheck = msg;
  refreshSettings();
}

/** 설정 도우미 점검을 부탁한다(앱이 데이터베이스·규칙·로그인·페이지를 확인해 {type:"cloud-check"} 로 알려 줌). */
function requestCloudCheck(force) {
  if (DEMO || (!force && Date.now() - cloudCheckAskedAt < 4000)) return;
  cloudCheckAskedAt = Date.now();
  send({ cmd: 'cloud-check' });
}

const CHECK_KEYS = ['db', 'rules', 'auth', 'page'];
const cloudAllOk = () => CHECK_KEYS.every((k) => cloudCheck?.steps?.[k]?.s === 'ok');

// 도우미를 열어 둔 동안 5초마다 다시 확인 — 콘솔에서 한 단계를 마치고 돌아오면 저절로 ✓ 가 된다
setInterval(() => {
  if (cloudWizardOpen && !settingsEl.hidden && cloud?.hasProject && !cloudAllOk()) requestCloudCheck();
}, 5000);

function setCloudWizard(open) {
  cloudWizardOpen = open;
  cloudSetupError = '';
  if (open && cloud?.hasProject) requestCloudCheck(true);
  refreshSettings();
}

function cloudStatus() {
  const c = cloud;
  if (!c) return { text: '확인 중…', tone: 'var(--dim)' };
  if (!c.configured) return {
    text: c.hasProject ? '데이터베이스를 아직 찾지 못했어요 — 설정 도우미 3단계' : '설정 안 됨 — [설정 도우미]를 눌러 단계별로 따라 하세요(10분쯤)',
    tone: 'var(--dim)' };
  if (!c.enabled) return { text: '꺼짐', tone: 'var(--dim)' };
  if (c.state === 'error') return { text: '⚠️ ' + (c.error || '올리지 못하는 중'), tone: '#ffb347' };
  if (c.state === 'on') return { text: `📡 올리는 중 · 마지막 ${fmtTime(c.last)}` + (c.notice ? ` · ${c.notice}` : ''), tone: '#7dffa0' };
  return { text: '연결 중…', tone: 'var(--dim)' };
}

const DONE_CHOICES = [[5, '5분'], [10, '10분'], [30, '30분'], [60, '1시간']];
const doneLabel = () => (DONE_CHOICES.find(([m]) => m === appSettings?.notifyDoneMinutes) || [0, `${appSettings?.notifyDoneMinutes ?? 10}분`])[1];

function updateStatus() {
  const u = appSettings?.update;
  if (!u) return { text: '확인 중…', tone: 'var(--dim)' };
  if (u.newer) return { text: `🎉 새 버전 ${u.latest} 이 나왔어요`, tone: '#7dffa0' };
  if (u.error) return { text: `⚠️ ${u.error}`, tone: '#ffb347' };
  if (u.latest) return { text: `최신 버전입니다 · 확인 ${fmtTime(u.checkedAt)}`, tone: 'var(--dim)' };
  return { text: u.checkedAt ? '아직 공개된 버전이 없어요' : '아직 확인 전(켠 뒤 30초쯤)', tone: 'var(--dim)' };
}

// ── 휴대폰 보기 설정 도우미: 단계마다 할 일·Firebase 콘솔 바로가기·앱이 자동으로 확인한 결과 ──
const FB_CONSOLE = 'https://console.firebase.google.com';
const fbProject = () => (cloud?.project ? `${FB_CONSOLE}/project/${encodeURIComponent(cloud.project)}` : FB_CONSOLE);
const dbInstance = () => { try { return new URL(cloud.db).hostname.split('.')[0]; } catch { return null; } };

/** 단계 상태 {s: ok|fail|warn|wait, text}. 앱의 점검 결과(cloudCheck)에서 읽는다. */
function wizCheck(key) {
  if (!cloud?.hasProject) return { s: 'wait', text: '' };
  const r = cloudCheck?.steps?.[key];
  if (r) return { s: r.s === 'skip' ? 'wait' : r.s, text: r.text };
  return { s: 'wait', text: cloudCheck ? '' : '확인 중…' };   // 앞 단계에서 멈춰 아직 못 본 단계는 빈칸
}

async function copyRules(e) {
  const btn = e.currentTarget;
  if (!cloudCheck?.rules) { requestCloudCheck(true); return; }
  try {
    await navigator.clipboard.writeText(cloudCheck.rules);
    btn.textContent = '복사됨 ✓';
  } catch {
    btn.textContent = '아래 글을 직접 복사';
  }
  setTimeout(() => { btn.textContent = '규칙 복사'; }, 1800);
}

const WIZ = [
  { title: 'Firebase 프로젝트 만들기',
    desc: '구글 계정으로 Firebase 콘솔에 들어가 [프로젝트 만들기]. 이름은 아무거나(예: my-claude-office), Google 애널리틱스는 꺼도 됩니다. 요금제는 무료(Spark) 그대로 — 결제 정보는 필요 없습니다.',
    buttons: () => [['Firebase 콘솔 열기', () => openUrl(FB_CONSOLE + '/')]],
    state: () => (cloud?.hasProject ? { s: 'ok', text: '만들었어요' } : { s: 'wait', text: '' }) },
  { title: '웹 앱 등록 → 설정 붙여넣기', form: true,
    desc: '프로젝트 첫 화면에서 웹(</>) 버튼 → 앱 닉네임(아무거나) → [앱 등록] (Hosting 체크는 안 해도 됩니다). 나오는 firebaseConfig 블록을 통째로 복사해 아래에 붙여 넣고 [저장]. 이미 등록했다면 ⚙️ 프로젝트 설정 → 일반 → 내 앱에 있습니다.',
    buttons: () => [['프로젝트 설정 열기', () => openUrl(cloud?.project ? fbProject() + '/settings/general' : FB_CONSOLE + '/')]],
    state: () => (cloud?.hasProject ? { s: 'ok', text: cloud.project ? `프로젝트 ${cloud.project}` : '로컬 시험 서버' } : { s: 'wait', text: '' }) },
  { title: 'Realtime Database 만들기',
    desc: '왼쪽 메뉴의 Realtime Database(데이터베이스 또는 빌드 아래) → [데이터베이스 만들기]. 위치는 가까운 싱가포르(asia-southeast1), 보안 규칙은 "잠금 모드로 시작". 만들면 여기서 주소를 저절로 찾습니다.',
    buttons: () => [['Realtime Database 열기', () => openUrl(fbProject() + '/database')]],
    state: () => wizCheck('db') },
  { title: '보안 규칙 붙여넣기', rules: true,
    desc: 'Realtime Database 의 [규칙] 탭 내용을 모두 지우고 [규칙 복사]로 복사한 것을 붙여 넣은 뒤 [게시]. PC는 로그인한 자기 칸에만 쓰고, 휴대폰은 암호문만 읽게 하는 규칙입니다.',
    buttons: () => [['규칙 복사', copyRules], ['규칙 화면 열기', () => openUrl(dbInstance() ? `${fbProject()}/database/${dbInstance()}/rules` : fbProject() + '/database')]],
    state: () => wizCheck('rules') },
  { title: '익명 로그인 켜기',
    desc: '왼쪽 메뉴 Authentication(보안 또는 빌드 아래) → [시작하기] → 로그인 방법 → 익명 → 사용 설정 → [저장]. PC의 Claude Office만 이것으로 로그인합니다(휴대폰은 로그인 없음). "익명 계정 자동 정리"는 켜지 마세요 — 30일마다 QR을 다시 찍어야 합니다.',
    buttons: () => [['Authentication 열기', () => openUrl(fbProject() + '/authentication/providers')]],
    state: () => wizCheck('auth') },
  { title: '켜고 휴대폰으로 QR 찍기',
    desc: '[켜기] → 아래 [QR 보기] → 휴대폰 카메라로 찍으면 열립니다. 크롬 메뉴 → [앱 설치](아이폰은 공유 → 홈 화면에 추가)를 하면 주소창 없이 앱처럼 씁니다.',
    buttons: () => [[cloud?.enabled ? '켜져 있어요' : '켜기', () => send({ cmd: 'cloud-enable', value: true }), !cloud?.configured || !!cloud?.enabled]],
    state: () => {
      if (cloud?.enabled && cloud.state === 'on') return { s: 'ok', text: '올리는 중 — 아래 [QR 보기]' };
      if (cloud?.enabled && cloud.state === 'error') return { s: 'fail', text: cloud.error || '올리지 못하는 중' };
      const page = wizCheck('page');
      return page.s === 'fail' ? page : { s: 'wait', text: cloud?.configured ? '[켜기]를 눌러 주세요' : '' };
    } },
];

// ───────────────────────────── 설정 패널 ─────────────────────────────
// 항목을 늘릴 때는 SETTINGS 에 한 줄 추가하면 화면은 저절로 그려진다.
//   toggle : get() / set(v)                       — 켜고 끄는 스위치
//   number : get() / set(v) / min / max / unit    — − 값 + 조절
//   choice : get() / set(v) / options[[값, 글]]   — 여러 개 중 하나(이름 아래 한 줄로)
//   action : label() / run() / status()           — 버튼(앱이 확인 창을 띄우는 일 등)과 지금 상태 글
//   text   : get() / set(v) / placeholder / max   — 글 입력칸(치는 대로 저장)
//   picker :                                      — 직원 체크 목록(직접 고르기)
//   hours  : getStart/setStart/getEnd/setEnd      — 시작~끝 시각(방해 금지 시간대)
//   cloudwizard / cloudqr :                       — 휴대폰 보기 설정 도우미(WIZ 단계) / QR
//   공통   : name, desc(글 또는 함수), shown()이 false 면 숨김, enabled()가 false 면 잠금(toggle·action)
//            pcOnly: true — PC 앱에서만 바꿀 수 있는 것(휴대폰 보기에서는 빼고 그린다)
// 화면에서만 쓰는 값은 store(localStorage)에 두고, 앱(C#)이 알아야 하는 값은 host.postMessage 로 넘긴다.

const SETTINGS = [
  {
    title: '사무실',
    items: [
      { type: 'text', name: '사무실 이름', desc: '벽 간판과 창 제목(작업표시줄)에 씁니다. 비우면 기본 이름입니다.', pcOnly: true,
        placeholder: DEFAULT_OFFICE, max: 30, get: () => officeName, set: setOfficeName },
    ],
  },
  {
    title: '직원 표시',
    // 위에서 아래로 거르는 순서: 표시 기간 → 퇴근 숨김 → 고르는 방식 → 보일 인원
    items: [
      { type: 'choice', name: '표시 기간', pcOnly: true,
        desc: () => `마지막 대화가 ${windowLabel()} 안에 있는 세션만 사무실에 들입니다. 3시간 넘게 조용하면 퇴근(🚪)으로 보입니다.`,
        options: WINDOW_CHOICES, get: () => windowHours, set: setWindowHours },
      { type: 'toggle', name: '퇴근한 직원 보이기', desc: '퇴근(🚪)한 직원의 빈 책상도 사무실에 남겨 둡니다.',
        get: () => showAway, set: setShowAway },
      { type: 'toggle', name: '✋ 기다리는 직원만 보기', desc: '승인·질문을 기다리는 직원만 책상에 앉힙니다. 직원이 많을 때 휴대폰으로 볼 때 좋아요.',
        get: () => waitOnly, set: (v) => { waitOnly = v; store.set('waitOnly', v); layoutKey = ''; refreshHud(); refreshSettings(); } },
      { type: 'number', name: '보일 인원', desc: '책상에 앉힐 최대 인원입니다.',
        min: SHOWN_MIN, max: SHOWN_MAX, unit: '명', get: () => maxShown, set: setMaxShown },
      { type: 'choice', name: '고르는 방식',
        desc: () => (pickMode === 'manual'
          ? '체크한 직원만 앉힙니다. 새로 출근한 직원은 체크해야 보입니다.'
          : `최근에 일한 순서로 ${maxShown}명을 앉힙니다. 자리는 출근 순서대로 그대로입니다.`),
        options: [['recent', '최근 사용순'], ['manual', '직접 고르기']],
        get: () => pickMode, set: setPickMode },
      { type: 'picker', shown: () => pickMode === 'manual' },
    ],
  },
  {
    title: '창',
    items: [
      { type: 'toggle', name: '항상 위에 표시', desc: '다른 창에 가려지지 않게 맨 위에 띄웁니다.', pcOnly: true,
        get: () => pinned, set: setPinned },
      { type: 'toggle', name: '프로그램 시작 시 작업표시줄로 이동', pcOnly: true,
        desc: '켜 두면 다음 실행부터 창을 띄우지 않고 작업표시줄 오른쪽 알림 영역(🏢)에 숨긴 채로 켜집니다. 아이콘을 누르면 창이 열립니다. 감시와 휴대폰 보기는 그대로 돌아갑니다.',
        get: () => !!appSettings?.startMinimized,
        set: (v) => setAppSetting('startMinimized', v),
        enabled: () => !DEMO && !!appSettings },
      { type: 'toggle', name: '닫기(✕)를 눌러도 끄지 않고 숨기기', pcOnly: true,
        desc: '✕는 창만 숨기고 알림 영역(🏢)에서 계속 돌아갑니다(휴대폰 보기도 계속). 완전히 끝내려면 🏢 아이콘 오른쪽 클릭 → 끝내기.',
        get: () => !!appSettings?.closeToTray,
        set: (v) => setAppSetting('closeToTray', v),
        enabled: () => !DEMO && !!appSettings },
      { type: 'toggle', name: 'Windows 시작할 때 자동 실행', pcOnly: true,
        desc: () => 'PC에 로그인하면 알림 영역(🏢)에 숨긴 채로 자동으로 켜집니다(이 사용자 계정의 시작 프로그램에 등록).'
          + (appSettings?.autoStartElsewhere ? ' ⚠️ 지금은 다른 위치의 Claude Office가 등록돼 있어요 — 껐다 켜면 이 프로그램으로 바뀝니다.' : ''),
        get: () => !!appSettings?.autoStart,
        set: (v) => setAppSetting('autoStart', v),
        enabled: () => !DEMO && !!appSettings },
    ],
  },
  {
    title: '알림',
    // 창을 숨겨 둬도 알아야 하므로 앱(C#)이 판단해 Windows 알림으로 띄운다. 창을 보고 있을 때는 띄우지 않는다
    items: [
      { type: 'toggle', name: '✋ 기다릴 때 알림', pcOnly: true,
        desc: '직원이 승인·질문을 기다리기 시작하면 Windows 알림을 띄웁니다. 창을 보고 있을 때는 띄우지 않고, 알림을 누르면 그 직원이 열립니다.',
        get: () => !!appSettings?.notifyWaiting, set: (v) => setAppSetting('notifyWaiting', v), enabled: appReady },
      { type: 'toggle', name: '오래 걸린 일이 끝나면 알림', pcOnly: true,
        desc: () => `${doneLabel()} 넘게 일하던 직원이 일을 마치고 쉬기 시작하면 알립니다.`,
        get: () => !!appSettings?.notifyDone, set: (v) => setAppSetting('notifyDone', v), enabled: appReady },
      { type: 'choice', name: '오래 걸린 일 기준', pcOnly: true, shown: () => !!appSettings?.notifyDone,
        options: DONE_CHOICES, get: () => appSettings?.notifyDoneMinutes, set: (v) => setAppSetting('notifyDoneMinutes', v) },
      { type: 'toggle', name: '알림음', pcOnly: true, desc: '끄면 소리 없이 알림만 띄웁니다.',
        get: () => !!appSettings?.notifySound, set: (v) => setAppSetting('notifySound', v), enabled: appReady },
      { type: 'toggle', name: '방해 금지 시간대', pcOnly: true,
        desc: () => (appSettings?.quietEnabled
          ? `${appSettings.quietStart}시부터 ${appSettings.quietEnd}시까지는 알림을 띄우지 않습니다.`
          : '정한 시간에는 알림을 띄우지 않습니다(예: 밤).'),
        get: () => !!appSettings?.quietEnabled, set: (v) => setAppSetting('quietEnabled', v), enabled: appReady },
      { type: 'hours', pcOnly: true, shown: () => !!appSettings?.quietEnabled,
        getStart: () => appSettings?.quietStart ?? 22, setStart: (v) => setAppSetting('quietStart', v),
        getEnd: () => appSettings?.quietEnd ?? 8, setEnd: (v) => setAppSetting('quietEnd', v), enabled: appReady },
      { type: 'action', name: '알림 시험', pcOnly: true, desc: '지금 알림을 하나 띄워 봅니다(창을 보고 있어도).',
        label: () => '띄워 보기', run: () => send({ cmd: 'notify-test' }), enabled: () => appReady() && !!appSettings.tray },
    ],
  },
  {
    title: 'Claude Code 연동',
    items: [
      { type: 'action', name: '훅 연결', pcOnly: true,
        desc: '승인 창·입력 요청·세션 종료를 Claude Code 훅으로 받아 정확히 표시합니다. 연결·해제할 때 ~/.claude/settings.json 을 고치므로 확인 창이 먼저 뜹니다.',
        label: () => (data.hooks?.state === 'off' ? '연결하기' : '연결 해제'),
        enabled: () => !DEMO && !!data.hooks,   // 견본 화면에서는 바꿀 수 없다(앱이 --demo 면 훅 수신기도 안 켠다)
        run: toggleHooks, status: hookStatus },
    ],
  },
  {
    title: '휴대폰에서 보기',
    items: [
      { type: 'action', name: '휴대폰에서 보기', pcOnly: true,
        desc: '화면용 요약만 PC에서 암호화해 내 구글 Firebase(무료)에 올리고, 휴대폰은 QR로 열어 봅니다(보기 전용, 로그인 없음, 밖에서도). 대화 원본은 올리지 않습니다.',
        label: () => (!cloud?.configured ? (cloudWizardOpen ? '도우미 닫기' : '설정 도우미') : cloud.enabled ? '끄기' : '켜기'),
        enabled: () => !DEMO && !!cloud,   // 견본 화면에서는 쓸 수 없다
        run: () => {
          if (!cloud.configured) setCloudWizard(!cloudWizardOpen);
          else send({ cmd: 'cloud-enable', value: !cloud.enabled });
        },
        status: cloudStatus },
      { type: 'cloudwizard', pcOnly: true, shown: () => !DEMO && cloudWizardOpen },
      { type: 'cloudqr', pcOnly: true, shown: () => !DEMO && !!cloud?.configured },
      { type: 'choice', name: '휴대폰 페이지', pcOnly: true, shown: () => !DEMO && !!cloud?.configured && cloud.page !== 'local',
        desc: () => (cloud?.page === 'own'
          ? `내 Firebase Hosting(https://${cloud.project}.web.app)에 tools\\deploy-mobile.ps1 로 올려 둔 페이지를 씁니다.`
          : '모두가 같이 쓰는 페이지(GitHub)라 따로 올릴 것이 없습니다. 데이터는 내 Firebase에만, 해독 키는 휴대폰에만 있습니다.')
          + ' 바꾸면 휴대폰에서 QR을 다시 찍어야 합니다.',
        options: [['public', '공용 페이지'], ['own', '내 Firebase Hosting']],
        get: () => cloud?.page, set: (v) => { if (v !== cloud.page) send({ cmd: 'cloud-page', value: v }); } },
    ],
  },
  {
    title: '정보',
    items: [
      { type: 'action', name: '버전', pcOnly: true,
        desc: () => `지금 버전 ${appSettings?.version || '?'} — 새 버전은 GitHub 릴리스에서 받습니다.`,
        label: () => (appSettings?.update?.newer ? '받으러 가기' : '지금 확인'),
        run: () => (appSettings.update?.newer ? openUrl(appSettings.update.url) : send({ cmd: 'update-check' })),
        enabled: appReady, status: updateStatus },
      { type: 'toggle', name: '새 버전 알림', pcOnly: true,
        desc: '하루 두 번 GitHub에 새 버전이 나왔는지 물어보고, 나왔으면 한 번 알려 드립니다(보내는 것은 프로그램 버전뿐).',
        get: () => !!appSettings?.updateCheck, set: (v) => setAppSetting('updateCheck', v), enabled: appReady },
      { type: 'action', name: '데이터 폴더', pcOnly: true,
        desc: '설정과 별명·메모 사본(share.json), 휴대폰 보기 키(cloud.json)가 있는 곳입니다. 다른 PC로 옮길 때는 share.json 을 그 PC의 같은 폴더에 복사하세요. cloud.json 은 남에게 주지 마세요.',
        label: () => '열기', run: () => send({ cmd: 'open-data-dir' }), enabled: appReady },
    ],
  },
];

const elem = (tag, cls, text) => {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text) e.textContent = text;
  return e;
};

// 형식마다 조작부를 줄에 붙이고 "값을 다시 그리는 함수"를 돌려준다.
// 줄은 한 번만 만들고 이후엔 값만 고친다(스냅샷마다 다시 만들면 누르는 도중에 버튼이 바뀌어 클릭이 사라진다).
const RENDER = {
  toggle(it, row) {
    const sw = elem('button', 'switch');
    sw.setAttribute('role', 'switch');
    sw.setAttribute('aria-label', it.name);
    sw.addEventListener('click', () => it.set(!it.get()));
    row.append(sw);
    return (locked) => {
      sw.disabled = locked;
      sw.setAttribute('aria-checked', String(!!it.get()));
    };
  },

  action(it, row) {
    const b = elem('button', 'act');
    b.addEventListener('click', () => it.run());
    row.append(b);
    return (locked) => {
      b.disabled = locked;
      b.textContent = it.label();
    };
  },

  number(it, row) {
    const box = elem('div', 'stepper');
    const minus = elem('button', '', '−'), val = elem('span', 'val'), plus = elem('button', '', '+');
    minus.setAttribute('aria-label', `${it.name} 줄이기`);
    plus.setAttribute('aria-label', `${it.name} 늘리기`);
    minus.addEventListener('click', () => it.set(it.get() - 1));
    plus.addEventListener('click', () => it.set(it.get() + 1));
    box.append(minus, val, plus);
    row.append(box);
    return () => {
      const v = it.get();
      val.textContent = `${v}${it.unit || ''}`;
      minus.disabled = v <= it.min;
      plus.disabled = v >= it.max;
    };
  },

  choice(it, row) {
    row.classList.add('stack');
    const seg = elem('div', 'seg');
    const buttons = it.options.map(([value, label]) => {
      const b = elem('button', '', label);
      b.addEventListener('click', () => it.set(value));
      seg.append(b);
      return [value, b];
    });
    row.append(seg);
    return () => {
      const v = it.get();
      for (const [value, b] of buttons) b.setAttribute('aria-pressed', String(value === v));
    };
  },

  text(it, row) {
    row.classList.add('stack');
    const input = elem('input', 'field');
    input.type = 'text';
    input.maxLength = it.max || 40;
    input.placeholder = it.placeholder || '';
    input.spellcheck = false;
    input.addEventListener('input', () => it.set(input.value));
    row.append(input);
    return () => {
      if (document.activeElement !== input) input.value = it.get();   // 쓰는 도중엔 덮어쓰지 않는다
    };
  },

  hours(it, row) {
    const box = elem('div', 'hours');
    const pick = (set) => {
      const sel = elem('select', 'field');
      for (let h = 0; h < 24; h++) sel.append(new Option(`${h}시`, String(h)));
      sel.addEventListener('change', () => set(Number(sel.value)));
      return sel;
    };
    const from = pick(it.setStart), to = pick(it.setEnd);
    box.append(from, elem('span', '', '부터'), to, elem('span', '', '까지'));
    row.append(box);
    return (locked) => {
      from.disabled = to.disabled = locked;
      if (document.activeElement !== from) from.value = String(it.getStart());
      if (document.activeElement !== to) to.value = String(it.getEnd());
    };
  },

  // 휴대폰 보기 설정 도우미 — WIZ 단계. 지금 할 단계만 펼치고(마친 단계는 ✓, 머리를 누르면 펼침), 앱이 확인한 결과를 단계마다 보여 준다
  cloudwizard(it, row) {
    row.classList.add('stack');
    const list = elem('ol', 'wiz');
    const opened = new Set();   // 사용자가 펼쳐 본 단계
    const parts = WIZ.map((st, i) => {
      const li = elem('li', 'wstep'), head = elem('button', 'whead'), body = elem('div', 'wbody');
      const mark = elem('span', 'wmark', String(i + 1)), htxt = elem('span', 'wht'), sum = elem('span', 'wsum');
      htxt.append(elem('span', 'wtitle', st.title), sum);
      head.append(mark, htxt);
      head.addEventListener('click', () => { if (!opened.delete(i)) opened.add(i); refreshSettings(); });
      body.append(elem('div', 'desc', st.desc));
      const bar = elem('div', 'btnbar');   // 콘솔 바로가기 — 먼저 열고, 그다음 아래 칸에 붙여 넣는 순서
      body.append(bar);

      let err = null, rulesEl = null;
      if (st.form) {   // Firebase 웹 앱 설정(firebaseConfig) 붙여넣기
        const ta = elem('textarea', 'field');
        ta.rows = 6;
        ta.spellcheck = false;
        ta.placeholder = 'const firebaseConfig = {\n  apiKey: "…",\n  authDomain: "….firebaseapp.com",\n  projectId: "…",\n  …\n};';
        err = elem('div', 'pick-note');
        const save = elem('button', 'act', '저장');
        save.addEventListener('click', () => send({ cmd: 'cloud-setup', text: ta.value }));   // 결과는 {type:"cloud-setup", error}
        const saveBar = elem('div', 'btnbar');
        saveBar.append(save);
        body.append(ta, err, saveBar);
      }
      if (st.rules) {   // 붙여 넣을 규칙을 그대로 보여 준다(복사가 안 되면 직접 골라 복사)
        rulesEl = elem('pre', 'rules mono');
        body.append(rulesEl);
      }
      li.append(head, body);
      list.append(li);
      return { st, li, mark, sum, body, bar, err, rulesEl, barKey: '' };
    });

    const foot = elem('div', 'btnbar'), note = elem('div', 'desc');
    const recheck = elem('button', 'act', '다시 확인'), close = elem('button', 'act ghost', '도우미 닫기');
    recheck.addEventListener('click', () => requestCloudCheck(true));
    close.addEventListener('click', () => setCloudWizard(false));
    foot.append(recheck, close);
    row.append(elem('div', 'desc', '구글 Firebase(무료)에 내 칸을 만드는 과정입니다. 단계마다 [열기]로 Firebase 콘솔의 그 화면이 열리고, 마치면 여기서 저절로 ✓ 가 됩니다.'),
      list, foot, note);

    return () => {
      let current = -1;
      parts.forEach((p, i) => {
        const r = p.st.state();
        if (current < 0 && r.s !== 'ok') current = i;
        p.li.dataset.s = r.s;
        p.mark.textContent = r.s === 'ok' ? '✓' : r.s === 'fail' || r.s === 'warn' ? '!' : String(i + 1);
        p.sum.textContent = r.text || '';
        // 버튼은 글자·잠금이 바뀔 때만 새로 만든다(스냅샷마다 만들면 누르는 도중 사라진다)
        const btns = p.st.buttons();
        const key = btns.map(([label, , off]) => label + (off ? '·' : '')).join('|');
        if (key !== p.barKey) {
          p.barKey = key;
          p.bar.replaceChildren(...btns.map(([label, fn, off], j) => {
            const b = elem('button', j ? 'act ghost' : 'act', label);
            b.disabled = !!off;
            b.addEventListener('click', fn);
            return b;
          }));
        }
        if (p.err) {
          p.err.textContent = cloudSetupError;
          p.err.classList.toggle('blocked', !!cloudSetupError);
        }
        if (p.rulesEl) p.rulesEl.textContent = cloudCheck?.rules || '';
      });
      parts.forEach((p, i) => {
        p.li.classList.toggle('current', i === current);
        // 1·2단계(프로젝트 만들기·설정 붙여넣기)는 한 번에 이어서 하므로 함께 펼친다
        p.body.hidden = !(i === current || (current === 0 && i === 1) || opened.has(i) || p.li.dataset.s === 'warn');
      });
      note.textContent = cloudCheck?.error ? `⚠️ ${cloudCheck.error}`
        : current < 0 ? '🎉 모두 마쳤어요. 휴대폰으로 QR을 찍어 보세요.'
        : cloudCheck?.at ? `마지막 확인 ${fmtTime(cloudCheck.at)} — 도우미를 열어 두면 5초마다 다시 확인합니다.` : '';
    };
  },

  // 휴대폰 보기: QR(해독 키가 들어 있어 누를 때만 보인다)과 주소 복사·키 새로 만들기·설정 바꾸기
  cloudqr(it, row) {
    row.classList.add('stack');
    const bar = elem('div', 'btnbar');
    const showBtn = elem('button', 'act'), copyBtn = elem('button', 'act ghost', '주소 복사');
    const rekeyBtn = elem('button', 'act ghost', '키 새로 만들기'), editBtn = elem('button', 'act ghost', '점검·설정 도우미');
    bar.append(showBtn, copyBtn, rekeyBtn, editBtn);
    const box = elem('div', 'qrbox'), qrEl = elem('div', 'qr'), urlEl = elem('div', 'qrurl mono');
    box.append(qrEl, urlEl, elem('div', 'desc',
      '휴대폰 카메라로 찍어 여세요(홈 화면에 추가하면 앱처럼 쓸 수 있어요). 주소에 해독 키가 들어 있으니 남에게 보이지 않게 하고, 보였다면 "키 새로 만들기".'));
    row.append(bar, box);

    showBtn.addEventListener('click', () => { cloudQrShown = !cloudQrShown; refreshSettings(); });
    copyBtn.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(cloud.url);
        copyBtn.textContent = '복사됨';
        setTimeout(() => { copyBtn.textContent = '주소 복사'; }, 1500);
      } catch {
        cloudQrShown = true;   // 클립보드를 못 쓰면 주소를 보여 줘서 직접 복사하게
        refreshSettings();
      }
    });
    rekeyBtn.addEventListener('click', () => send({ cmd: 'cloud-rekey' }));   // 앱이 확인 창을 띄운다
    editBtn.addEventListener('click', () => setCloudWizard(!cloudWizardOpen));

    let drawnUrl = null;
    return () => {
      const url = cloud?.url;   // 첫 로그인(익명)이 끝나야 생긴다
      showBtn.textContent = cloudQrShown ? 'QR 숨기기' : 'QR 보기';
      showBtn.disabled = copyBtn.disabled = !url;
      box.hidden = !(cloudQrShown && url);
      if (!url || !cloudQrShown || url === drawnUrl) return;
      drawnUrl = url;
      urlEl.textContent = url;
      if (typeof qrcode === 'function') {   // vendor/qrcode.js
        const qr = qrcode(0, 'M');
        qr.addData(url);
        qr.make();
        qrEl.innerHTML = qr.createSvgTag({ cellSize: 4, margin: 4, scalable: true });
      }
    };
  },

  // 직원 체크 목록. 순서는 출근 순서(C#이 보낸 순서)라 이미 있는 줄은 움직이지 않는다 → 누르는 도중 자리가 바뀌지 않음
  picker(it, row) {
    row.classList.add('stack');
    const head = elem('div', 'pick-head'), list = elem('div', 'pick-list'), note = elem('div', 'pick-note');
    row.append(head, list, note);
    const lines = new Map();   // 세션 ID → 줄
    let flashedFor = 0;

    const makeLine = (id) => {
      const label = elem('label', 'pick'), cb = elem('input'), icon = elem('span', 'ic');
      const txt = elem('span', 'pt'), name = elem('span', 'nm'), meta = elem('span', 'mt');
      cb.type = 'checkbox';
      cb.addEventListener('change', () => togglePick(id, cb.checked));
      txt.append(name, meta);
      label.append(cb, icon, txt);
      return { label, cb, icon, name, meta };
    };

    return () => {
      const ids = new Set(data.sessions.map((s) => s.id));
      for (const [id, ln] of lines) if (!ids.has(id)) { ln.label.remove(); lines.delete(id); }
      data.sessions.forEach((s, i) => {
        let ln = lines.get(s.id);
        if (!ln) lines.set(s.id, (ln = makeLine(s.id)));
        if (list.children[i] !== ln.label) list.insertBefore(ln.label, list.children[i] || null);
        ln.cb.checked = pickedIds.has(s.id);
        ln.icon.textContent = s.icon;
        ln.name.textContent = displayName(s);
        const orig = noteOf(s.id).nick ? `${s.name} · ` : '';   // 별명이 있으면 원래 세션 이름도 같이
        ln.meta.textContent = `${orig}${s.team} · ${s.act === 'away' ? '퇴근' : fmtDur(now() - s.last) + ' 전'}`;
        ln.label.classList.toggle('away', s.act === 'away');
      });

      const count = pickedCount();
      head.textContent = data.sessions.length ? `선택 ${count} / 최대 ${maxShown}명` : '아직 고를 직원이 없어요';
      head.classList.toggle('full', count >= maxShown);

      // 방금 넘치게 고르려 했으면 그 안내를, 아니면 보일 인원보다 많이 체크돼 있을 때(인원을 줄인 경우) 그 안내를
      const blocked = Date.now() < pickNoteUntil;
      note.textContent = blocked ? `최대 ${maxShown}명만 선택할 수 있습니다.`
        : count > maxShown ? `${count}명이 체크돼 있어 최근에 일한 ${maxShown}명만 보입니다. 보일 인원을 늘리거나 체크를 줄여 주세요.`
        : '';
      note.classList.toggle('blocked', blocked);
      if (blocked && flashedFor !== pickNoteUntil) {   // 거듭 눌러도 눈에 띄게 다시 흔든다
        flashedFor = pickNoteUntil;
        note.classList.remove('flash');
        void note.offsetWidth;
        note.classList.add('flash');
      }
    };
  },
};

const settingRows = [];

function buildSettings() {
  for (const sec of SETTINGS) {
    const items = sec.items.filter((it) => !(REMOTE && it.pcOnly));   // 휴대폰 보기에서는 PC 전용 항목을 뺀다
    if (!items.length) continue;
    settingsBody.append(elem('h3', '', sec.title));
    for (const it of items) {
      const row = elem('div', 'setting');
      let desc = null, status = null;
      if (it.name) {
        const txt = elem('div', 'txt');
        desc = elem('div', 'desc');
        txt.append(elem('div', 'name', it.name), desc);
        if (it.status) status = txt.appendChild(elem('div', 'status'));
        row.append(txt);
      }
      const refresh = RENDER[it.type](it, row);
      settingsBody.append(row);
      settingRows.push({ it, row, desc, status, refresh });
    }
  }
}

function refreshSettings() {
  if (settingsEl.hidden) return;
  for (const { it, row, desc, status, refresh } of settingRows) {
    row.hidden = it.shown ? !it.shown() : false;
    if (row.hidden) continue;
    if (desc) desc.textContent = typeof it.desc === 'function' ? it.desc() : it.desc;
    if (status) {
      const st = it.status();
      status.textContent = st.text;
      status.style.color = st.tone;
    }
    refresh(it.enabled ? !it.enabled() : false);
  }
}

function setSettingsOpen(open) {
  settingsEl.hidden = !open;
  btnSettings.classList.toggle('on', open);
  if (open) drawerFolded = false;
  if (open && selectedId) { selectedId = null; renderPanel(); }   // 서랍은 한 번에 하나만
  syncDrawers();
  refreshSettings();
}

// ───────────────────────────── 오른쪽 서랍(상세 패널·설정) ─────────────────────────────
// 한 번에 하나만 열린다. 펼치면 사무실을 서랍 자리를 뺀 폭에 맞춰 다시 배치해 직원을 가리지 않고,
// 접으면 오른쪽 끝에 손잡이만 남기고 사무실이 창 전체를 쓴다. ✕ 는 완전히 닫기.

const DRAWER_W = 330;        // office.css 의 --drawer-w 와 같게
const DRAWER_MIN_VIEW = 360; // 창이 너무 좁아 이만큼도 안 남으면 비켜 주지 않고 예전처럼 서랍이 위에 덮는다
let drawerFolded = false;

const drawerOpen = () => !panelEl.hidden || !settingsEl.hidden;

/** 사무실을 그릴 수 있는 폭(CSS 픽셀). 펼친 서랍 자리는 뺀다. */
function viewW() {
  const reserve = drawerOpen() && !drawerFolded && innerWidth - DRAWER_W >= DRAWER_MIN_VIEW;
  return innerWidth - (reserve ? DRAWER_W : 0);
}

function setDrawerFolded(v) {
  drawerFolded = v;
  if (v && document.activeElement?.closest('.drawer')) document.activeElement.blur();   // 접힌 칸에 글자가 들어가지 않게
  syncDrawers();
}

/** 서랍 상태를 화면에 반영: HUD 비키기(panel-open), 접힘(drawer-folded), 손잡이 글자. */
function syncDrawers() {
  const open = drawerOpen();
  if (!open) drawerFolded = false;   // 닫았다가 다시 열면 펼친 채로
  document.body.classList.toggle('panel-open', open && !drawerFolded);
  document.body.classList.toggle('drawer-folded', open && drawerFolded);
  drawerTab.hidden = !open;
  if (!open) return;
  const s = !panelEl.hidden ? data.sessions.find((x) => x.id === selectedId) : null;
  drawerTab.querySelector('.arrow').textContent = drawerFolded ? '‹' : '›';
  drawerTab.querySelector('.lbl').textContent = drawerFolded ? (s ? displayName(s) : '설정') : '';
  drawerTab.title = drawerFolded ? '펼치기' : '접기 — 사무실을 넓게 보기';
}

drawerTab.addEventListener('click', () => setDrawerFolded(!drawerFolded));

btnSettings.addEventListener('click', () => {
  if (!settingsEl.hidden && drawerFolded) setDrawerFolded(false);   // 접혀 있으면 펼치기
  else setSettingsOpen(settingsEl.hidden);
});
$('sClose').addEventListener('click', () => setSettingsOpen(false));

function hitTest(px, py) {
  const lx = px / U, ly = py / U;
  return desks.find((d) => lx >= d.x && lx < d.x + CELL_W && ly >= d.y && ly < d.y + CELL_H) || null;
}

canvas.addEventListener('mousemove', (e) => {
  const hit = hitTest(e.offsetX, e.offsetY);
  hoverId = hit ? hit.s.id : null;
  canvas.style.cursor = hit ? 'pointer' : 'default';
  if (!hit) { tooltipEl.hidden = true; return; }
  const s = hit.s, a = actOf(s);
  tooltipEl.replaceChildren();
  const add = (tag, text, cls) => { const el = document.createElement(tag); el.textContent = text; if (cls) el.className = cls; tooltipEl.append(el); };
  const clip = (t, n) => (t.length > n ? t.slice(0, n - 1) + '…' : t);
  const note = noteOf(s.id);
  add('b', displayName(s));
  if (note.nick) add('div', `세션 이름: ${s.name}`, 'dim');
  add('div', `${s.icon} ${a.label} — ${s.text}`);
  if (note.memo) add('div', '📝 ' + clip(note.memo.replace(/\s+/g, ' ').trim(), 90));
  if (s.prompt) add('div', '📨 ' + clip(s.prompt, 90), 'dim');
  (s.interns || []).forEach((it) => add('div', `${it.icon} ${it.name}: ${it.text}`, 'dim'));   // 이름이 이미 "탐색 인턴" 꼴
  add('div', '클릭하면 자세히 보기', 'dim');
  tooltipEl.hidden = false;
  const r = tooltipEl.getBoundingClientRect();
  tooltipEl.style.left = clamp(e.clientX + 14, 4, viewW() - r.width - 4) + 'px';
  tooltipEl.style.top = clamp(e.clientY + 14, 4, innerHeight - r.height - 4) + 'px';
});

canvas.addEventListener('mouseleave', () => { hoverId = null; tooltipEl.hidden = true; });

canvas.addEventListener('click', (e) => {
  const hit = hitTest(e.offsetX, e.offsetY);
  if (hit && hit.s.id === selectedId && !panelEl.hidden) {   // 같은 직원을 다시 누르면 접기·펼치기
    setDrawerFolded(!drawerFolded);
    return;
  }
  selectedId = hit ? hit.s.id : null;
  if (hit) drawerFolded = false;                    // 다른 직원을 누르면 펼쳐서 보여 준다
  if (!settingsEl.hidden) setSettingsOpen(false);   // 사무실을 누르면 설정은 닫힌다
  renderPanel();
});

$('pClose').addEventListener('click', () => { selectedId = null; renderPanel(); });
// 작업 폴더를 탐색기로 — 경로는 앱이 자기 스냅샷에서 찾는다(화면이 보낸 경로를 그대로 열지 않음)
$('pOpenDir').addEventListener('click', () => { if (selectedId) send({ cmd: 'open-folder', id: selectedId }); });
document.addEventListener('keydown', (e) => {
  if (e.key !== 'Escape') return;
  if (e.target instanceof Element && e.target.matches('input, textarea')) { e.target.blur(); return; }   // 쓰던 칸에서는 Esc가 칸만 빠져나온다
  selectedId = null;
  renderPanel();
  setSettingsOpen(false);
});

// 상세 패널의 별명·메모 칸. 글자를 칠 때마다 바로 저장한다(따로 저장 버튼 없음)
const nickEl = $('pNick'), memoEl = $('pMemo');
let noteFor = null;   // 입력칸이 지금 어느 세션 것인지

nickEl.addEventListener('input', () => {
  if (!noteFor) return;
  setNote(noteFor, { nick: nickEl.value.trim() });
  renderPanel();
  refreshSettings();   // 체크 목록의 이름도
});
memoEl.addEventListener('input', () => {
  if (noteFor) setNote(noteFor, { memo: memoEl.value });
});

function listItem(icon, text, time) {
  const li = document.createElement('li');
  if (time) { const t = document.createElement('span'); t.className = 't'; t.textContent = time; li.append(t); }
  const x = document.createElement('span');
  x.className = 'x';
  x.textContent = `${icon} ${text}`;
  li.append(x);
  return li;
}

function renderPanel() {
  const s = selectedId ? data.sessions.find((x) => x.id === selectedId) : null;
  panelEl.hidden = !s;
  syncDrawers();
  if (!s) { noteFor = null; return; }
  const a = actOf(s);
  const note = noteOf(s.id);
  $('pName').textContent = displayName(s);
  // 별명을 붙였으면 원래 세션 이름(/rename 이름·자동 제목)을 아래에 남겨 둔다
  $('pTitle').textContent = note.nick ? `세션 이름: ${s.name}${s.title ? ' · ' + s.title : ''}` : (s.title || '');
  // 입력칸은 다른 직원으로 바꿨을 때만 채운다(스냅샷마다 채우면 쓰는 도중 글자가 되돌아간다)
  if (noteFor !== s.id) {
    noteFor = s.id;
    nickEl.value = note.nick || '';
    nickEl.placeholder = `별명 (비우면 "${s.name}")`;
    memoEl.value = note.memo || '';
  }
  const st = $('pState');
  st.textContent = `${s.icon} ${a.label} — ${s.text}`;
  st.style.color = a.tone;
  $('pCwd').textContent = s.cwd || s.team;
  $('pOpenDir').hidden = DEMO || !s.cwd;
  $('pEntry').textContent = entryName(s.entry);
  $('pModel').textContent = s.model || '-';
  $('pBranch').textContent = s.branch || '-';
  $('pLast').textContent = s.last ? `${fmtTime(s.last)} (${fmtDur(now() - s.last)} 전)` : '-';
  $('pPrompt').textContent = s.prompt || '(없음)';
  const interns = s.interns || [];
  $('pInternsWrap').hidden = interns.length === 0;
  $('pInterns').replaceChildren(...interns.map((it) => listItem(it.icon, `${it.name}${it.desc ? ' · ' + it.desc : ''} — ${it.text}`)));
  $('pLog').replaceChildren(...(s.recent || []).map((r) => listItem(r.icon, r.text, fmtTime(r.t))));
  $('pId').textContent = `세션 ID ${s.id}`;
}

// ───────────────────────────── 휴대폰 보기(원격) ─────────────────────────────
// PC가 Firebase Realtime Database 의 offices/<u> 에 올린 { v, z, t, iv, ct } 를 실시간 스트림(EventSource)으로 받아
// 이 기기 안에서 해독(WebCrypto AES-GCM) → 압축 풀기(gzip) → 사무실 화면에 그린다. 키는 주소의 # 뒤에만 있다.

let remoteAt = 0;        // PC가 마지막으로 올린 시각(ms) — 1분마다 생존 신호가 온다
let remoteError = '';
let remoteNode = null;   // 데이터베이스에서 받은 현재 값

const b64ToBytes = (s) => {
  s = s.replace(/-/g, '+').replace(/_/g, '/');
  while (s.length % 4) s += '=';
  return Uint8Array.from(atob(s), (c) => c.charCodeAt(0));
};

async function gunzip(bytes) {
  const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'));
  return new Uint8Array(await new Response(stream).arrayBuffer());
}

/** 휴대폰 보기 상태(상단 요약 맨 앞에 붙는다). */
/** PC 프로그램을 정상적으로 끝냈을 때 남긴 시각(off). 그 뒤로 새로 올라오면(PUT) 사라진다. */
const remoteOffAt = () => (remoteNode && remoteNode.off && remoteNode.off >= (remoteNode.t || 0) ? remoteNode.off : 0);

/** 휴대폰에 보이는 사무실이 지난 모습인지(PC 꺼짐·응답 없음) — 흐리게 그린다. */
const remoteStale = () => !!remoteAt && (!!remoteOffAt() || Date.now() - remoteAt >= 90000);

function remoteLabel() {
  if (remoteError) return `⚠️ ${remoteError}`;
  if (!remoteAt) return '📡 PC 연결 중…';
  const off = remoteOffAt();
  if (off) return `💤 PC에서 꺼짐 · ${fmtTime(off)} (${fmtDur(Date.now() - off)} 전)`;
  const age = Date.now() - remoteAt;
  return age < 90000 ? '📡 실시간' : `📡 PC 응답 없음 · ${fmtDur(age)} 전`;   // 생존 신호가 1분마다라 1분 반 넘게 없으면 끊긴 것
}

async function startRemote() {
  if (!remoteLink) {
    remoteError = '이 기기에 연결 정보가 없어요 — PC의 Claude Office ⚙️ → 휴대폰에서 보기 → QR 보기를 찍어 주세요';
    refreshHud();
    return;
  }
  let key;
  try {
    key = await crypto.subtle.importKey('raw', b64ToBytes(remoteLink.k), 'AES-GCM', false, ['decrypt']);
  } catch {
    remoteError = '주소의 키가 잘못됐어요 — PC에서 QR을 다시 찍어 주세요';
    refreshHud();
    return;
  }
  const url = `${remoteLink.db.replace(/\/+$/, '')}/offices/${encodeURIComponent(remoteLink.u)}.json`;
  const es = new EventSource(url);   // Firebase REST 스트리밍: put·patch·keep-alive·cancel 이벤트

  const apply = async (type, raw) => {
    const { path, data: value } = JSON.parse(raw);
    // 우리는 늘 통째로 올리지만(put "/") 혹시 일부만 오면(하위 경로·patch) 합쳐서 쓴다
    if (path === '/' || path === '') remoteNode = type === 'patch' ? { ...(remoteNode || {}), ...value } : value;
    else remoteNode = { ...(remoteNode || {}), [path.replace(/^\//, '').split('/')[0]]: value };
    if (!remoteNode || !remoteNode.ct) {
      remoteError = 'PC가 아직 아무것도 올리지 않았어요 — PC에서 휴대폰 보기를 켜 주세요';
      refreshHud();
      return;
    }
    try {
      const p = remoteNode;
      const plain = new Uint8Array(await crypto.subtle.decrypt({ name: 'AES-GCM', iv: b64ToBytes(p.iv) }, key, b64ToBytes(p.ct)));
      const obj = JSON.parse(new TextDecoder().decode(p.z ? await gunzip(plain) : plain));
      remoteShare = obj.share || {};
      officeName = String(remoteShare.office || '').trim();
      remoteAt = p.t || Date.now();
      remoteError = '';
      onSnapshot(obj.snap);
    } catch {
      remoteError = '해독하지 못했어요 — PC에서 키를 새로 만들었다면 QR을 다시 찍어 주세요';
      refreshHud();
    }
  };

  es.addEventListener('put', (e) => apply('put', e.data));
  es.addEventListener('patch', (e) => apply('patch', e.data));
  es.addEventListener('cancel', () => { remoteError = '읽을 권한이 없어요 — 데이터베이스 규칙을 확인해 주세요'; refreshHud(); });
  es.addEventListener('error', () => { if (!remoteError) { remoteError = '연결이 끊겨 다시 잇는 중…'; refreshHud(); } });   // EventSource 가 알아서 다시 잇는다
  es.addEventListener('open', () => { if (remoteError.startsWith('연결이 끊겨')) { remoteError = ''; refreshHud(); } });
  setInterval(refreshHud, 5000);   // "N분 전"·끊김 표시를 갱신
}

// ───────────────────────────── 견본(데모) ─────────────────────────────

function startDemo() {
  const base = Date.now();
  const people = [
    // 견본은 가상의 회사·프로젝트(공개 스크린샷에도 쓰인다)
    { id: 'demo-a1', name: '장바구니 개선', team: 'acme/web-shop', title: '장바구니 수량 버그 수정' },
    { id: 'demo-a2', name: '디자인 정리', team: 'acme/web-shop', title: '버튼 색상 통일' },
    { id: 'demo-b1', name: '결제 연동', team: 'acme/api-server', title: '결제 응답 재시도 처리' },
    { id: 'demo-b2', name: '로그 점검', team: 'acme/api-server', title: '오류 로그 분석' },
    { id: 'demo-b3', name: '아주아주긴세션이름이라서잘려야하는세션', team: 'acme/api-server' },
    { id: 'demo-c1', name: '사무실 만들기', team: 'toy/pixel-office', title: 'Claude Pixel Office' },
    { id: 'demo-c2', name: '어제 작업', team: 'toy/pixel-office' },
  ];
  const script = [
    ['thinking', '💭', '생각 중'],
    ['reading', '📄', 'CartService.cs 읽는 중'],
    ['writing', '✏️', 'PaymentApi.cs 수정 중'],
    ['running', '⌨️', '빌드하는 중'],
    ['searching', '🔍', "'ProcessOrder' 찾는 중"],
    ['browsing', '🌐', "'재시도 정책' 검색 중"],
    ['delegating', '👥', '인턴에게: 결제 모듈 구조 분석'],
    ['waiting', '✋', '승인해 주세요 · 빌드하는 중'],
    ['talking', '💬', '답변 쓰는 중'],
    ['asking', '✋', '질문에 답해주세요!'],
    ['planning', '📋', '할 일 정리 중'],
    ['idle', '☕', '대기 중'],
    ['sleeping', '💤', 'zzz'],
  ];

  function snapshot() {
    const t = Date.now();
    const step = Math.floor((t - base) / 4000);
    return {
      now: t,
      hooks: { state: 'on', last: t - 30000 },
      sessions: people.map((p, i) => {
        const [act, icon, text] = p.id === 'demo-c2' ? ['away', '🚪', '퇴근'] : script[(step + i * 2) % script.length];
        const delegating = act === 'delegating';
        return {
          ...p,
          cwd: 'C:\\Projects\\' + p.team.replace('/', '\\'),
          entry: i % 2 ? 'cli' : 'claude-vscode',
          branch: 'main',
          model: 'claude-opus-5-5',
          act, icon, text,
          since: t - ((t - base) % 4000) - i * 47000,
          last: t - 1500,
          started: base - (people.length - i) * 600000,
          prompt: '결제 실패 시 재시도 로직을 점검하고, 실패한 요청은 로그에 남겨줘',
          recent: [
            { t: t - 4000, icon, text },
            { t: t - 9000, icon: '📄', text: 'Program.cs 읽는 중' },
            { t: t - 15000, icon: '📨', text: '지시: 로그 확인해줘' },
          ],
          interns: delegating ? [
            { id: p.id + '-i1', name: '일반 인턴', desc: '결제 모듈 구조 분석', act: 'reading', icon: '📄', text: 'PaymentClient.cs 읽는 중', since: t - 3000 },
            { id: p.id + '-i2', name: '탐색 인턴', desc: '호출부 찾기', act: step % 2 ? 'idle' : 'searching', icon: step % 2 ? '✅' : '🔍', text: step % 2 ? '다 했어요!' : "'ProcessOrderAsync' 찾는 중", since: t - 2000 },
          ] : [],
        };
      }),
    };
  }

  onSnapshot(snapshot());
  setInterval(() => onSnapshot(snapshot()), 1000);
}

// ───────────────────────────── 시작 ─────────────────────────────

buildSettings();
refreshButtons();
refreshHud();
if (selectedId === 'settings') setSettingsOpen(true);
window.addEventListener('resize', () => { layoutKey = ''; });

if (REMOTE) {
  // 휴대폰 보기: PC 전용 조작(항상 위·훅)은 숨기고, 별명·메모는 PC에서 정한 것을 보여 주기만 한다
  document.body.classList.add('remote');
  btnPin.hidden = true;
  nickEl.readOnly = memoEl.readOnly = true;
  nickEl.placeholder = memoEl.placeholder = '별명·메모는 PC에서 정합니다';
  startRemote();
  // 설치형 앱(주소창 없이 열기)에 필요. 끊겼을 때 마지막 화면 파일을 보여 주는 정도만 한다(sw.js)
  if ('serviceWorker' in navigator && window.isSecureContext && !host) navigator.serviceWorker.register('sw.js').catch(() => {});
} else if (DEMO) {
  startDemo();
} else {
  host.addEventListener('message', (e) => {
    const m = e.data;
    if (m?.type === 'cloud') onCloud(m);
    else if (m?.type === 'cloud-setup') onCloudSetup(m);
    else if (m?.type === 'cloud-check') onCloudCheck(m);
    else if (m?.type === 'app') onApp(m);
    else if (m?.type === 'share') onSavedShare(m.data);
    else if (m?.type === 'select') selectSession(m.id);
    else onSnapshot(m);
  });
  send({ cmd: 'ready' });   // 앱이 저장해 둔 별명·메모(share)를 돌려주면 합쳐서 다시 보낸다(onSavedShare)
  if (pinned) send({ cmd: 'topmost', value: true });
  send({ cmd: 'window', hours: windowHours });   // 앱은 늘 12시간으로 시작하므로 저장해 둔 값을 알려 준다
}

requestAnimationFrame(frame);
