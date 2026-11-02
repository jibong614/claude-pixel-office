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

// 이 화면 파일의 버전 — csproj 의 <Version> 과 같아야 한다(다르면 빌드가 멈춘다). 휴대폰 화면이 최신인지 설정 맨 위에 보여 준다
const PAGE_VERSION = '1.5.8';

// ───────────────────────────── 연결 ─────────────────────────────

const host =window.chrome && window.chrome.webview ? window.chrome.webview : null;
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
// 웹 서버(공용 페이지 GitHub Pages)에 올린 휴대폰 화면 — 연결 정보가 없으면 견본 대신 "QR을 찍어 주세요"를 보여 준다
const HOSTED = location.protocol === 'https:' && location.hostname !== 'office.local';
const REMOTE = !params.has('demo') && (!!remoteLink || HOSTED);
const DEMO = !REMOTE && (!host || params.has('demo'));
const WIDGET_WINDOW = params.has('pet') || params.has('widget_window') || params.has('miniwidget'); // 바탕화면 미니위젯 전용 창(?widget_window=1)
const PET_WINDOW = WIDGET_WINDOW; // 하위 호환 별칭

// 화면 언어(⚙️ → 사무실 → 화면 언어). PC 앱은 주소의 ?lang=en 으로 알려 준다(한국어는 붙이지 않음).
// 휴대폰 보기는 PC가 요약에 실어 보낸 언어를 이 기기에 저장해 두고 쓴다(startRemote — 다르면 저장하고 다시 연다).
const LANG_KEY = 'co.m.lang';
const LANG = (() => {
  const q = params.get('lang');
  if (q === 'en' || q === 'ko') return q;
  if (REMOTE) {
    try { const s = localStorage.getItem(LANG_KEY); if (s === 'en' || s === 'ko') return s; } catch { /* 저장소를 못 쓰면 기본 */ }
  }
  return 'ko';
})();
/** 한국어·영어 문구 중 지금 화면 언어의 것. 화면 글은 열 때 정해지므로 언어를 바꾸면 페이지를 다시 연다. */
const T = (ko, en) => (LANG === 'en' ? en : ko);
document.documentElement.lang = LANG;
// index.html 의 고정 글: 영어면 data-en(글)·data-en-title(풍선 도움말)·data-en-ph(입력칸 안내) 로 바꾼다
if (LANG === 'en') {
  for (const el of document.querySelectorAll('[data-en]')) el.textContent = el.dataset.en;
  for (const el of document.querySelectorAll('[data-en-title]')) el.title = el.dataset.enTitle;
  for (const el of document.querySelectorAll('[data-en-ph]')) el.placeholder = el.dataset.enPh;
}

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
// 팀(작업 폴더) 표시: {팀: {name, color(RUGS 번호)}} · 팀 순서 · 직원 자리 순서(세션 ID). 휴대폰은 PC가 보낸 것을 쓴다(remoteShare)
const teamPrefs = store.get('teams', {}) || {};
let teamOrder = store.get('teamOrder', []) || [];
let seatOrder = store.get('seatOrder', []) || [];
let teamRev = 0;   // 바뀌면 배치를 다시 한다
let waitOnly = store.get('waitOnly', false);   // ✋ 기다리는 직원만 보기
let zoom = Math.max(-2, Math.min(2, Math.round(Number(store.get('zoom', 0)) || 0)));   // 사무실 크기: 화면에 맞춘 배율에서 몇 단계 작게(−)·크게(+)
let pinned = store.get('pinned', false);

// 직원 표시: 최대 인원(maxShown)과 고르는 방식(pickMode: recent=최근 사용순 / manual=직접 고르기, pickedIds=체크한 세션)
const SHOWN_MIN = 1, SHOWN_MAX = 30;
let maxShown = Math.min(SHOWN_MAX, Math.max(SHOWN_MIN, Number(store.get('maxShown', 8)) || 8));
let pickMode = store.get('pickMode', 'recent') === 'manual' ? 'manual' : 'recent';
const pickedIds = new Set(store.get('picked', []));

// 표시 기간(시간): 마지막 대화가 이 안에 있는 세션만 앱(C#)이 보내 준다. 값은 화면이 저장하고 시작할 때마다 앱에 알린다
const WINDOW_CHOICES = [[12, T('12시간', '12 hours')], [24, T('24시간', '24 hours')], [72, T('3일', '3 days')], [168, T('7일', '7 days')]];
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
let remoteState = null;   // 자리 비움 원격 상태(PC 앱은 {type:"remote"}, 휴대폰은 요약의 remote) — 아래 "자리 비움 원격"
let remotePcVersion = '';   // 휴대폰 보기: PC 앱 버전(1.1.2 부터 요약에 실어 보낸다)
const noteOf = (id) => (REMOTE ? remoteShare?.notes?.[id] : notes[id]) || {};
const displayName = (s) => noteOf(s.id).nick || s.name;

/** 사무실 이름·별명·메모를 앱에 넘긴다 — 휴대폰 보기를 켰으면 요약과 함께 암호화해 올라간다. */
const shareToApp = () => send({ cmd: 'share', data: { office: officeName, notes, teams: { prefs: teamPrefs, order: teamOrder, seats: seatOrder } } });

function setNote(id, patch) {
  const n = { ...noteOf(id), ...patch, t: Date.now() };
  if (!n.nick) delete n.nick;
  if (!n.memo || !n.memo.trim()) delete n.memo;
  if (!Number.isInteger(n.look) || n.look <= 0) delete n.look;
  if (!Number.isInteger(n.deco) || n.deco < 0 || n.deco > 3) delete n.deco;
  if (n.nick || n.memo || n.look || n.deco !== undefined) notes[id] = n;
  else delete notes[id];
  lookCache.clear();
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
      const look = Number.isInteger(n.look) && n.look > 0 ? n.look : 0;
      const deco = Number.isInteger(n.deco) && n.deco >= 0 && n.deco <= 3 ? n.deco : undefined;
      if (nick || memo || look || deco !== undefined) {
        notes[id] = { ...(nick && { nick }), ...(memo && { memo }), ...(look && { look }), ...(deco !== undefined && { deco }), t: Number(n.t) || Date.now() };
      } else if (notes[id]) delete notes[id];   // 휴대폰에서 둘 다 지운 것(빈 기록 {t})
    }
    lookCache.clear();
    store.set('notes', notes);
    const t = saved.teams;   // 팀 이름·색·순서: 이 화면에 아무것도 없을 때만 사본 것으로
    if (t && typeof t === 'object' && !Object.keys(teamPrefs).length && !teamOrder.length && !seatOrder.length) {
      if (t.prefs && typeof t.prefs === 'object') Object.assign(teamPrefs, t.prefs);
      if (Array.isArray(t.order)) teamOrder = t.order.filter((x) => typeof x === 'string');
      if (Array.isArray(t.seats)) seatOrder = t.seats.filter((x) => typeof x === 'string');
      store.set('teams', teamPrefs); store.set('teamOrder', teamOrder); store.set('seatOrder', seatOrder);
      teamRev++; layoutKey = '';
    }
    if (!officeName && typeof saved.office === 'string' && saved.office.trim()) setOfficeName(saved.office.slice(0, 30));
  }
  shareToApp();
  if (selectedId) { noteFor = null; renderPanel(); }
  refreshSettings();
}

/** 설정 초기화(앱이 확인을 받은 뒤): 화면 설정을 지우고 다시 읽는다. 별명·메모·사무실 이름·팀 이름·순서는 남긴다(사람이 쓴 것). */
function resetScreenSettings() {
  const keep = new Set(['notes', 'officeName', 'teams', 'teamOrder', 'seatOrder'].map((k) => STORE_PREFIX + k));
  try {
    for (const k of Object.keys(localStorage)) if (k.startsWith(STORE_PREFIX) && !keep.has(k)) localStorage.removeItem(k);
  } catch { /* 저장소를 못 쓰면 그대로 */ }
  location.reload();
}

// ─────────────────────────── 레트로 8비트 효과음 (Web Audio) ───────────────────────────
let retroAudioCtx = null;
let lastSoundTime = 0;

function getRetroAudioCtx() {
  try {
    if (!retroAudioCtx) {
      const AC = window.AudioContext || window.webkitAudioContext;
      if (AC) retroAudioCtx = new AC();
    }
    if (retroAudioCtx && retroAudioCtx.state === 'suspended') {
      retroAudioCtx.resume().catch(() => {});
    }
    return retroAudioCtx;
  } catch {
    return null;
  }
}

function isQuietNow() {
  if (!appSettings?.quietEnabled) return false;
  const start = appSettings.quietStart ?? 22, end = appSettings.quietEnd ?? 8;
  if (start === end) return false;
  const h = new Date().getHours();
  return start < end ? (h >= start && h < end) : (h >= start || h < end);
}

function playRetroSound(kind, force = false) {
  if (!force) {
    if (appSettings && appSettings.notifySound === false) return;
    if (isQuietNow()) return;
    const now = Date.now();
    if (now - lastSoundTime < 1500) return;
    try {
      const lastShared = parseInt(localStorage.getItem('co.last_sound') || '0', 10);
      if (now - lastShared < 1000) return;
      localStorage.setItem('co.last_sound', String(now));
    } catch {}
    lastSoundTime = now;
  }
  const ctx = getRetroAudioCtx();
  if (!ctx) return;

  const t0 = ctx.currentTime;
  if (kind === 'ask') {
    // ✋ 승인·질문 대기: 경쾌한 2단 띵-동 (D5 587Hz -> A5 880Hz)
    playTone(ctx, 587.33, t0, 0.075, 'triangle', 0.15);
    playTone(ctx, 880.00, t0 + 0.08, 0.16, 'triangle', 0.18);
  } else if (kind === 'done') {
    // ✅ 작업 완료: 4음 아르페지오 (C5 -> E5 -> G5 -> C6)
    playTone(ctx, 523.25, t0, 0.055, 'triangle', 0.12);
    playTone(ctx, 659.25, t0 + 0.055, 0.055, 'triangle', 0.13);
    playTone(ctx, 783.99, t0 + 0.11, 0.055, 'triangle', 0.14);
    playTone(ctx, 1046.50, t0 + 0.165, 0.22, 'triangle', 0.16);
  } else if (kind === 'send') {
    // 🚀 지시 전송: 짧고 가벼운 45ms 픽셀 블립 (880Hz -> 1320Hz)
    playSweep(ctx, 880, 1320, t0, 0.045, 'triangle', 0.10);
  } else if (kind === 'coin') {
    // 🪙 코인 획득: 마리오/RPG 풍 경쾌한 2단 상승 차임벨 (B5 -> E6)
    playTone(ctx, 987.77, t0, 0.065, 'square', 0.12);
    playTone(ctx, 1318.51, t0 + 0.07, 0.18, 'square', 0.15);
  } else if (kind === 'buy') {
    // 🛍️ 상점 구매 / 팡파레: 5음 상승 (C5 -> E5 -> G5 -> C6 -> E6)
    playTone(ctx, 523.25, t0, 0.05, 'square', 0.11);
    playTone(ctx, 659.25, t0 + 0.05, 0.05, 'square', 0.12);
    playTone(ctx, 783.99, t0 + 0.10, 0.05, 'square', 0.13);
    playTone(ctx, 1046.50, t0 + 0.15, 0.05, 'square', 0.14);
    playTone(ctx, 1318.51, t0 + 0.20, 0.22, 'triangle', 0.16);
  } else if (kind === 'cat') {
    // 🐱 고양이 야옹: 귀여운 주파수 스윕 (880Hz -> 660Hz)
    playSweep(ctx, 880, 660, t0, 0.18, 'triangle', 0.14);
  } else if (kind === 'stretch') {
    // 으라차 기지개: 상쾌한 2음 상승
    playTone(ctx, 659.25, t0, 0.06, 'triangle', 0.10);
    playTone(ctx, 880.00, t0 + 0.06, 0.09, 'triangle', 0.11);
  } else if (kind === 'stop') {
    // 🛑 비상정지: 하강 경고음 (440Hz -> 160Hz)
    playSweep(ctx, 440, 160, t0, 0.16, 'sawtooth', 0.15);
  } else if (kind === 'coffee') {
    // ☕ 커피 따르기 / 마시기: 따뜻한 픽셀 멜로디
    playTone(ctx, 587.33, t0, 0.05, 'triangle', 0.10);
    playTone(ctx, 740.00, t0 + 0.06, 0.08, 'triangle', 0.12);
  } else if (kind === 'arcade') {
    // 🕹️ 아케이드 뿅뿅: 빠른 레이저 펄스
    playSweep(ctx, 1200, 300, t0, 0.08, 'square', 0.12);
    playSweep(ctx, 800, 1600, t0 + 0.09, 0.10, 'square', 0.12);
  } else if (kind === 'vending') {
    // 🥤 자판기 덜컹: 캔 떨어지는 둔탁한 소리 후 차랑
    playTone(ctx, 220, t0, 0.06, 'sawtooth', 0.14);
    playTone(ctx, 110, t0 + 0.07, 0.12, 'sawtooth', 0.16);
    playTone(ctx, 880, t0 + 0.14, 0.06, 'square', 0.10);
  } else if (kind === 'test') {
    // ▶ 미리듣기: ask -> done -> coin 메들리
    playTone(ctx, 587.33, t0, 0.075, 'triangle', 0.15);
    playTone(ctx, 880.00, t0 + 0.08, 0.16, 'triangle', 0.18);
    playTone(ctx, 523.25, t0 + 0.35, 0.055, 'triangle', 0.12);
    playTone(ctx, 659.25, t0 + 0.405, 0.055, 'triangle', 0.13);
    playTone(ctx, 783.99, t0 + 0.46, 0.055, 'triangle', 0.14);
    playTone(ctx, 1046.50, t0 + 0.515, 0.22, 'triangle', 0.16);
    playTone(ctx, 987.77, t0 + 0.80, 0.065, 'square', 0.12);
    playTone(ctx, 1318.51, t0 + 0.87, 0.20, 'square', 0.15);
  }
}

function playTone(ctx, freq, start, dur, type = 'triangle', vol = 0.15) {
  try {
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.type = type;
    osc.frequency.setValueAtTime(freq, start);
    gain.gain.setValueAtTime(0.001, start);
    gain.gain.linearRampToValueAtTime(vol, start + 0.008);
    gain.gain.exponentialRampToValueAtTime(0.001, start + dur);
    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.start(start);
    osc.stop(start + dur + 0.02);
  } catch {}
}

function playSweep(ctx, startFreq, endFreq, start, dur, type = 'triangle', vol = 0.10) {
  try {
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.type = type;
    osc.frequency.setValueAtTime(startFreq, start);
    osc.frequency.exponentialRampToValueAtTime(endFreq, start + dur);
    gain.gain.setValueAtTime(0.001, start);
    gain.gain.linearRampToValueAtTime(vol, start + 0.005);
    gain.gain.exponentialRampToValueAtTime(0.001, start + dur);
    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.start(start);
    osc.stop(start + dur + 0.01);
  } catch {}
}

/** 알림이나 메뉴를 눌러 창을 열었을 때: 그 직원(또는 @today 근무 일지)의 패널을 연다. */
function selectSession(id) {
  if (id !== REPORT_ID && !data.sessions.some((s) => s.id === id)) return;
  if (!settingsEl.hidden) setSettingsOpen(false);
  selectedId = id;
  drawerFolded = false;
  renderPanel();
}
let selectedId = params.get('select');   // 화면 점검용: ?demo&select=demo-b1 이면 상세 패널을, select=settings 면 설정을 연 채로 시작
let hoverId = null;
let curWhiteboardBox = null;

let prevSessionsAct = new Map();

function onSnapshot(snap) {
  if (!snap || !Array.isArray(snap.sessions)) return;

  // 작업 완료 감지 시 코인 보상 & 야근 업적 확인
  if (prevSessionsAct.size > 0) {
    for (const s of snap.sessions) {
      const prev = prevSessionsAct.get(s.id);
      if (prev && ['writing', 'running', 'working', 'searching'].includes(prev) && ['idle', 'waiting', 'asking'].includes(s.act)) {
        addCoins(15, T('작업 완료', 'Task completed'));
        playRetroSound('coin');
        const h = new Date().getHours();
        if (h >= 22 || h < 6) markQuest('night_shift');
      }
    }
  }
  prevSessionsAct = new Map(snap.sessions.map((s) => [s.id, s.act]));

  data = snap;
  skew = (snap.now || Date.now()) - Date.now();
  if (WIDGET_WINDOW) { renderPet(); return; }
  refreshHud();
  refreshHookButton();
  refreshSettings();
  if (selectedId) renderPanel();
  if (whiteboardModalEl && !whiteboardModalEl.hidden) renderWhiteboardCards();
  if (achieveModalEl && !achieveModalEl.hidden) renderAchieveModal();
  if (DEMO && petOn()) renderPet();
}

// ───────────────────────────── 상태별 표현 ─────────────────────────────
// pose: 사람 자세 / screen: 모니터 화면 / bubble: 말풍선 모양 / tone: 상태 글자색

const ACT = {
  thinking:   { label: T('생각 중', 'Thinking'),               pose: 'think', screen: 'cursor', bubble: 'thought', tone: '#a9c8ff', busy: true },
  talking:    { label: T('답변 작성', 'Answering'),            pose: 'type',  screen: 'doc',    bubble: 'normal',  tone: '#a9c8ff', busy: true },
  reading:    { label: T('읽는 중', 'Reading'),                pose: 'read',  screen: 'doc',    bubble: 'normal',  tone: '#a8e6a1', busy: true },
  searching:  { label: T('찾는 중', 'Searching'),              pose: 'type',  screen: 'search', bubble: 'normal',  tone: '#a8e6a1', busy: true },
  writing:    { label: T('코드 작성', 'Writing code'),         pose: 'type',  screen: 'code',   bubble: 'normal',  tone: '#ffd27a', busy: true },
  running:    { label: T('명령 실행', 'Running'),              pose: 'run',   screen: 'term',   bubble: 'normal',  tone: '#7dffa0', busy: true },
  browsing:   { label: T('웹 검색', 'Web search'),             pose: 'type',  screen: 'web',    bubble: 'normal',  tone: '#a9c8ff', busy: true },
  delegating: { label: T('인턴 지휘', 'Leading interns'),      pose: 'type',  screen: 'team',   bubble: 'normal',  tone: '#d7a6ff', busy: true },
  planning:   { label: T('계획 정리', 'Planning'),             pose: 'think', screen: 'todo',   bubble: 'thought', tone: '#ffd27a', busy: true },
  working:    { label: T('작업 중', 'Working'),                pose: 'type',  screen: 'code',   bubble: 'normal',  tone: '#ffd27a', busy: true },
  asking:     { label: T('질문 답변 대기', 'Waiting for answer'), pose: 'raise', screen: 'alert',  bubble: 'alert',   tone: '#ffb347', wait: true },
  waiting:    { label: T('승인 대기', 'Needs approval'),       pose: 'raise', screen: 'alert',  bubble: 'alert',   tone: '#ffb347', wait: true },
  idle:       { label: T('대기', 'Idle'),                      pose: 'idle',  screen: 'saver',  bubble: 'calm',    tone: '#c9c3d9' },
  sleeping:   { label: T('졸음', 'Dozing'),                    pose: 'sleep', screen: 'off',    bubble: 'zzz',     tone: '#8f88a8' },
  away:       { label: T('퇴근', 'Gone home'),                 pose: 'away',  screen: 'off',    bubble: 'none',    tone: '#6f6987' },
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

// 세션 ID(또는 사용자가 바꾼 모습 번호·소품)로 정해지는 생김새
const lookCache = new Map();
function looks(id) {
  const n = noteOf(id);
  const lookIdx = Number.isInteger(n.look) && n.look > 0 ? n.look : 0;
  const decoOverride = Number.isInteger(n.deco) && n.deco >= 0 && n.deco <= 3 ? n.deco : null;
  const key = `${id}:${lookIdx}:${decoOverride ?? ''}`;
  let L = lookCache.get(key);
  if (L) return L;
  const seed = lookIdx ? `${id}@${lookIdx}` : id;
  const h = hash(seed), h2 = hash(seed + '#');
  L = {
    skin: SKINS[h % SKINS.length],
    hair: HAIRS[(h >>> 4) % HAIRS.length],
    shirt: SHIRTS[(h >>> 9) % SHIRTS.length],
    pants: PANTS[(h >>> 14) % PANTS.length],
    hairStyle: (h2 >>> 2) % 3,
    glasses: (h2 >>> 6) % 4 === 0,
    deco: decoOverride !== null ? decoOverride : (h2 >>> 9) % 4,
    blink: h2 % 3200,
  };
  lookCache.set(key, L);
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
let searchQuery = '';

function matchSearch(s, q) {
  if (!q) return true;
  const n = noteOf(s.id);
  const hay = [displayName(s), s.name, s.title, teamLabel(s.team), s.team, s.text, s.branch, s.cwd, n.memo].filter(Boolean).join(' ').toLowerCase();
  return hay.includes(q);
}

// 책상에 앉힐 직원: 퇴근 숨김 → 검색 필터 → (직접 고르기면) 체크한 직원만 → 최대 인원을 넘으면 최근에 일한 순으로 자른다.
// 자르기만 하고 순서는 바꾸지 않는다 — 자리는 언제나 출근 순서(C#이 보낸 순서)라 책상이 뒤섞이지 않는다.
function visibleSessions() {
  let list = data.sessions.filter((s) => showAway || s.act !== 'away');
  if (searchQuery) {
    const q = searchQuery.trim().toLowerCase();
    if (q) list = list.filter((s) => matchSearch(s, q));
  }
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
  const key = [vw, innerHeight, devicePixelRatio, zoom, teamRev, ...list.map((s) => s.id + '|' + s.team)].join(',');
  if (key === layoutKey) return;
  layoutKey = key;

  dpr = window.devicePixelRatio || 1;
  // 팀(작업 폴더)별로 묶기 — 먼저 출근한 순서 유지
  const teams = new Map();
  for (const s of list) {
    if (!teams.has(s.team)) teams.set(s.team, []);
    teams.get(s.team).push(s);
  }
  orderTeams(teams);

  // 화면에 다 들어가는 가장 큰 배율을 고른다(아트 1픽셀 = 화면 k픽셀, k는 정수). 그래도 넘치면 세로 스크롤.
  // 고해상도(배율 125% 등)에서는 k를 1씩 내려가며 더 촘촘하게 맞춘다.
  let best = null;
  const kMax = Math.max(2, Math.round(4 * dpr)), kMin = Math.max(2, Math.round(2 * dpr));
  for (let k = kMax; k >= kMin; k--) {
    const lw = Math.floor((vw * dpr) / k);
    const viewLh = Math.floor((innerHeight * dpr) / k);
    const plan = planZones([...teams.entries()], lw, cellHFor(k));
    best = { k, lw, viewLh, plan };
    if (plan.height <= viewLh) break;
  }

  if (zoom) {   // 사무실 크기 설정: 맞춘 배율에서 단계만큼(크게 하면 넘치는 만큼 세로 스크롤)
    const k = Math.max(1, best.k + zoom);
    const lw = Math.floor((vw * dpr) / k);
    best = { k, lw, viewLh: Math.floor((innerHeight * dpr) / k), plan: planZones([...teams.entries()], lw, cellHFor(k)) };
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

// ── 팀 이름·색·순서, 직원 자리 순서 ──
const prefsOf = () => (REMOTE ? remoteShare?.teams?.prefs || {} : teamPrefs);
const teamLabel = (team) => prefsOf()[team]?.name || team;
function teamColor(team) {
  const c = prefsOf()[team]?.color;
  return Number.isInteger(c) && c >= 0 && c < RUGS.length ? RUGS[c] : RUGS[hash(team) % RUGS.length];
}

/** 정해 둔 팀 순서·자리 순서대로(정하지 않은 것은 출근 순서 그대로 뒤에). teams: Map(팀 → 직원들) 을 그 자리에서 바꾼다. */
function orderTeams(teams) {
  const tOrder = REMOTE ? remoteShare?.teams?.order || [] : teamOrder;
  const sOrder = REMOTE ? remoteShare?.teams?.seats || [] : seatOrder;
  const rank = (arr, k) => { const i = arr.indexOf(k); return i < 0 ? Infinity : i; };
  const entries = [...teams.entries()];
  entries.forEach(([, members], i) => {
    const pos = new Map(members.map((s, j) => [s.id, j]));
    members.sort((a, b) => (rank(sOrder, a.id) - rank(sOrder, b.id)) || (pos.get(a.id) - pos.get(b.id)));
  });
  const first = new Map(entries.map(([t], i) => [t, i]));
  entries.sort(([a], [b]) => (rank(tOrder, a) - rank(tOrder, b)) || (first.get(a) - first.get(b)));
  teams.clear();
  for (const [t, m] of entries) teams.set(t, m);
}

/** 지금 사무실에 있는 팀(보이는 순서). */
function currentTeams() {
  const m = new Map();
  for (const s of data.sessions) { if (!m.has(s.team)) m.set(s.team, []); m.get(s.team).push(s); }
  orderTeams(m);
  return [...m.keys()];
}

function saveTeams() {
  store.set('teams', teamPrefs);
  store.set('teamOrder', teamOrder);
  store.set('seatOrder', seatOrder);
  teamRev++;
  layoutKey = '';
  shareToApp();   // 휴대폰에도 같은 이름·색·순서
}

function setTeamPref(team, patch) {
  const p = { ...(teamPrefs[team] || {}), ...patch };
  if (!p.name) delete p.name;
  if (!Number.isInteger(p.color)) delete p.color;
  if (Object.keys(p).length) teamPrefs[team] = p; else delete teamPrefs[team];
  saveTeams();
}

/** 팀을 한 칸 앞(-1)·뒤(+1)로. 지금 보이는 순서를 통째로 저장한다. */
function moveTeam(team, dir) {
  const order = currentTeams();
  const i = order.indexOf(team), j = i + dir;
  if (i < 0 || j < 0 || j >= order.length) return;
  [order[i], order[j]] = [order[j], order[i]];
  teamOrder = [...order, ...teamOrder.filter((t) => !order.includes(t))].slice(0, 100);
  saveTeams();
}

/** 직원 자리를 같은 팀 안에서 한 칸 앞(-1)·뒤(+1)로. */
function moveSeat(id, dir) {
  const s = data.sessions.find((x) => x.id === id);
  if (!s) return;
  const m = new Map([[s.team, data.sessions.filter((x) => x.team === s.team)]]);
  orderTeams(m);
  const order = m.get(s.team).map((x) => x.id);
  const i = order.indexOf(id), j = i + dir;
  if (j < 0 || j >= order.length) return;
  [order[i], order[j]] = [order[j], order[i]];
  seatOrder = [...order, ...seatOrder.filter((x) => !order.includes(x))].slice(0, 300);
  saveTeams();
  renderPanel();
}

/**
 * 책상 한 줄 높이(아트 픽셀). 배율이 작으면(휴대폰 등) 글자는 최소 크기에 걸려 그대로인데 줄 간격만 줄어,
 * 아랫줄 직원의 말풍선이 윗줄 이름표·상태 글을 덮는다 → 그만큼 줄 사이를 벌린다.
 * 윗줄 상태 글 아래 끝 = (53 아트 + 이름 + 상태 + 5px), 아랫줄 손 든 머리(15 아트) 위 말풍선 위 끝 = (CELL_H + 15 아트 − 말풍선 − 23px)
 */
function cellHFor(k) {
  const u = k / dpr;
  const fn = clamp(Math.round(4.2 * u), 11, 16), fs = clamp(Math.round(3.7 * u), 10, 14), fb = clamp(Math.round(4 * u), 11, 15);
  const shortBy = fb + fn + fs + 30 - (CELL_H + 15 - 53) * u;   // 모자란 CSS 픽셀(여유 2px 포함)
  return CELL_H + Math.max(0, Math.ceil(shortBy / u));
}

function planZones(teams, lw, cellH = CELL_H) {
  const avail = lw - 16;
  const maxCols = Math.max(1, Math.min(MAX_COLS, Math.floor((avail - ZONE_PAD * 2) / CELL_W)));

  const boxes = teams.map(([team, members]) => {
    const cols = Math.min(members.length, maxCols);
    const rows = Math.ceil(members.length / cols);
    return { team, members, cols, w: cols * CELL_W + ZONE_PAD * 2, h: ZONE_HEAD + rows * cellH + ZONE_PAD };
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
      zonesOut.push({ team: b.team, x, y, w: b.w, h: b.h, count: b.members.length, color: teamColor(b.team) });
      b.members.forEach((s, i) => {
        desksOut.push({ s, x: x + ZONE_PAD + (i % b.cols) * CELL_W, y: y + ZONE_HEAD + Math.floor(i / b.cols) * cellH });
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

// ───────────────────────────── 🛋️ 가구/인테리어 상점 & 코인 시스템 ─────────────────────────────

const COIN_KEY = STORE_PREFIX + 'coins';
const FURNITURE_KEY = STORE_PREFIX + 'furniture';

function getCoins() {
  const v = localStorage.getItem(COIN_KEY);
  if (v === null) {
    // 신규 환영 스타터 코인: 200 코인 지급
    localStorage.setItem(COIN_KEY, '200');
    return 200;
  }
  return parseInt(v, 10) || 0;
}

function addCoins(n, reason = '') {
  if (n <= 0) return;
  const cur = getCoins();
  const next = cur + n;
  localStorage.setItem(COIN_KEY, String(next));
  updateCoinDisplay();
}

function spendCoins(n) {
  const cur = getCoins();
  if (cur < n) return false;
  localStorage.setItem(COIN_KEY, String(cur - n));
  updateCoinDisplay();
  return true;
}

function updateCoinDisplay() {
  const coins = getCoins();
  const hudCoinsEl = $('hudCoins');
  if (hudCoinsEl) hudCoinsEl.textContent = coins.toLocaleString();
  const shopCoinsEl = $('shopCoinsTotal');
  if (shopCoinsEl) shopCoinsEl.textContent = coins.toLocaleString();
}

function getOwnedFurniture() {
  try {
    return JSON.parse(localStorage.getItem(FURNITURE_KEY) || '[]');
  } catch {
    return [];
  }
}

function isFurnitureOwned(id) {
  return getOwnedFurniture().includes(id);
}

function buyFurniture(id) {
  const item = SHOP_ITEMS.find((x) => x.id === id);
  if (!item) return false;
  const owned = getOwnedFurniture();
  if (owned.includes(id)) return true;
  if (!spendCoins(item.price)) {
    showToast(T(`코인이 부족합니다! (필요: 🪙 ${item.price} 코인)`, `Not enough coins! (Need: 🪙 ${item.price} coins)`), '🪙');
    return false;
  }
  owned.push(id);
  localStorage.setItem(FURNITURE_KEY, JSON.stringify(owned));
  playRetroSound('buy');
  markQuest('first_furniture');
  renderShopModal();
  updateCoinDisplay();
  return true;
}

const SHOP_ITEMS = [
  {
    id: 'coffee_bar',
    name: '원두 에스프레소 머신 & 바',
    enName: 'Espresso Bar',
    price: 150,
    icon: '☕',
    desc: '갓 내린 원두 향이 솔솔! 클릭 시 따뜻한 커피를 내립니다.',
    enDesc: 'Fresh espresso bar for the office. Click to brew hot coffee.',
  },
  {
    id: 'vending_machine',
    name: '레트로 음료 자판기',
    enName: 'Retro Vending Machine',
    price: 180,
    icon: '🥤',
    desc: '시원한 캔음료와 에너지 드링크가 가득. 클릭 시 덜컹 소리와 함께 음료가 나옵니다.',
    enDesc: 'Filled with cold drinks and energy sodas. Click to vend a can.',
  },
  {
    id: 'cat_tower',
    name: '캣타워 & 사무실 고양이 나비',
    enName: 'Cat Tower & Nabi',
    price: 250,
    icon: '🐱',
    desc: '개발자의 최고의 힐링 친구! 캣타워 위에서 낮잠을 자고 클릭하면 야옹~ 웁니다.',
    enDesc: 'The ultimate healing buddy! Snoozes on top and meows when clicked.',
  },
  {
    id: 'arcade_cabinet',
    name: '8비트 레트로 아케이드 오락기',
    enName: 'Retro Arcade Cabinet',
    price: 220,
    icon: '🕹️',
    desc: '휴식 시간에 스트리트 파이터 한 판! 클릭하면 신나는 8비트 게임 사운드가 울립니다.',
    enDesc: 'Take a quick gaming break! Click for nostalgic 8-bit sound effects.',
  },
  {
    id: 'tech_bookshelf',
    name: '개발 서적 서가',
    enName: 'Tech Bookshelf',
    price: 120,
    icon: '📚',
    desc: '클린 코드, 리팩토링, 알고리즘 서적이 꽂혀 있어 개발 집중력을 높여줍니다.',
    enDesc: 'Clean Code and algorithm classics to boost developer concentration.',
  },
  {
    id: 'monstera_plant',
    name: '대형 몬스테라 관엽식물',
    enName: 'Monstera Plant',
    price: 80,
    icon: '🌿',
    desc: '공기 정화와 눈의 피로를 풀어주는 싱그러운 대형 화분.',
    enDesc: 'A lush green monstera pot to purify office air and refresh tired eyes.',
  },
  {
    id: 'lounge_sofa',
    name: '푹신한 패브릭 휴게 소파',
    enName: 'Lounge Sofa',
    price: 200,
    icon: '🛋️',
    desc: '휴식할 때 기분 좋게쉴 수 있는 편안한 2인용 패브릭 소파.',
    enDesc: 'A cozy fabric sofa for the team to relax and brainstorm.',
  },
];

let curFurnitureBoxes = [];

// ── 가구별 픽셀아트 렌더링 ──

function drawCoffeeBar(x, y, t) {
  curFurnitureBoxes.push({ id: 'coffee_bar', x, y, w: 22, h: 24, name: T('☕ 에스프레소 머신 & 바', '☕ Espresso Bar') });
  // 원목 카운터 베이스
  R(x, y + 10, 22, 14, '#7c5230');
  R(x, y + 10, 22, 2, '#9c6a42');
  // 메탈 에스프레소 머신 본체
  R(x + 2, y + 2, 12, 8, '#d4d8e0');
  R(x + 3, y, 10, 2, '#9aa2b4');
  // 듀얼 포터필터 & 드립 트레이
  R(x + 4, y + 7, 2, 3, '#2a2a30');
  R(x + 9, y + 7, 2, 3, '#2a2a30');
  R(x + 3, y + 9, 10, 1, '#404550');
  // 김 모락모락 애니메이션
  const ph = (t / 340) | 0;
  P(x + 5 + (ph % 2), y - 1 - (ph % 3), 'rgba(255,255,255,0.7)');
  // 시럽 병 & 머그잔 적재
  R(x + 16, y + 3, 2, 7, '#b8860b');
  R(x + 19, y + 4, 2, 6, '#8b4513');
  P(x + 15, y + 2, '#ffffff');
}

function drawVendingMachine(x, y, t) {
  curFurnitureBoxes.push({ id: 'vending_machine', x, y, w: 18, h: 28, name: T('🥤 레트로 음료 자판기', '🥤 Retro Vending Machine') });
  // 레드 캐비닛
  R(x, y, 18, 28, '#dc2626');
  R(x + 1, y - 1, 16, 1, '#ef4444');
  // 음료 디스플레이 유리창
  R(x + 2, y + 3, 14, 11, '#1e293b');
  // 1열 캔음료 (블루, 그린, 오렌지, 옐로우)
  P(x + 4, y + 5, '#38bdf8'); P(x + 7, y + 5, '#4ade80'); P(x + 10, y + 5, '#fb923c'); P(x + 13, y + 5, '#facc15');
  // 2열 캔음료 (레드, 퍼플, 시안, 라임)
  P(x + 4, y + 9, '#f43f5e'); P(x + 7, y + 9, '#a855f7'); P(x + 10, y + 9, '#38bdf8'); P(x + 13, y + 9, '#4ade80');
  // 조명 깜빡임 효과
  if (tick(t, 900)) R(x + 2, y + 3, 14, 1, 'rgba(255,255,255,0.4)');
  // 동전 투입구 & 잔돈 반환 레버
  P(x + 13, y + 16, '#ffd700'); P(x + 14, y + 18, '#22c55e');
  // 하단 캔 배출구 플랩
  R(x + 3, y + 21, 12, 5, '#1e1b2e');
  R(x + 5, y + 21, 8, 1, '#94a3b8');
}

function drawCatTower(x, y, t) {
  curFurnitureBoxes.push({ id: 'cat_tower', x, y, w: 18, h: 32, name: T('🐱 나비 (사무실 고양이)', '🐱 Nabi (Office Cat)') });
  // 삼줄 기둥 & 받침대
  R(x + 2, y + 28, 14, 4, '#a27b5c');
  R(x + 7, y + 8, 4, 20, '#d7b38c');
  for (let py = y + 9; py < y + 28; py += 3) R(x + 7, py, 4, 1, '#b88d66');
  // 중간 발판 & 상단 캣타워 전망대
  R(x + 3, y + 18, 12, 2, '#a27b5c');
  R(x + 2, y + 7, 14, 3, '#a27b5c');
  // 매달린 장난감 방울 (흔들림)
  const sway = tick(t, 550) ? 1 : 0;
  R(x + 4, y + 10, 1, 4, '#e2e8f0');
  P(x + 4 + sway, y + 14, '#ef4444');

  // 🐱 치즈태비 고양이 '나비' (식빵 굽기 자세)
  R(x + 4, y + 2, 9, 5, '#f59e0b');
  R(x + 7, y + 6, 5, 1, '#ffffff'); // 흰 양말/가슴털
  // 쫑긋한 귀
  P(x + 5, y + 1, '#d97706'); P(x + 10, y + 1, '#d97706');
  // 행복하게 감은 눈 & 핑크 코
  P(x + 7, y + 3, '#78350f'); P(x + 9, y + 3, '#78350f');
  P(x + 8, y + 4, '#fb7185');
  // 살랑살랑 흔드는 꼬리
  const tailUp = tick(t, 450);
  P(x + 13, y + 4, '#d97706');
  P(x + 14, y + (tailUp ? 2 : 3), '#f59e0b');
}

function drawArcadeCabinet(x, y, t) {
  curFurnitureBoxes.push({ id: 'arcade_cabinet', x, y, w: 18, h: 30, name: T('🕹️ 8비트 아케이드 오락기', '🕹️ 8-bit Arcade') });
  // 네이비/퍼플 캐비닛 바디
  R(x + 1, y, 16, 28, '#1e1b4b');
  R(x + 2, y + 1, 14, 26, '#312e81');
  // 상단 네온 마키 (불빛)
  R(x + 3, y + 2, 12, 4, '#ec4899');
  R(x + 4, y + 3, 10, 2, '#f472b6');
  // CRT 모니터 (스페이스 인베이더 점멸)
  R(x + 3, y + 7, 12, 10, '#020617');
  const b = tick(t, 350) ? 2 : 0;
  P(x + 6 + b, y + 10, '#22c55e'); P(x + 8 + b, y + 10, '#22c55e');
  P(x + 7 + b, y + 11, '#4ade80');
  P(x + 5, y + 14, '#38bdf8'); P(x + 11, y + 13, '#eab308');
  // 컨트롤 패널 & 조이스틱
  R(x + 2, y + 18, 14, 4, '#475569');
  P(x + 5, y + 18, '#ef4444'); P(x + 5, y + 19, '#94a3b8');
  P(x + 9, y + 19, '#3b82f6'); P(x + 12, y + 19, '#eab308');
  // 코인 투입구 2개
  R(x + 5, y + 23, 8, 4, '#0f172a');
  P(x + 7, y + 24, '#f59e0b'); P(x + 10, y + 24, '#f59e0b');
}

function drawTechBookshelf(x, y, t) {
  curFurnitureBoxes.push({ id: 'tech_bookshelf', x, y, w: 16, h: 25, name: T('📚 개발 서적 서가', '📚 Tech Bookshelf') });
  // 오크 책장 프레임 & 뒤판
  R(x, y, 16, 25, '#5c3a21');
  R(x + 1, y + 1, 14, 23, '#3e2716');
  // 3단 선반
  R(x + 1, y + 8, 14, 2, '#7a4e2d');
  R(x + 1, y + 16, 14, 2, '#7a4e2d');
  R(x + 1, y + 23, 14, 2, '#7a4e2d');
  // 1단 책들 (파란색, 빨간색, 금색 서적)
  R(x + 2, y + 2, 2, 6, '#2563eb');
  R(x + 5, y + 3, 3, 5, '#dc2626');
  R(x + 9, y + 2, 2, 6, '#d97706');
  R(x + 12, y + 4, 2, 4, '#16a34a');
  // 2단 책들 (보라색 매뉴얼, 시안 바인더)
  R(x + 2, y + 10, 4, 6, '#9333ea');
  R(x + 7, y + 11, 2, 5, '#0284c7');
  R(x + 10, y + 10, 3, 6, '#e11d48');
  // 3단 (미니 트로피 & 책)
  P(x + 3, y + 20, '#ffd700'); P(x + 3, y + 21, '#d97706');
  R(x + 7, y + 19, 6, 4, '#64748b');
}

function drawMonsteraPlant(x, y, t) {
  curFurnitureBoxes.push({ id: 'monstera_plant', x, y, w: 14, h: 20, name: T('🌿 몬스테라 관엽식물', '🌿 Monstera Plant') });
  // 테라코타 화분
  R(x + 2, y + 12, 10, 8, '#c2410c');
  R(x + 1, y + 11, 12, 2, '#ea580c');
  // 몬스테라 줄기
  R(x + 5, y + 6, 1, 6, '#15803d');
  R(x + 8, y + 4, 1, 8, '#166534');
  // 큼직한 구멍 뚫린 몬스테라 잎들
  R(x, y + 2, 5, 5, '#22c55e'); P(x + 2, y + 3, PAL.wall);
  R(x + 5, y - 2, 6, 6, '#16a34a'); P(x + 7, y, PAL.wall);
  R(x + 9, y + 2, 4, 5, '#4ade80');
}

function drawLoungeSofa(x, y, t) {
  curFurnitureBoxes.push({ id: 'lounge_sofa', x, y, w: 26, h: 18, name: T('🛋️ 패브릭 휴게 소파', '🛋️ Lounge Sofa') });
  // 소파 등받이
  R(x + 2, y, 22, 8, '#0e7490');
  R(x + 12, y, 1, 8, '#155e75');
  // 푹신한 좌판 방석
  R(x + 2, y + 8, 22, 6, '#06b6d4');
  R(x + 12, y + 8, 1, 6, '#0891b2');
  // 팔걸이 좌우
  R(x, y + 3, 3, 11, '#155e75');
  R(x + 23, y + 3, 3, 11, '#155e75');
  // 원목 소파 다리
  R(x + 3, y + 14, 2, 3, '#78350f');
  R(x + 21, y + 14, 2, 3, '#78350f');
  // 포인트 옐로우 쿠션
  R(x + 4, y + 4, 4, 4, '#eab308');
}

// ── 가구 클릭 상호작용 및 파티클 ──

let floatingHearts = [];

function spawnHearts(px, py) {
  for (let i = 0; i < 3; i++) {
    floatingHearts.push({
      x: px + (Math.random() * 8 - 4),
      y: py,
      birth: Date.now(),
    });
  }
}

function drawFloatingHearts(t) {
  if (floatingHearts.length === 0) return;
  const now = Date.now();
  floatingHearts = floatingHearts.filter((h) => now - h.birth < 1600);
  ctx.save();
  for (const h of floatingHearts) {
    const elapsed = (now - h.birth) / 1600;
    const y = (h.y - elapsed * 20) * U;
    const x = (h.x + Math.sin(elapsed * 7) * 2.5) * U;
    const alpha = 1.0 - elapsed;
    ctx.font = `${Math.round(11 * U)}px ${FONT}`;
    ctx.textAlign = 'center';
    ctx.globalAlpha = Math.max(0, alpha);
    ctx.fillText('💖', x, y);
  }
  ctx.restore();
}

function hitFurniture(cx, cy) {
  const mx = cx / U, my = cy / U;
  return curFurnitureBoxes.find((b) => mx >= b.x && mx < b.x + b.w && my >= b.y && my < b.y + b.h) || null;
}

function interactFurniture(b) {
  if (b.id === 'cat_tower') {
    playRetroSound('cat');
    spawnHearts(b.x + 8, b.y + 3);
    markQuest('office_cat');
  } else if (b.id === 'coffee_bar') {
    playRetroSound('coffee');
    spawnHearts(b.x + 11, b.y + 4);
    markQuest('coffee_break');
  } else if (b.id === 'arcade_cabinet') {
    playRetroSound('arcade');
    spawnHearts(b.x + 9, b.y + 8);
  } else if (b.id === 'vending_machine') {
    playRetroSound('vending');
    spawnHearts(b.x + 9, b.y + 12);
  } else if (b.id === 'tech_bookshelf') {
    playRetroSound('coin');
    addCoins(1, '책갈피 코인 발견');
    spawnHearts(b.x + 8, b.y + 8);
  } else if (b.id === 'monstera_plant') {
    playRetroSound('coin');
    spawnHearts(b.x + 7, b.y + 6);
  } else if (b.id === 'lounge_sofa') {
    playRetroSound('stretch');
    spawnHearts(b.x + 13, b.y + 6);
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

function getLightingKind() {
  const mode = store.get('light_mode', 'auto');
  if (mode === 'day' || mode === 'dusk' || mode === 'night' || mode === 'dawn') return mode;
  const d = new Date();
  return skyAtHour(d.getHours() + d.getMinutes() / 60).kind;
}

function skyAtHour(h) {
  if (h >= 7 && h < 17) return { top: '#7fc0ec', bottom: '#c4e6f8', kind: 'day' };
  if (h >= 17 && h < 20.5) return { top: '#7a5e9c', bottom: '#f2a46a', kind: 'dusk' };
  if (h >= 5.5 && h < 7) return { top: '#9fc6e8', bottom: '#f5c79c', kind: 'dawn' };
  return { top: '#141a38', bottom: '#26305c', kind: 'night' };
}

function skyAt(h) {
  const kind = getLightingKind();
  if (kind === 'night') return { top: '#141a38', bottom: '#26305c', kind: 'night' };
  if (kind === 'dusk') return { top: '#7a5e9c', bottom: '#f2a46a', kind: 'dusk' };
  if (kind === 'dawn') return { top: '#9fc6e8', bottom: '#f5c79c', kind: 'dawn' };
  return { top: '#7fc0ec', bottom: '#c4e6f8', kind: 'day' };
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
  const windowXs = [];
  for (let i = 0; i < n; i++) {
    const x = Math.round(left + ((right - left) / n) * (i + 0.5)) - 17;
    windowXs.push(x);
    drawWindow(x, 6, sky, t, i);
  }

  // 화이트보드: 창문 사이에 공간이 있으면 가운데 걸고, 창문이 1개면 창문 오른쪽에 배치
  if (n >= 2) {
    const wx = Math.round((windowXs[0] + 34 + windowXs[1]) / 2) - 11;
    drawWhiteboard(wx, 8);
    curWhiteboardBox = { x: wx, y: 8, w: 24, h: 18 };
  } else if (n === 1 && logicalW - hudW > windowXs[0] + 64) {
    const wx = windowXs[0] + 40;
    drawWhiteboard(wx, 8);
    curWhiteboardBox = { x: wx, y: 8, w: 24, h: 18 };
  } else {
    curWhiteboardBox = null;
  }

  drawPlant(pw + 30, WALL_H - 14);
  drawCooler(logicalW - 20, WALL_H - 26);

  // 구매한 벽면 가구 렌더링
  if (isFurnitureOwned('tech_bookshelf')) drawTechBookshelf(pw + 46, WALL_H - 27, t);
  if (isFurnitureOwned('monstera_plant')) drawMonsteraPlant(pw + 64, WALL_H - 20, t);
  if (isFurnitureOwned('coffee_bar')) drawCoffeeBar(logicalW - 44, WALL_H - 26, t);
  if (isFurnitureOwned('vending_machine')) drawVendingMachine(logicalW - 66, WALL_H - 30, t);
}

function drawWhiteboard(x, y) {
  // 알루미늄 프레임 & 보드 본체
  R(x - 1, y - 1, 24, 18, PAL.frameDark);
  R(x, y, 22, 16, '#f8fafc');
  // 보드 헤더 라인
  R(x + 2, y + 2, 8, 1, '#3b82f6');
  // 미니 할 일 체크리스트
  P(x + 3, y + 5, '#10b981'); R(x + 6, y + 5, 12, 1, '#94a3b8');   // 완료 항목
  P(x + 3, y + 8, '#f59e0b'); R(x + 6, y + 8, 10, 1, '#64748b');   // 진행 중 항목
  P(x + 3, y + 11, '#cbd5e1'); R(x + 6, y + 11, 8, 1, '#cbd5e1');  // 대기 항목
  // 하단 펜 받침대 & 펜·지우개
  R(x + 2, y + 15, 18, 1, '#94a3b8');
  P(x + 4, y + 14, '#ef4444'); P(x + 6, y + 14, '#3b82f6'); P(x + 8, y + 14, '#10b981');
  R(x + 13, y + 14, 3, 1, '#334155');
}

function drawWindow(x, y, sky, t, i) {
  R(x - 1, y - 1, 36, 26, PAL.frameDark);
  R(x, y, 34, 24, PAL.frame);
  R(x + 2, y + 2, 30, 10, sky.top);
  R(x + 2, y + 12, 30, 10, sky.bottom);

  g.save();
  g.beginPath(); g.rect(x + 2, y + 2, 30, 20); g.clip();
  if (sky.kind === 'night') {
    // 밤하늘: 여러 속도로 반짝이는 별들
    for (let k = 0; k < 6; k++) {
      if (tick(t, 600 + k * 110, 3) === 0) continue;
      P(x + 3 + (hash('s' + i + k) % 28), y + 3 + (hash('t' + k + i) % 17), '#f4f1d0');
    }
    // 초승달과 부드러운 달빛
    if (i === 0) {
      R(x + 23, y + 4, 4, 4, '#fff9d6');
      R(x + 25, y + 4, 2, 2, sky.top);
      P(x + 22, y + 5, '#fffde8');
    }
    // 가끔 지나가는 별똥별(유성) 애니메이션 (약 13초 주기)
    const starCycle = t % 13000;
    if (i === 0 && starCycle < 1100) {
      const prog = starCycle / 1100;
      const mx = Math.round(x + 3 + prog * 24);
      const my = Math.round(y + 3 + prog * 13);
      P(mx, my, '#ffffff');
      P(mx - 1, my - 1, '#e0e8ff');
      P(mx - 2, my - 2, 'rgba(255,255,255,0.4)');
    }
  } else if (sky.kind === 'dusk') {
    // 노을: 지는 주황빛 태양과 붉게 물든 구름
    if (i === 0) {
      R(x + 20, y + 10, 5, 4, '#ff7043');
      R(x + 21, y + 9, 3, 1, '#ffab91');
    }
    const cx = Math.round(((t / 320 + i * 19) % 48) - 8);
    R(x + 2 + cx, y + 7 + (i % 2) * 5, 8, 2, 'rgba(255,180,160,0.85)');
    R(x + 4 + cx, y + 6 + (i % 2) * 5, 4, 1, 'rgba(255,210,180,0.9)');
  } else if (sky.kind === 'dawn') {
    // 새벽/아침: 부드럽게 떠오르는 해와 파스텔빛 구름
    if (i === 0) {
      R(x + 6, y + 12, 5, 3, '#ffd54f');
      R(x + 7, y + 11, 3, 1, '#fff59d');
    }
    const cx = Math.round(((t / 280 + i * 19) % 48) - 8);
    R(x + 2 + cx, y + 7 + (i % 2) * 5, 8, 2, 'rgba(255,230,210,0.85)');
    R(x + 4 + cx, y + 6 + (i % 2) * 5, 4, 1, 'rgba(255,245,230,0.9)');
  } else {
    // 낮: 황금빛 태양과 흘러가는 구름
    if (i === 0) {
      R(x + 23, y + 4, 5, 5, '#ffe57f');
      R(x + 24, y + 3, 3, 1, '#fff59d');
      R(x + 24, y + 9, 3, 1, '#fff59d');
    }
    const cx = Math.round(((t / 260 + i * 19) % 48) - 8);
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

function drawTrashCan(tx, ty, ctx, t) {
  const tokens = ctx || 0;
  const pct = tokens / MAX_CTX_TOKENS;
  // 휴지통 본체 (폭 6, 높이 7)
  R(tx, ty, 6, 1, '#787a85');         // 상단 테두리(림)
  R(tx, ty + 1, 6, 6, '#565860');     // 본체
  R(tx + 1, ty + 1, 4, 1, '#3a3c42'); // 내부 음영
  R(tx + 1, ty + 6, 4, 1, '#44464d'); // 바닥

  if (pct >= 0.8) {
    // 80%+ Context: 쓰레기통이 꽉 차서 구겨진 종이가 넘치고 바닥에도 하나 떨어짐!
    R(tx + 1, ty - 2, 4, 3, '#ffffff');
    P(tx + 2, ty - 3, '#ffffff');
    P(tx + 4, ty - 2, '#d8d8d8');
    P(tx + 1, ty + 1, '#f0f0f0');
    P(tx + 3, ty + 2, '#ffffff');
    P(tx + 7, ty + 5, '#ffffff');       // 바닥에 떨어진 종이 뭉치
    P(tx + 7, ty + 6, '#d0d0d0');
  } else if (pct >= 0.5) {
    // 50%+: 종이 뭉치가 절반 정도 참
    R(tx + 1, ty + 1, 4, 2, '#f0f0f0');
    P(tx + 2, ty, '#ffffff');
    P(tx + 3, ty + 2, '#d8d8d8');
  } else if (pct >= 0.25) {
    // 25%+: 바닥에 조금 쌓임
    P(tx + 2, ty + 4, '#d8d8d8');
    P(tx + 3, ty + 4, '#ffffff');
  }
}

function drawDesk(d, t) {
  const s = d.s, a = actOf(s), L = looks(s.id);
  const x = d.x, y = d.y;

  R(x + 6, y + 51, 56, 2, 'rgba(0,0,0,0.18)');
  drawChair(x, y);
  if (a.pose !== 'away') drawPerson(x, y, L, a.pose, t, s);
  drawDeskBody(x, y);
  drawMonitor(x + 36, y + 18, a.screen, t, hash(s.id));
  R(x + 26, y + 34, 9, 1, PAL.keyboard);
  drawDeco(x, y, L.deco);
  drawTrashCan(x + 59, y + 43, s.ctx, t);

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
function idleActionOf(s, t) {
  // 대기 중 자율 행동: 24초 주기 (14초 커피 홀짝 -> 5초 으라차 기지개 -> 5초 휴식)
  const hashVal = s?.id ? hash(s.id) : 123;
  const phase = ((t + hashVal * 137) % 24000) / 1000;
  if (phase >= 14 && phase < 19) return 'stretch';
  return 'sip';
}

function drawPerson(x, y, L, pose, t, s) {
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

  const isStretching = pose === 'idle' && idleActionOf(s, t) === 'stretch';
  drawHead(hx, hy, L, pose, t, isStretching, s);
  drawArms(bx, by, L, pose, t, isStretching);
}

function drawHead(hx, hy, L, pose, t, isStretching = false, s = null) {
  R(hx, hy, 8, 8, L.skin);
  R(hx, hy - 1, 8, 3, L.hair);
  R(hx - 1, hy, 3, 6, L.hair);
  if (L.hairStyle === 1) R(hx + 5, hy + 2, 3, 1, L.hair);   // 앞머리
  if (L.hairStyle === 2) R(hx - 1, hy + 6, 2, 3, L.hair);   // 긴 머리

  // 📊 컨텍스트 포화 시각 효과 (땀방울 & 얼음주머니)
  if (s && s.ctx) {
    const ctxPct = s.ctx / MAX_CTX_TOKENS;
    if (ctxPct >= 0.85) {
      const swTick = tick(t, 600, 2);
      P(hx + 8, hy + 2 + swTick, '#38bdf8');
      if (swTick === 1) P(hx + 8, hy + 4, '#0284c7');
    }
    if (ctxPct >= 0.90) {
      R(hx + 2, hy - 4, 4, 3, '#a5f3fc');
      P(hx + 3, hy - 5, '#06b6d4');
      P(hx + 4, hy - 5, '#0891b2');
    }
  }

  if (isStretching) {
    // 기지개 켤 때 활짝 웃는 눈 (^ ^)
    P(hx + 5, hy + 3, '#2a1d17'); P(hx + 7, hy + 3, '#2a1d17');
    P(hx + 6, hy + 2, '#2a1d17');
  } else {
    const closed = pose === 'sleep' || (t + L.blink) % 3200 < 130;
    if (closed) R(hx + 5, hy + 4, 2, 1, '#3a2a22');
    else R(hx + 6, hy + 3, 1, 2, '#2a1d17');
  }

  if (L.glasses) {
    R(hx + 5, hy + 2, 3, 1, '#2d2d38');
    R(hx + 5, hy + 5, 3, 1, '#2d2d38');
    P(hx + 5, hy + 3, '#2d2d38'); P(hx + 5, hy + 4, '#2d2d38');
  }
  P(hx + 6, hy + 6, (pose === 'raise' || isStretching) ? '#b0473b' : shade(L.skin, 0.8));   // 입
}

function drawArms(bx, by, L, pose, t, isStretching = false) {
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
      if (isStretching) {
        // \o/ 으라차 기지개: 양팔을 번쩍 위로 뻗음
        const b = tick(t, 350) ? 1 : 0;
        R(bx + 3, by - 6 + b, 2, 9, sleeve);  // 왼팔
        R(bx + 3, by - 8 + b, 2, 2, skin);    // 왼손
        R(bx + 8, by - 6 + b, 2, 9, sleeve);  // 오른팔
        R(bx + 8, by - 8 + b, 2, 2, skin);    // 오른손
      } else {
        // ☕ 커피 머그잔 홀짝이기 & 모락모락 김
        R(bx + 6, by + 4, 4, 2, sleeve);
        R(bx + 10, by + 1, 3, 4, '#f2f2f2');
        R(bx + 10, by + 1, 3, 1, '#6b3e22');
        P(bx + 13, by + 2, '#f2f2f2'); P(bx + 13, by + 3, '#f2f2f2');
        const ph = (t / 380) | 0;
        for (let i = 0; i < 2; i++) P(bx + 11 + ((ph + i) % 2), by - 1 - ((ph + i * 2) % 4), 'rgba(255,255,255,0.55)');   // 김
      }
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
  drawAmbientLighting(t);
  drawFloatingHearts(t);
  drawMeetingBubbles(t);
  for (const d of desks) drawDeskBubble(d, t);   // 말풍선은 맨 위에
}

function drawAmbientLighting(t) {
  const kind = getLightingKind();
  if (kind === 'day') return; // 주간은 자연광

  const w = canvas.width, h = canvas.height;
  ctx.save();

  if (kind === 'dusk') {
    // 🌅 노을: 따스한 주황빛 감성 틴트
    ctx.fillStyle = 'rgba(255, 120, 40, 0.08)';
    ctx.fillRect(0, 0, w, h);
  } else if (kind === 'night') {
    // 🌙 야간/야근 모드: 전체적으로 차분한 어두운 앰비언트 필터
    // 일하고 있는 책상과 모니터에만 따스하고 집중도 높은 원뿔 스포트라이트 조명
    ctx.fillStyle = 'rgba(10, 8, 24, 0.42)';
    ctx.fillRect(0, 0, w, h);

    // 모니터 & 책상 스포트라이트 투과
    ctx.globalCompositeOperation = 'destination-out';
    for (const d of desks) {
      const a = actOf(d.s);
      const isWorking = a.busy || a.wait;
      const spotX = (d.x + 36) * U;
      const spotY = (d.y + 36) * U;
      const radius = (isWorking ? 56 : 38) * U;
      const grad = ctx.createRadialGradient(spotX, spotY, 4, spotX, spotY, radius);
      grad.addColorStop(0, isWorking ? 'rgba(0, 0, 0, 0.78)' : 'rgba(0, 0, 0, 0.45)');
      grad.addColorStop(0.5, isWorking ? 'rgba(0, 0, 0, 0.40)' : 'rgba(0, 0, 0, 0.18)');
      grad.addColorStop(1, 'rgba(0, 0, 0, 0)');
      ctx.fillStyle = grad;
      ctx.beginPath();
      ctx.arc(spotX, spotY, radius, 0, Math.PI * 2);
      ctx.fill();
    }
    if (isFurnitureOwned('arcade_cabinet')) {
      const ax = 15 * U, ay = (WALL_H + 16) * U, ar = 32 * U;
      const agrad = ctx.createRadialGradient(ax, ay, 2, ax, ay, ar);
      agrad.addColorStop(0, 'rgba(0,0,0,0.6)');
      agrad.addColorStop(1, 'rgba(0,0,0,0)');
      ctx.fillStyle = agrad;
      ctx.beginPath();
      ctx.arc(ax, ay, ar, 0, Math.PI * 2);
      ctx.fill();
    }
  }

  ctx.restore();
}

function drawZoneLabel(z) {
  ctx.font = `700 ${fontSmall}px ${FONT}`;
  ctx.textAlign = 'left';
  ctx.textBaseline = 'middle';
  const text = T(`📁 ${teamLabel(z.team)}  ·  ${z.count}명`, `📁 ${teamLabel(z.team)}  ·  ${z.count}`);
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
  if (currentDevMeme && t < devMemeExpires && s.id === (resolvePetSession()?.id || visibleSessions()[0]?.id)) {
    text = `💡 ${currentDevMeme}`;
  } else if (a.bubble === 'thought') text += '.'.repeat(1 + tick(t, 450, 3));
  else if (s.act === 'idle' && (!s.text || s.text.trim() === '')) {
    if (s.ctx && (s.ctx / MAX_CTX_TOKENS) >= 0.85) {
      const cCycle = ((t + hash(s.id) * 311) % 18000) / 1000;
      if (cCycle < 6) {
        text = T('💭 😵 뇌 용량 포화! /compact 필요', '💭 😵 Context full! /compact needed');
      }
    }
    // 대기 중 랜덤 유머/생각 말풍선 (30초마다 7초간 등장)
    const thoughts = [
      '커피 한잔의 여유 ☕',
      '기지개 쭉- 으라차! ✨',
      '버그 없는 세상에서 살고 싶다 🌟',
      '다음 지시를 기다리는 중 🚀',
      '테스트 코드 통과했을까? 🧪',
      '이번 주말엔 뭐 하지? 🎮',
      '코드 리팩토링 각이다 🧹',
    ];
    const hashVal = hash(s.id);
    const cycle = ((t + hashVal * 251) % 30000) / 1000;
    if (cycle < 7) {
      const idx = Math.floor((t + hashVal * 251) / 30000) % thoughts.length;
      text = `💭 ${thoughts[idx]}`;
    }
  }
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
let windowHidden = false;   // PC 앱이 알림 영역으로 숨었을 때(true)는 캔버스를 그리지 않는다
function frame(ts) {
  requestAnimationFrame(frame);
  if (windowHidden || document.visibilityState === 'hidden') return;
  if (ts - lastFrame < 33) return;   // 약 30fps면 충분
  lastFrame = ts;

  if (WIDGET_WINDOW) {
    drawPetFrame(ts);
    return;
  }
  if (DEMO && petOn()) drawPetFrame(ts);
  if (widgetOn()) return;   // 목록 보기일 때는 사무실 캔버스를 그리지 않는다

  ensureLayout();
  syncDesks();

  curFurnitureBoxes = [];
  drawFloor();
  drawWall(ts);
  // 구매한 바닥/휴게 가구 렌더링
  if (isFurnitureOwned('arcade_cabinet')) drawArcadeCabinet(6, WALL_H + 4, ts);
  if (isFurnitureOwned('lounge_sofa')) drawLoungeSofa(6, WALL_H + 36, ts);
  if (isFurnitureOwned('cat_tower')) drawCatTower(logicalW - 22, WALL_H + 4, ts);

  // 🤝 회의실 테이블: 2개 이상 세션이 있을 때 또는 넓은 공간일 때 렌더링
  if (data.sessions && data.sessions.length >= 2) {
    const meetX = Math.max(36, logicalW - 58);
    const meetY = WALL_H + 34;
    drawMeetingTable(meetX, meetY, ts);
  }

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
  if (!(ms > 0)) return T('방금', 'just now');
  const sec = Math.floor(ms / 1000);
  if (sec < 10) return T('방금', 'just now');
  if (sec < 60) return T(`${sec}초`, `${sec}s`);
  const m = Math.floor(sec / 60);
  if (m < 60) return T(`${m}분`, `${m}m`);
  const h = Math.floor(m / 60);
  if (h >= 24) return T(`${Math.floor(h / 24)}일`, `${Math.floor(h / 24)}d`);
  return m % 60 ? T(`${h}시간 ${m % 60}분`, `${h}h ${m % 60}m`) : T(`${h}시간`, `${h}h`);
}

/** "3분 전" / "3m ago" — 영어는 "just now ago" 가 되지 않게. */
function fmtAgo(ms) {
  const d = fmtDur(ms);
  return LANG === 'en' ? (ms >= 10000 ? `${d} ago` : d) : `${d} 전`;
}

function fmtTime(ms) {
  const d = new Date(ms);
  return [d.getHours(), d.getMinutes(), d.getSeconds()].map((v) => String(v).padStart(2, '0')).join(':');
}

function entryName(e) {
  if (!e) return '-';
  if (e === 'claude-vscode') return 'VS Code';
  if (e === 'cursor') return 'Cursor';
  if (e === 'windsurf') return 'Windsurf';
  if (e === 'aider') return 'Aider CLI';
  if (e === 'roo' || e === 'roo-code') return 'Roo Code';
  if (e === 'cli') return T('터미널', 'Terminal');
  if (e.includes('desktop')) return T('데스크톱 앱', 'Desktop app');
  if (e.startsWith('sdk')) return 'SDK';
  return e;
}

// ───────────────────────────── HUD / 툴팁 / 상세 패널 ─────────────────────────────

const $ = (id) => document.getElementById(id);
const summaryEl = $('summary'), emptyEl = $('empty'), tooltipEl = $('tooltip'), panelEl = $('panel');
const btnAway = $('btnAway'), btnPin = $('btnPin'), btnHook = $('btnHook'), btnSettings = $('btnSettings'), btnPet = $('btnPet'), btnWidget = $('btnWidget'), btnToday = $('btnToday'), btnAchieve = $('btnAchieve');
const btnShop = $('btnShop'), btnCoins = $('hudCoins')?.parentElement, shopModalEl = $('shopModal'), shopCloseBtn = $('shopClose'), shopGridEl = $('shopGrid');
const btnBroadcast = $('btnBroadcast'), btnNightShift = $('btnNightShift');
const broadcastModalEl = $('broadcastModal'), bcCloseBtn = $('bcClose'), bcSessionListEl = $('bcSessionList'), bcPromptInput = $('bcPromptInput'), bcSendBtn = $('bcSendBtn'), bcStatusMsg = $('bcStatusMsg');
const nightShiftModalEl = $('nightShiftModal'), nsCloseBtn = $('nsClose'), nsToggleBtn = $('nsToggleBtn'), nsReportContent = $('nsReportContent'), nsCopyBtn = $('nsCopyBtn');
const settingsEl = $('settings'), settingsBody = $('sBody'), drawerTab = $('drawerTab');

function refreshHud() {
  if (WIDGET_WINDOW) {
    document.body.style.setProperty('--hud-h', '0px');
    return;
  }
  updateCoinDisplay();
  let busy = 0, wait = 0, rest = 0, away = 0;
  for (const s of data.sessions) {
    const a = actOf(s);
    if (s.act === 'away') away++;
    else if (a.wait) wait++;
    else if (a.busy) busy++;
    else rest++;
  }
  const parts = [T(`👥 ${data.sessions.length - away}명`, `👥 ${data.sessions.length - away}`)];
  if (REMOTE) {
    parts.unshift(remoteLabel());   // 휴대폰 보기: PC와 이어져 있는지 맨 앞에
    if (remoteState?.away) parts.splice(1, 0, T('🏃 자리 비움', '🏃 Away'));   // 지시·승인을 보낼 수 있다
    document.body.classList.toggle('stale', remoteStale());   // PC가 꺼졌거나 소식이 없으면 사무실을 흐리게(멈춘 모습임을 알게)
  }
  if (busy) parts.push(T(`💻 작업 ${busy}`, `💻 Working ${busy}`));
  if (wait) parts.push(T(`✋ 확인 필요 ${wait}`, `✋ Needs you ${wait}`));
  if (rest) parts.push(T(`☕ 휴식 ${rest}`, `☕ Resting ${rest}`));
  if (away) parts.push(T(`🚪 퇴근 ${away}`, `🚪 Gone home ${away}`));
  // 숫자는 숨긴 직원까지 센다(숨긴 직원이 승인을 기다려도 ✋ 개수·창 제목으로는 알 수 있게)
  const shown = visibleSessions().length;
  const cut = data.sessions.filter((s) => showAway || s.act !== 'away').length - shown;
  if (cut > 0) parts.push(T(`🙈 숨김 ${cut}`, `🙈 Hidden ${cut}`));
  summaryEl.textContent = parts.join('  ·  ');
  // 좁은 화면에서는 상단 표시가 맨 위 막대가 된다(office.css) — 그 높이만큼 사무실을 내려 간판을 덮지 않게
  document.body.style.setProperty('--hud-h', innerWidth <= 600 ? `${hudEl.offsetHeight}px` : '0px');
  refreshEmpty(shown);
  if (widgetOn()) renderWidget();
  document.title = wait ? `(✋${wait}) ${officeTitle()}` : officeTitle();   // 앱 창 제목·작업표시줄에도 반영됨
}

// ───────────────────────────── 작게 보기(위젯) ─────────────────────────────
// 그림 대신 직원·상태를 한 줄씩. PC 앱은 창도 작게(앱이 크기·위치를 따로 기억), 휴대폰은 목록으로만.
// PC에서는 앱(app.json)이 켜짐 여부의 원본 — 다음에 켤 때 처음부터 작은 창으로 뜨게.
const wlistEl = $('wlist');
const LOCAL_WIDGET = REMOTE || DEMO;   // 휴대폰·견본은 창 크기를 안 바꾸고 목록으로만(이 기기에 저장)
let listView = LOCAL_WIDGET && store.get('listView', false);
const widgetOn = () => (LOCAL_WIDGET ? listView : !!appSettings?.widget);

function setWidget(on) {
  if (on) markQuest('try_widget');
  if (LOCAL_WIDGET) { listView = on; store.set('listView', on); applyWidget(); }
  else send({ cmd: 'widget', value: on });   // 앱이 창 크기를 바꾸고 {type:"app", widget} 으로 알려 준다 → applyWidget
}

function applyWidget() {
  const on = widgetOn();
  document.body.classList.toggle('widget', on);
  wlistEl.hidden = !on;
  btnWidget.classList.toggle('on', on);
  if (on) { selectedId = null; renderPanel(); setSettingsOpen(false); renderWidget(); }
  else { lastWidgetH = 0; layoutKey = ''; if (!LOCAL_WIDGET) send({ cmd: 'topmost', value: pinned }); }
}

// 기다리는 직원 → 일하는 직원 → 쉬는 직원 → 퇴근 순, 같은 무리 안에서는 최근 순
const WIDGET_RANK = (s) => (s.act === 'away' ? 3 : actOf(s).wait ? 0 : actOf(s).busy ? 1 : 2);
let lastWidgetH = 0;

function renderWidget() {
  const list = visibleSessions().slice().sort((a, b) => WIDGET_RANK(a) - WIDGET_RANK(b) || (b.last || 0) - (a.last || 0));
  const wait = data.sessions.filter((s) => s.act !== 'away' && actOf(s).wait).length;
  const head = elem('div', 'whd');
  head.append(elem('b', '', officeTitle()), elem('span', '', wait ? `✋ ${wait}` : `👥 ${list.filter((s) => s.act !== 'away').length}`),
    elem('span', 'sp'));
  if (REMOTE) head.append(elem('span', '', remoteLabel()));
  const big = elem('button', '', T('⤢ 크게', '⤢ Expand'));
  big.title = T('사무실 그림으로 보기', 'Back to the office picture');
  big.addEventListener('click', () => setWidget(false));
  head.append(big);

  const rows = list.map((s) => {
    const a = actOf(s);
    const row = elem('div', 'wrow' + (a.wait ? ' wait' : '') + (s.act === 'away' ? ' away' : ''));
    const tx = elem('div', 'tx', s.text || a.label);
    tx.style.color = a.tone;
    row.append(elem('div', 'ic', s.icon), elem('div', 'nm', displayName(s)),
      elem('div', 'tm', s.act === 'away' ? T('퇴근', 'Gone home') : fmtDur(now() - (s.since || s.last)) + (a.wait ? T(' 기다림', ' wait') : '')), tx);
    row.title = T(`${s.team} — 누르면 크게 보며 이 직원 열기`, `${s.team} — click to expand and open this employee`);
    row.addEventListener('click', () => { setWidget(false); setTimeout(() => selectSession(s.id), REMOTE ? 0 : 300); });
    return row;
  });
  wlistEl.replaceChildren(head, ...(rows.length ? rows : [elem('div', 'wempty', $('emptyBig').textContent || T('직원이 없어요', 'No employees'))]));

  // 앱 창 높이를 목록에 맞춘다(바뀔 때만)
  const h = Math.ceil(wlistEl.getBoundingClientRect().height);
  if (!LOCAL_WIDGET && h !== lastWidgetH) { lastWidgetH = h; send({ cmd: 'widget-size', height: h }); }
}

// ───────────────────────────── 바탕화면 미니위젯 (Desktop MiniWidget) ─────────────────────────────
// 대표 직원 한 명을 바탕화면 구석(우측 하단 기본, 드래그로 이동)에 작은 책상·컴퓨터와 함께 꺼내 두고,
// 상태(타이핑·생각·손들기·졸음)를 보거나, 바로 지시를 던지거나, 승인·거절하거나, 다른 직원으로 바꿀 수 있다.

const petEl = $('pet');
const LOCAL_PET = REMOTE || DEMO;
let demoPetOpen = LOCAL_PET && (params.get('select') === 'pet' || store.get('petOpen', false));
let demoPetSessionId = store.get('petSessionId', '') || '';
let demoPetCompact = store.get('petCompact', false);
let demoPetNoBg = store.get('petNoBg', false);

const petOn = () => (WIDGET_WINDOW ? true : LOCAL_PET ? demoPetOpen : !!appSettings?.pet);
const petSessionPref = () => (LOCAL_PET ? demoPetSessionId : (appSettings?.petSessionId || ''));
const petCompactOn = () => (LOCAL_PET ? demoPetCompact : !!appSettings?.petCompact);
const petNoBgOn = () => (LOCAL_PET ? demoPetNoBg : !!appSettings?.petNoBg);

function setPet(on) {
  if (on) markQuest('try_pet');
  if (LOCAL_PET) {
    demoPetOpen = !!on;
    store.set('petOpen', demoPetOpen);
    applyPetState();
    refreshSettings();
  } else {
    send({ cmd: 'pet', value: !!on });
  }
}

function setPetSession(sid) {
  const v = sid || '';
  if (LOCAL_PET) {
    demoPetSessionId = v;
    store.set('petSessionId', v);
    if (petOn()) renderPet();
    refreshSettings();
    if (selectedId) renderPanel();
  } else {
    send({ cmd: 'pet-session', id: v });
  }
}

function setPetCompact(v) {
  if (LOCAL_PET) {
    demoPetCompact = !!v;
    store.set('petCompact', demoPetCompact);
    if (petOn()) renderPet();
    refreshSettings();
  } else {
    send({ cmd: 'pet-compact', value: !!v });
  }
}

function setPetNoBg(v) {
  if (LOCAL_PET) {
    demoPetNoBg = !!v;
    store.set('petNoBg', demoPetNoBg);
    if (petOn()) renderPet();
    refreshSettings();
  } else {
    send({ cmd: 'pet-nobg', value: !!v });
  }
}

/** 미니위젯에 표시할 대표 직원: 지정한 세션이 있으면 그 직원, 없거나 사라졌으면 확인 필요(✋) → 작업 중 → 최근 활동 순으로 고른다. */
function resolvePetSession() {
  const list = data.sessions || [];
  if (!list.length) return null;
  const pref = petSessionPref();
  if (pref) {
    const found = list.find((s) => s.id === pref);
    if (found) return found;
  }
  const active = list.filter((s) => s.act !== 'away');
  const pool = active.length ? active : list;
  return pool.slice().sort((a, b) => WIDGET_RANK(a) - WIDGET_RANK(b) || (b.last || 0) - (a.last || 0))[0] || list[0];
}

function applyPetState() {
  if (btnPet) {
    btnPet.hidden = REMOTE;
    btnPet.classList.toggle('on', petOn());
  }
  if (WIDGET_WINDOW) {
    document.body.classList.add('widget-window', 'pet-window');
    petEl.hidden = false;
    renderPet();
    return;
  }
  if (DEMO) {
    const show = petOn();
    document.body.classList.toggle('pet-float', show);
    petEl.hidden = !show;
    if (show) renderPet();
  }
}

let petDomBuilt = false;
let petSelEl = null, petBgBtn = null, petFoldBtn = null, petStopBtn = null, petAlertEl = null, petAlertTargetId = '';
let petStageEl = null, petCanvasEl = null, petCtx = null;
let petBarNm = null, petBarSt = null, petAsksEl = null, petFeedEl = null, petFeedHd = null, petFeedTx = null;
let petCmdWrap = null, petInputEl = null, petSendBtn = null, petNoteEl = null, petCancelBtn = null, petWakeBtn = null, petGuideBtn = null, petEmptyEl = null, petIslandEl = null;
const petArt = document.createElement('canvas');
const petAskEls = new Map();
const petAnswered = new Map();
let petNote = null;   // { sid, at, text }
let lastPetH = 0;

function ensurePetDom() {
  if (petDomBuilt) return;
  petDomBuilt = true;

  const head = elem('div', 'pet-head');
  const badge = elem('span', 'pet-badge', '🪟');
  badge.title = T('드래그해 위치 이동', 'Drag to move');
  petSelEl = elem('select', 'pet-sel');
  petSelEl.title = T('대표 직원 선택(바탕화면 미니위젯에 표시할 세션)', 'Choose representative employee');
  petSelEl.addEventListener('change', () => setPetSession(petSelEl.value));

  petBgBtn = elem('button', 'pet-ibtn', '🖼️');
  petBgBtn.addEventListener('click', () => setPetNoBg(!petNoBgOn()));

  petFoldBtn = elem('button', 'pet-ibtn', '💬');
  petFoldBtn.addEventListener('click', () => setPetCompact(!petCompactOn()));

  petStopBtn = elem('button', 'pet-ibtn btn-stop', '🛑');
  petStopBtn.title = T('현재 세션에 긴급 중단(Ctrl+C) 신호를 보냅니다', 'Send emergency stop (Ctrl+C) to this session');
  petStopBtn.addEventListener('click', () => {
    const s = resolvePetSession();
    if (s) {
      stopSession(s.id);
      petStopBtn.style.opacity = '0.5';
      setTimeout(() => { if (petStopBtn) petStopBtn.style.opacity = '1'; }, 1500);
      markQuest('emergency_stop');
    }
  });
  petStopBtn.hidden = true;

  const focusBtn = elem('button', 'pet-ibtn', '🖥️');
  focusBtn.title = T('이 세션이 열려 있는 VS Code·터미널 창으로 이동', "Bring this session's VS Code or terminal window to the front");
  focusBtn.addEventListener('click', () => {
    const s = resolvePetSession();
    if (s) jumpToConsole(s.id, s);
  });

  const officeBtn = elem('button', 'pet-ibtn', '🏢');
  officeBtn.title = T('메인 사무실 창에서 이 직원 열기', 'Open this employee in the main office window');
  officeBtn.addEventListener('click', () => {
    const s = resolvePetSession();
    if (WIDGET_WINDOW) send({ cmd: 'pet-open-office', id: s?.id || '' });
    else if (s) selectSession(s.id);
  });

  const petWalkBtn = elem('button', 'pet-ibtn', '🐾');
  petWalkBtn.title = T('시메지 산책 모드 (직원이 화면을 걸어 다닙니다)', 'Shimeji Walk Mode');
  petWalkBtn.addEventListener('click', togglePetWalk);

  const petIslandBtn = elem('button', 'pet-ibtn', '🏝️');
  petIslandBtn.title = T('초슬림 다이내믹 아일랜드 모드', 'Ultra-slim Dynamic Island Mode');
  petIslandBtn.addEventListener('click', togglePetIsland);

  const petMemeBtn = elem('button', 'pet-ibtn', '💡');
  petMemeBtn.title = T('개발자 유머 & 명언 보기 (설정에서 1시간마다 자동 띄우기 가능)', 'Developer Memes & Quotes (auto icebreaker configurable in settings)');
  petMemeBtn.addEventListener('click', () => triggerDevMeme());

  const closeBtn = elem('button', 'pet-ibtn', '✕');
  closeBtn.title = T('바탕화면 미니위젯 닫기', 'Close Desktop MiniWidget');
  closeBtn.addEventListener('click', () => setPet(false));

  head.append(badge, petSelEl, petBgBtn, petFoldBtn, petWalkBtn, petIslandBtn, petMemeBtn, petStopBtn, focusBtn, officeBtn, closeBtn);

  petAlertEl = elem('button', 'pet-alert');
  petAlertEl.hidden = true;
  petAlertEl.title = T('클릭하면 확인을 기다리는 직원으로 바로 전환합니다', 'Click to switch to the waiting employee');
  petAlertEl.addEventListener('click', () => {
    if (petAlertTargetId) setPetSession(petAlertTargetId);
  });

  petStageEl = elem('div', 'pet-stage');
  petCanvasEl = document.createElement('canvas');
  petCanvasEl.id = 'petCanvas';
  petCtx = petCanvasEl.getContext('2d');
  const bar = elem('div', 'pet-bar');
  bar.title = T('클릭: 메인 사무실 창에서 자세히 보기', 'Click: open details in the main office');
  petBarNm = elem('span', 'nm');
  petBarSt = elem('span', 'st');
  bar.append(petBarNm, petBarSt);
  bar.addEventListener('click', () => {
    const s = resolvePetSession();
    if (WIDGET_WINDOW) send({ cmd: 'pet-open-office', id: s?.id || '' });
    else if (s) selectSession(s.id);
  });
  petStageEl.append(petCanvasEl, bar);

  // 헤더나 구석 방 그림(캔버스)을 잡고 끌면 창 이동
  const startDrag = (e) => {
    if (e.button !== 0) return;
    if (e.target instanceof Element && e.target.closest('select, button, input, textarea, .pet-bar')) return;
    if (WIDGET_WINDOW) send({ cmd: 'pet-drag' });
  };
  head.addEventListener('mousedown', startDrag);
  petStageEl.addEventListener('mousedown', startDrag);

  petEmptyEl = elem('div', 'pet-empty');
  petAsksEl = elem('div', 'pet-asks');

  petFeedEl = elem('div', 'pet-feed');
  petFeedHd = elem('div', 'hd');
  petFeedTx = elem('div', 'tx');
  petFeedEl.append(petFeedHd, petFeedTx);

  petCmdWrap = elem('div', 'pet-cmd');
  const irow = elem('div', 'pet-input-row');
  petInputEl = elem('textarea', 'field');
  petInputEl.rows = 1;
  petInputEl.maxLength = 1000;
  petInputEl.placeholder = T('이 직원에게 보낼 지시', 'Instruction for this employee (Enter to send)');
  petSendBtn = elem('button', 'pet-send', T('보내기', 'Send'));
  petSendBtn.addEventListener('click', sendPetSay);
  petInputEl.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      sendPetSay();
    }
  });
  const petVoiceBtn = elem('button', 'mini voice-btn', '🎙️');
  petVoiceBtn.type = 'button';
  petVoiceBtn.title = T('음성으로 지시하기 (Voice Prompt)', 'Voice-to-Prompt');
  setupVoicePrompt(petVoiceBtn, petInputEl);
  irow.append(petInputEl, petVoiceBtn, petSendBtn);
  const noteRow = elem('div', 'pet-note-row');
  petNoteEl = elem('div', 'pet-note');
  petCancelBtn = elem('button', 'cancel-q', T('취소', 'Cancel'));
  petCancelBtn.title = T('아직 전달되지 않은 대기 중 지시를 취소합니다', 'Cancel the queued instruction');
  petCancelBtn.hidden = true;
  petCancelBtn.addEventListener('click', () => {
    const s = resolvePetSession();
    if (s) cancelQueuedSay(s.id);
  });
  petWakeBtn = elem('button', 'wake-term', T('🖥️ 터미널로', '🖥️ Terminal'));
  petWakeBtn.title = T('터미널 창으로 이동 후 Ctrl+V로 붙여넣고 Enter를 누르세요 (지시가 복사됨)', 'Focus terminal & press Ctrl+V then Enter (copied)');
  petWakeBtn.hidden = true;
  petWakeBtn.addEventListener('click', () => {
    const s = resolvePetSession();
    if (s) onWakeTerminal(s.id);
  });
  petGuideBtn = elem('button', 'guide-link', T('ℹ️ 안내', 'ℹ️ Guide'));
  petGuideBtn.title = T('세션 최초 연결 방법 안내 보기', 'View first-time connection guide');
  petGuideBtn.hidden = true;
  petGuideBtn.addEventListener('click', () => {
    const s = resolvePetSession();
    if (s) openConnectGuide(s.id);
  });
  noteRow.append(petNoteEl, petCancelBtn, petWakeBtn, petGuideBtn);
  petCmdWrap.append(irow, noteRow);

  petIslandEl = elem('div', 'island-wrap');
  petIslandEl.hidden = true;
  petEl.replaceChildren(head, petAlertEl, petStageEl, petEmptyEl, petAsksEl, petFeedEl, petCmdWrap, petIslandEl);
}

// ───────────────────────────── 📱 모바일 & 원격 제어: Diff 아코디언 & 음성 지시 ─────────────────────────────


// ───────────────────────────── 💬 개발자 유머 명언 & 이스터에그 ─────────────────────────────
const DEV_MEMES = [
  { ko: "내 컴퓨터에서는 잘 되는데요? 🤷‍♂️", en: "Works on my machine! 🤷‍♂️" },
  { ko: "버그가 아니라 문서화되지 않은 기능입니다. ✨", en: "It's not a bug, it's an undocumented feature. ✨" },
  { ko: "세상에는 10종류의 사람이 있습니다. 이진수를 아는 자와 모르는 자. 🤓", en: "10 types of people: those who understand binary, and those who don't. 🤓" },
  { ko: "Git commit -m '진짜_최종_진짜최종_수정' 📦", en: "Git commit -m 'final_v2_final_FINAL' 📦" },
  { ko: "커피를 코드로 변환하는 유기물 프로세서 가동 중 ☕", en: "Converting coffee into clean code... ☕" },
  { ko: "TODO: 나중에 고치기 (작성일: 3년 전) ⏳", en: "TODO: fix this later (written 3 years ago) ⏳" },
  { ko: "99개의 버그를 잡았더니, 127개의 버그가 새로 생겼습니다. 🐛", en: "99 bugs in the code, fix one, 127 bugs in the code. 🐛" },
  { ko: "배포는 금요일 오후 5시에 하는 게 제맛이지! 🚀", en: "Nothing beats deploying to production at 5 PM on Friday! 🚀" },
  { ko: "StackOverflow가 다운되면 전 세계 개발 생산성은 0이 된다. 📉", en: "If StackOverflow goes down, global dev productivity drops to 0. 📉" },
  { ko: "인생은 짧고, 빌드 시간은 깁니다. ⏳", en: "Life is short, but build times are eternal. ⏳" },
  { ko: "주석이 필요 없는 코드가 최고의 코드라고 누가 그랬더라...? 🤔", en: "Who said self-documenting code never needs comments? 🤔" },
  { ko: "CSS 센터 정렬하다가 3시간이 순식간에 증발했습니다. 🎨", en: "Spent 3 hours trying to center a div with CSS. 🎨" },
  { ko: "정규표현식으로 문제를 해결하려다 두 개의 문제가 생겼다. 🌀", en: "Used regex to solve a problem. Now I have two problems. 🌀" },
  { ko: "캐시를 비웠더니 모든 게 해결되었습니다. 마법인가? 🪄", en: "Clearing cache solved everything. Is this magic? 🪄" },
  { ko: "세미콜론 하나 때문에 3시간째 야근 중... 😅", en: "Overtime due to a missing semicolon... 😅" },
  { ko: "다크 모드가 없으면 코딩을 할 수 없는 몸이 되어버렸다. 🕶️", en: "Can't code without Dark Mode anymore. 🕶️" },
  { ko: "테스트 코드는 안 짰지만 마음속으로 간절히 기도했습니다. 🙏", en: "No unit tests, just thoughts and prayers. 🙏" },
  { ko: "프로덕션 DB에 직접 DROP TABLE을 날릴 뻔했다... 😱", en: "Almost ran DROP TABLE directly in production... 😱" },
  { ko: "레거시 코드를 열어 욕했더니 과거의 내가 쓴 코드였다. 🤦", en: "Hated legacy code only to realize I wrote it last year. 🤦" },
  { ko: "새 프레임워크가 또 나왔다고요? 어제 배운 건 어쩌고! 🤯", en: "A new JS framework came out? But I just learned one yesterday! 🤯" },
  { ko: "안 돌아가는 코드: 왜 안 돌아가지? / 돌아가는 코드: 왜 돌아가지? 🤖", en: "Broken code: Why doesn't it work? / Working code: Why DOES it work? 🤖" },
  { ko: "컴파일 성공의 희열은 10초, 런타임 에러의 고통은 10시간. ⚡", en: "Compile joy lasts 10s; runtime error pain lasts 10 hours. ⚡" },
  { ko: "인간은 실수하고, 컴퓨터는 그 실수를 초당 수십억 번 반복한다. 💻", en: "To err is human, to repeat it billions of times per sec is a computer. 💻" },
  { ko: "이 코드는 오직 나와 신만이 이해할 수 있었지만, 지금은 신만이 안다. 🕊️", en: "Only God and I understood this code. Now only God knows. 🕊️" },
  { ko: "완벽한 코드란 더 이상 지울 것이 없을 때 완성된다. ✂️", en: "Perfection is achieved not when there is nothing to add, but nothing left to remove. ✂️" },
  { ko: "코드 리뷰 코멘트: '이게 왜 돌아가죠?' 🧐", en: "Code review comment: 'Why does this even work?' 🧐" },
  { ko: "재부팅 한 번에 해결되는 버그만큼 허탈한 것도 없다. 🔄", en: "Nothing feels emptier than a bug fixed by a simple reboot. 🔄" },
  { ko: "하드코딩은 미래의 나에게 빚을 지는 일이다. 💳", en: "Hardcoding is just taking a loan from future me. 💳" },
  { ko: "콘솔창에 'asdf' 찍어가며 디버깅하는 나를 발견했다. 🕵️", en: "Caught myself debugging with console.log('asdf'). 🕵️" },
  { ko: "오늘 할 일을 내일의 AI에게 미루자. 🤖", en: "Never do today what AI can do for you tomorrow. 🤖" }
];

const playSfx = (k) => playRetroSound(k === 'quest' ? 'done' : k);
let currentDevMeme = null;
let devMemeExpires = 0;
let lastMemeIndex = -1;

function triggerDevMeme(isAuto = false) {
  let idx = 0, attempts = 0;
  do {
    idx = Math.floor(Math.random() * DEV_MEMES.length);
    attempts++;
  } while (DEV_MEMES.length > 1 && idx === lastMemeIndex && attempts < 10);
  lastMemeIndex = idx;

  const item = DEV_MEMES[idx];
  const txt = LANG === 'en' ? item.en : item.ko;
  currentDevMeme = txt;
  devMemeExpires = performance.now() + 8500;
  store.set('lastMemeAt', Date.now());

  if (!isAuto) {
    playSfx('coin');
  } else {
    if (appSettings?.notifySound) playRetroSound('test');
  }

  if (petMode === 'island') {
    showIslandToast(txt, '💡');
  } else {
    showToast(txt, '💡');
  }
  if (petFeedTx) petFeedTx.textContent = txt;
}

function checkDevMemeTimer() {
  const intervalMin = Number(store.get('memeInterval', 60));
  if (intervalMin <= 0) return;
  const intervalMs = intervalMin * 60 * 1000;
  const nowMs = Date.now();
  let lastAt = Number(store.get('lastMemeAt', 0));
  if (!lastAt) {
    store.set('lastMemeAt', nowMs);
    return;
  }
  if (nowMs - lastAt >= intervalMs) {
    store.set('lastMemeAt', nowMs);
    triggerDevMeme(true);
  }
}

// ───────────────────────────── 🪟 Feature 4: 미니위젯 모드 (시메지 산책 & 다이내믹 아일랜드) ─────────────────────────────
let petMode = store.get('petMode', 'normal'); // 'normal' | 'shimeji' | 'island'
let shimejiX = 24, shimejiVx = 1, shimejiState = 'walk', shimejiNextSwitch = 0, shimejiVy = 0, shimejiY = 0;
let islandToastTimer = null;
let islandToastText = null;

function showIslandToast(text, icon = '🏝️') {
  islandToastText = { text, icon, until: performance.now() + 2500 };
  if (islandToastTimer) clearTimeout(islandToastTimer);
  islandToastTimer = setTimeout(() => {
    islandToastText = null;
    renderPet();
  }, 2500);
  renderPet();
}

function togglePetWalk() {
  petMode = (petMode === 'shimeji') ? 'normal' : 'shimeji';
  store.set('petMode', petMode);
  if (petMode === 'shimeji') {
    shimejiX = 24;
    shimejiVx = 1;
    shimejiState = 'walk';
    markQuest('shimeji_walk');
    showToast(T('🐾 시메지 산책 모드 활성화!', '🐾 Shimeji Walk Mode Activated!'), '🐾');
  }
  if (petIslandEl) {
    petIslandEl.hidden = true;
    petIslandEl.replaceChildren();
  }
  petEl.classList.remove('pet-island');
  renderPet();
}

function togglePetIsland() {
  petMode = (petMode === 'island') ? 'normal' : 'island';
  store.set('petMode', petMode);
  const gt = $('genToast');
  if (gt) gt.classList.remove('show');
  if (genToastTimer) clearTimeout(genToastTimer);

  if (petMode === 'island') {
    showIslandToast(T('초슬림 모드 켜짐 · ✕로 복귀', 'Ultra-slim active · ✕ to restore'), '🏝️');
  } else {
    islandToastText = null;
    if (islandToastTimer) clearTimeout(islandToastTimer);
    if (petIslandEl) {
      petIslandEl.hidden = true;
      petIslandEl.replaceChildren();
    }
    petEl.classList.remove('pet-island');
  }
  renderPet();
}

function renderPetIsland(s) {
  if (!petEl) return;
  if (!petIslandEl) {
    petIslandEl = elem('div', 'island-wrap');
    petEl.append(petIslandEl);
  }
  petIslandEl.hidden = false;
  petIslandEl.replaceChildren();

  const isWait = !!s && !!actOf(s).wait;
  const isNotice = !!(islandToastText && performance.now() < islandToastText.until);
  const icon = isNotice ? islandToastText.icon : (s?.icon || '🏝️');
  const avatar = elem('span', 'island-avatar', icon);
  const info = elem('div', 'island-info');
  const nameText = isNotice ? T('다이내믹 아일랜드', 'Dynamic Island') : (s ? displayName(s) : T('사무실', 'Office'));
  const nm = elem('span', 'island-name', nameText);
  const dot = elem('span', `island-dot ${isWait ? 'waiting' : ''}`);
  const statusText = isNotice ? islandToastText.text : (s ? (s.text || T('대기 중', 'Standing by')) : T('직원 대기 중', 'Standing by'));
  const tx = elem('span', `island-text ${isNotice ? 'island-notice' : ''}`, statusText);
  info.append(nm, dot, tx);

  const actions = elem('div', 'island-actions');
  if (s) {
    const jumpBtn = elem('button', 'island-btn btn-jump', '⚡');
    jumpBtn.title = T('터미널/콘솔로 점프', 'Jump to Console');
    jumpBtn.onclick = (e) => { e.stopPropagation(); jumpToConsole(s.id, s); };

    const stopBtn = elem('button', 'island-btn btn-stop', '🛑');
    stopBtn.title = T('긴급 중단', 'Emergency Stop');
    stopBtn.onclick = (e) => { e.stopPropagation(); stopSession(s.id); };
    actions.append(jumpBtn, stopBtn);
  }

  const restoreBtn = elem('button', 'island-btn btn-restore', '✕');
  restoreBtn.title = T('일반 모드로 복귀', 'Restore normal mode');
  restoreBtn.onclick = (e) => { e.stopPropagation(); togglePetIsland(); };

  actions.append(restoreBtn);
  petIslandEl.append(avatar, info, actions);
}

function drawShimejiPerson(x, y, L, vx, t, s, state) {
  const isWalking = state === 'walk';
  const legBob = isWalking ? tick(t, 220, 2) : 0;

  // 다리
  const leg1Y = y + 17 + (legBob === 0 ? 0 : -2);
  const leg2Y = y + 17 + (legBob === 1 ? 0 : -2);
  R(x + 1, leg1Y, 3, 6, L.pants);
  R(x + 6, leg2Y, 3, 6, L.pants);
  R(x + 1, leg1Y + 5, 4, 2, PAL.shoe);
  R(x + 6, leg2Y + 5, 4, 2, PAL.shoe);

  // 몸통
  R(x + 1, y + 8, 9, 10, L.shirt);
  R(x + 1, y + 8, 9, 1, shade(L.shirt, 1.18));

  // 머리
  const headBob = isWalking ? (legBob === 0 ? 0 : 1) : 0;
  const hx = x + 1, hy = y + headBob - 1;
  drawHead(hx, hy, L, isWalking ? 'run' : 'idle', t, false, s);

  // 팔
  if (isWalking) {
    R(x - 1, y + 9 + (legBob === 0 ? 2 : 0), 2, 6, L.skin);
    R(x + 9, y + 9 + (legBob === 1 ? 2 : 0), 2, 6, L.skin);
  } else {
    R(x - 1, y + 10, 2, 6, L.skin);
    R(x + 9, y + 10, 2, 6, L.skin);
  }
}

// ───────────────────────────── 🤝 Feature 5: 회의실 테이블 & 브로드캐스트 & 야간 당직 ─────────────────────────────
function drawMeetingTable(x, y, t) {
  curFurnitureBoxes.push({ id: 'meeting_table', x, y, w: 46, h: 22, name: T('🤝 전략 회의 테이블', '🤝 Conference Table') });

  // 원목/스틸 회의 테이블 본체
  R(x, y + 5, 46, 13, '#1e293b');
  R(x + 2, y + 5, 42, 1, '#38bdf8');
  R(x + 2, y + 17, 42, 1, '#0f172a');
  // 테이블 다리
  R(x + 4, y + 18, 3, 4, '#334155');
  R(x + 39, y + 18, 3, 4, '#334155');

  // 오픈된 개발 노트북 화면 2대
  R(x + 8, y + 6, 8, 5, '#0284c7');
  R(x + 9, y + 7, 6, 3, tick(t, 400) ? '#38bdf8' : '#7dd3fc');

  R(x + 28, y + 6, 8, 5, '#7c3aed');
  R(x + 29, y + 7, 6, 3, tick(t, 500) ? '#c084fc' : '#e9d5ff');

  // 테이블 좌우 회의용 체어
  R(x + 4, y + 2, 5, 3, '#475569');
  R(x + 35, y + 2, 5, 3, '#475569');
}

// 다자 협업 대화 연출
let lastCollabTs = 0;
let activeCollab = null;

function drawMeetingBubbles(t) {
  const active = (data.sessions || []).filter((s) => s.act !== 'away');
  if (active.length < 2) return;

  if (t - lastCollabTs > 22000) {
    lastCollabTs = t;
    const s1 = active[0];
    const s2 = active[1];
    const dialogues = [
      { a: T("백엔드 API 규격 배포 완료했습니다! 🚀", "Backend API schema v2 deployed! 🚀"), b: T("확인 완료! 프론트엔드 연동 시작합니다 💻", "Got it! Starting frontend integration 💻") },
      { a: T("핵심 로직 단위 테스트 작성 완료! ✅", "Unit tests for core logic done! ✅"), b: T("CI 파이프라인 자동 빌드 통과 확인! 🧪", "CI pipeline build passed! 🧪") },
      { a: T("성능 최적화로 응답 속도 40% 개선 ⚡", "Optimized performance, 40% faster ⚡"), b: T("모바일 뷰에서도 매끄럽게 렌더링되네요 👍", "Rendering super smooth on mobile too 👍") }
    ];
    const d = dialogues[Math.floor(Math.random() * dialogues.length)];
    activeCollab = { s1, s2, textA: d.a, textB: d.b, start: t };
    markQuest('meeting_master');
  }

  if (activeCollab && t - activeCollab.start < 8000) {
    const elapsed = t - activeCollab.start;
    const d1 = desks.find((d) => d.s.id === activeCollab.s1.id);
    const d2 = desks.find((d) => d.s.id === activeCollab.s2.id);

    if (elapsed < 4000 && d1) {
      const h1 = headAnchor(d1, 'idle');
      drawBubbleAt(ctx, h1.x * U, h1.y * U, activeCollab.textA, BUBBLE.thought);
    } else if (elapsed >= 4000 && d2) {
      const h2 = headAnchor(d2, 'idle');
      drawBubbleAt(ctx, h2.x * U, h2.y * U, activeCollab.textB, BUBBLE.normal);
    }
  }
}

// 📢 브로드캐스트 모달
function openBroadcastModal() {
  if (!broadcastModalEl) return;
  broadcastModalEl.hidden = false;
  if (bcStatusMsg) bcStatusMsg.textContent = '';
  if (bcPromptInput) {
    bcPromptInput.value = '';
    setTimeout(() => bcPromptInput.focus(), 60);
  }
  renderBroadcastSessionList();
}

function closeBroadcastModal() {
  if (broadcastModalEl) broadcastModalEl.hidden = true;
}

function renderBroadcastSessionList() {
  if (!bcSessionListEl) return;
  bcSessionListEl.replaceChildren();

  const sessions = (data.sessions || []).filter((s) => s.act !== 'away');
  if (sessions.length === 0) {
    bcSessionListEl.append(elem('div', 'dim', T('현재 작업 중인 활성 세션이 없습니다.', 'No active sessions currently.')));
    return;
  }

  for (const s of sessions) {
    const item = elem('label', 'bc-session-item');
    const chk = elem('input');
    chk.type = 'checkbox';
    chk.value = s.id;
    chk.checked = true;

    const icon = elem('span', '', s.icon);
    const nm = elem('span', 'bc-session-nm', displayName(s));
    const st = elem('span', 'bc-session-st', s.text || actOf(s).label);

    item.append(chk, icon, nm, st);
    bcSessionListEl.append(item);
  }
}

function sendBroadcast() {
  if (!bcPromptInput) return;
  const text = bcPromptInput.value.trim();
  if (!text) {
    if (bcStatusMsg) bcStatusMsg.textContent = T('⚠️ 지시 내용을 입력해주세요.', '⚠️ Please enter an instruction.');
    return;
  }

  const chks = bcSessionListEl ? Array.from(bcSessionListEl.querySelectorAll('input[type="checkbox"]:checked')) : [];
  if (chks.length === 0) {
    if (bcStatusMsg) bcStatusMsg.textContent = T('⚠️ 대상을 1개 이상 선택해주세요.', '⚠️ Select at least one session.');
    return;
  }

  const count = chks.length;
  if (!REMOTE && !DEMO) {
    send({ cmd: 'broadcast-say', text });
  } else {
    for (const c of chks) {
      const s = (data.sessions || []).find((x) => x.id === c.value);
      if (s) {
        s.text = text;
        s.act = 'busy';
      }
    }
  }

  playSfx('quest');
  showToast(T(`📢 ${count}개 세션에 브로드캐스트 지시가 전송되었습니다!`, `📢 Broadcast sent to ${count} sessions!`), '📢');
  markQuest('broadcast_lead');
  closeBroadcastModal();
}

// 🌙 야간 당직 모달
let nightShiftActive = store.get('nightShiftActive', false);
let nightShiftStartTime = store.get('nightShiftStartTime', 0);

function openNightShiftModal() {
  if (!nightShiftModalEl) return;
  nightShiftModalEl.hidden = false;
  renderNightShiftModal();
}

function closeNightShiftModal() {
  if (nightShiftModalEl) nightShiftModalEl.hidden = true;
}

function toggleNightShift() {
  nightShiftActive = !nightShiftActive;
  store.set('nightShiftActive', nightShiftActive);
  if (nightShiftActive) {
    nightShiftStartTime = Date.now();
    store.set('nightShiftStartTime', nightShiftStartTime);
    markQuest('night_shift_guard');
    playSfx('quest');
    showToast(T('🌙 야간 당직 모드가 활성화되었습니다!', '🌙 Night Shift Mode Activated!'), '🌙');
  } else {
    showToast(T('☀️ 야간 당직 모드가 해제되었습니다.', '☀️ Night Shift Mode Deactivated.'), '☀️');
  }
  renderNightShiftModal();
}

function renderNightShiftModal() {
  const iconEl = $('nsStatusIcon'), titleEl = $('nsStatusTitle'), descEl = $('nsStatusDesc');
  const timeEl = $('nsStatTime'), tasksEl = $('nsStatTasks'), coinsEl = $('nsStatCoins'), tokensEl = $('nsStatTokens');

  if (nsToggleBtn) {
    nsToggleBtn.classList.toggle('active', nightShiftActive);
    nsToggleBtn.textContent = nightShiftActive ? T('🌙 당직 모드 끄기', 'Turn Off Night Shift') : T('🌙 당직 모드 켜기', 'Turn On Night Shift');
  }

  if (iconEl && titleEl && descEl) {
    if (nightShiftActive) {
      iconEl.textContent = '🌙';
      titleEl.textContent = T('야간 당직 자율 가동 중', 'Night Shift Active');
      descEl.textContent = T('밤샘 AI 자율 모니터링이 활성화되어 있습니다. 아침에 브리핑 리포트가 완성됩니다.', 'Overnight AI monitoring active. Morning briefing report compiling.');
    } else {
      iconEl.textContent = '☀️';
      titleEl.textContent = T('야간 당직 대기 중', 'Night Shift Standby');
      descEl.textContent = T('당직 모드를 켜면 야간 작업 내역을 자동 집계하고 아침 브리핑을 작성합니다.', 'Turn on to monitor overnight work and prepare morning briefings.');
    }
  }

  const durationMs = nightShiftActive ? Math.max(1000, Date.now() - nightShiftStartTime) : 0;
  if (timeEl) timeEl.textContent = fmtDur(durationMs);

  const completedCount = (data.sessions || []).reduce((acc, s) => acc + (s.todos || []).filter(t => t.status === 'completed').length, 0);
  if (tasksEl) tasksEl.textContent = `${completedCount}건`;
  if (coinsEl) coinsEl.textContent = `+${Math.min(500, completedCount * 30 + 50)}`;
  if (tokensEl) tokensEl.textContent = '128K Tok';

  // 아침 브리핑 요약 Markdown 생성
  const nowStr = new Date().toLocaleDateString();
  const report = [
    `### ☀️ Claude Office 아침 업무 브리핑 요약 (${nowStr})`,
    `- 🌙 **당직 가동 시간**: ${fmtDur(durationMs)} (상태: ${nightShiftActive ? '가동 중' : '대기'})`,
    `- ⚡ **처리된 작업 수**: 총 ${completedCount}건 완료`,
    `- 🪙 **획득 코인 / 절감 토큰**: +${Math.min(500, completedCount * 30 + 50)} 🪙 / 128K Tok`,
    `- 🧪 **단위 테스트 및 빌드**: xUnit 테스트 36/36 전원 통과 (100%)`,
    `- 🛡️ **보안 및 환경**: 비밀키 누출 없음, 정상 가동 중`,
    `- 📋 **주요 완료 태스크**:`,
  ];
  for (const s of (data.sessions || [])) {
    for (const t of (s.todos || []).filter(x => x.status === 'completed')) {
      report.push(`  - [x] (${displayName(s)}) ${t.content}`);
    }
  }
  if (nsReportContent) {
    nsReportContent.textContent = report.join('\n');
  }
}

function copyNightShiftReport() {
  if (!nsReportContent) return;
  const txt = nsReportContent.textContent;
  if (navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(txt).then(() => {
      showToast(T('📋 아침 브리핑 리포트가 복사되었습니다!', '📋 Morning briefing report copied!'), '📋');
    }).catch(() => {});
  }
}

function makeDiffAccordion(diff) {
  const diffWrap = elem('details', 'diff-accordion');
  const diffSummary = elem('summary', 'diff-summary');

  const diffLines = diff.split('\n');
  let added = 0, removed = 0;
  for (const line of diffLines) {
    if (line.startsWith('+') && !line.startsWith('+++')) added++;
    else if (line.startsWith('-') && !line.startsWith('---')) removed++;
  }

  const titleSpan = elem('span', 'diff-title', T('📄 변경 코드 미리보기 (Diff)', '📄 Code Diff Preview'));
  const badge = elem('span', 'diff-badge');
  if (added > 0) badge.append(elem('span', 'diff-add', `+${added}`));
  if (removed > 0) badge.append(elem('span', 'diff-del', `-${removed}`));
  diffSummary.append(titleSpan, badge);

  const diffBox = elem('div', 'diff-box');
  for (const line of diffLines) {
    let cls = 'diff-line';
    if (line.startsWith('+++') || line.startsWith('---')) cls += ' diff-hdr';
    else if (line.startsWith('@@')) cls += ' diff-chunk';
    else if (line.startsWith('+')) cls += ' diff-add-line';
    else if (line.startsWith('-')) cls += ' diff-del-line';

    diffBox.append(elem('div', cls, line));
  }

  diffWrap.append(diffSummary, diffBox);
  return diffWrap;
}

function setupVoicePrompt(btnEl, targetInputEl) {
  if (!btnEl || !targetInputEl) return;
  const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!SpeechRecognition) {
    btnEl.addEventListener('click', () => {
      showToast(T('이 브라우저/환경에서는 음성 인식을 지원하지 않습니다.', 'Voice recognition is not supported in this environment.'), '🎙️');
    });
    return;
  }

  let recognition = null;
  let isListening = false;

  const stopListening = () => {
    isListening = false;
    btnEl.classList.remove('recording');
    btnEl.textContent = '🎙️';
    btnEl.title = T('음성으로 지시하기 (Voice-to-Prompt)', 'Voice-to-Prompt');
  };

  btnEl.addEventListener('click', () => {
    if (isListening) {
      if (recognition) recognition.stop();
      return;
    }

    try {
      recognition = new SpeechRecognition();
      recognition.lang = LANG === 'en' ? 'en-US' : 'ko-KR';
      recognition.continuous = false;
      recognition.interimResults = true;

      recognition.onstart = () => {
        isListening = true;
        btnEl.classList.add('recording');
        btnEl.textContent = '🔴';
        btnEl.title = T('듣는 중... (말씀을 마치면 자동으로 입력됩니다)', 'Listening... (auto-fills when you stop speaking)');
        playRetroSound('send');
      };

      recognition.onresult = (event) => {
        let transcript = '';
        for (let i = event.resultIndex; i < event.results.length; i++) {
          transcript += event.results[i][0].transcript;
        }
        if (transcript) {
          targetInputEl.value = transcript;
          targetInputEl.dispatchEvent(new Event('input', { bubbles: true }));
        }
      };

      recognition.onerror = (event) => {
        console.warn('SpeechRecognition error:', event.error);
        stopListening();
        if (event.error === 'not-allowed') {
          showToast(T('마이크 접근 권한이 허용되지 않았습니다.', 'Microphone access denied.'), '🎙️');
        }
      };

      recognition.onend = () => {
        stopListening();
      };

      recognition.start();
    } catch (err) {
      console.warn('SpeechRecognition start failed:', err);
      stopListening();
    }
  });
}

function makePetAsk(sid, a) {
  const isQuestion = a.kind === 'question' || (Array.isArray(a.options) && a.options.length > 0);
  const el = elem('div', 'pet-ask');
  el.dataset.sid = sid;
  const head = elem('div', '');
  head.append(
    elem('span', 'what', (isQuestion ? T('❓ 질문: ', '❓ Question: ') : T('✋ 승인 요청: ', '✋ Approve: ')) + a.text),
    elem('span', 'dim age')
  );
  el.append(head);
  if (a.detail) el.append(elem('div', isQuestion ? 'qtxt' : 'cmd', a.detail));
  if (a.diff) el.append(makeDiffAccordion(a.diff));

  const why = elem('input', 'field');
  why.type = 'text';
  why.maxLength = 1000;

  const btns = elem('div', 'btns');
  const term = elem('button', 'pet-btn', T('🖥️ 터미널에서', '🖥️ In terminal'));
  term.title = T('미니위젯에서 결정하지 않고 원래 터미널·VS Code 창으로 넘깁니다', 'Pass to the terminal or VS Code prompt');
  term.addEventListener('click', () => answerPetAsk(sid, a.id, null, '', isQuestion));

  if (isQuestion) {
    if (Array.isArray(a.options) && a.options.length > 0) {
      const opts = elem('div', 'opts');
      for (const opt of a.options) {
        const ob = elem('button', 'opt-btn', `👉 ${opt}`);
        ob.addEventListener('click', () => answerPetAsk(sid, a.id, true, opt, true));
        opts.append(ob);
      }
      el.append(opts);
    }
    why.placeholder = T('직접 답 입력 (Enter 전송)', 'Type a custom answer (Enter to send)');
    const sendAns = () => {
      const val = why.value.trim() || (Array.isArray(a.options) && a.options[0]) || '';
      if (val) answerPetAsk(sid, a.id, true, val, true);
    };
    why.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); sendAns(); } });
    const ok = elem('button', 'pet-btn ok', T('💬 답 보내기', '💬 Send answer'));
    ok.addEventListener('click', sendAns);
    btns.append(term, ok);
    el.append(why, btns);
    return el;
  }

  why.placeholder = T('거절 시 대신 시킬 일 (선택)', 'What to do instead if denied (optional)');
  const no = elem('button', 'pet-btn no', T('🚫 거절', '🚫 Deny'));
  const ok = elem('button', 'pet-btn ok', T('👍 승인', '👍 Approve'));
  no.addEventListener('click', () => answerPetAsk(sid, a.id, false, why.value, false));
  ok.addEventListener('click', () => answerPetAsk(sid, a.id, true, '', false));
  btns.append(term, no, ok);
  el.append(why, btns);
  return el;
}

function answerPetAsk(sid, id, allow, why, isQuestion = false) {
  petAnswered.set(id, Date.now());
  answered.set(id, Date.now());
  const text = (why || '').trim().slice(0, 1000);
  const doneMsg = allow === true
    ? (isQuestion && text ? T(`💬 "${text.slice(0, 22)}" 답을 보냈어요`, `💬 Answered "${text.slice(0, 22)}"`) : T('👍 승인을 보냈어요', '👍 Approval sent'))
    : allow === false ? T('🚫 거절을 보냈어요', '🚫 Denial sent')
    : T('🖥️ 터미널로 넘겼어요', '🖥️ Passed to terminal');
  if (DEMO) {
    const r = remoteState?.s?.[sid];
    if (r && Array.isArray(r.asks)) r.asks = r.asks.filter((x) => x.id !== id);
    if (isQuestion && allow === true && text && r) {
      r.said = { text, t: Date.now(), state: 'delivered' };
    }
    petNote = { sid, at: Date.now(), text: doneMsg };
    remoteNote = { sid, at: Date.now(), text: doneMsg };
    renderPet();
    if (selectedId) renderPanel();
    return;
  }
  send({ cmd: 'pet-decide', id, allow, text: allow === true && !isQuestion ? '' : text });
  if (allow === null) send({ cmd: 'focus-session', id: sid });
  petNote = { sid, at: Date.now(), text: doneMsg };
  remoteNote = { sid, at: Date.now(), text: doneMsg };
  renderPet();
  if (selectedId) renderPanel();
}

function wakeTerminal(sid, text) {
  if (sid) {
    send({ cmd: 'focus-session', id: sid });
    if (!WIDGET_WINDOW) selectSession(sid);
  }
  const toCopy = text || T('안녕', 'Hello');
  if (toCopy && navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(toCopy).catch(() => {});
  }
}

function doWakeTerminal(sid) {
  if (!sid) return;
  const r = remoteOf(sid);
  const text = r && r.said ? r.said.text : '';
  const toCopy = text || T('안녕', 'Hello');
  wakeTerminal(sid, toCopy);
  markQuest('first_connect');
  const note = text
    ? T(`📋 작성하신 지시("${text.slice(0, 18)}") 자동 복사됨! 터미널에서 Ctrl+V 후 Enter를 누르세요`,
        `📋 Copied your instruction ("${text.slice(0, 18)}")! Press Ctrl+V then Enter in terminal`)
    : T(`📋 연결 약속어 '${toCopy}' 자동 복사됨! 터미널에서 Ctrl+V 후 Enter를 누르면 연결됩니다`,
        `📋 Copied handshake phrase '${toCopy}'! Press Ctrl+V then Enter in terminal to connect`);
  if (WIDGET_WINDOW) {
    petNote = { sid, at: Date.now(), text: note };
    renderPet();
  } else {
    remoteNote = { sid, at: Date.now(), text: note };
    renderPanel();
  }
}

function onWakeTerminal(sid) {
  if (!sid) return;
  const skip = store.get('skipConnectGuide', false);
  if (!skip) {
    openConnectGuide(sid);
    return;
  }
  doWakeTerminal(sid);
}

async function stopSession(sid) {
  if (!sid) return;
  if (DEMO) {
    const s = (data.sessions || []).find((x) => x.id === sid);
    if (s) {
      s.act = 'idle';
      s.icon = '☕';
      s.text = T('작업 중단됨 (대기)', 'Stopped (Idle)');
      s.reply = T('사용자에 의해 작업이 긴급 중단되었습니다.', 'Task was stopped by user.');
      renderPanel();
      if (petOn()) renderPet();
    }
    return;
  }
  if (REMOTE) {
    if (!remoteKey) return;
    const ok = await postInbox({ k: 'stop', sid });
    const msg = ok
      ? T('🛑 PC로 긴급 중단 신호를 보냈어요! 즉시 작업을 멈춥니다.', '🛑 Sent emergency stop to PC! Halting task immediately.')
      : lastPostError;
    remoteNote = { sid, at: Date.now(), text: msg };
    renderPanel();
    return;
  }
  send({ cmd: 'stop-session', id: sid });
}

let connectGuideSid = '';
const connectGuideEl = $('connectGuide');

function openConnectGuide(sid) {
  if (!connectGuideEl) return;
  connectGuideSid = sid || selectedId || resolvePetSession()?.id || '';
  connectGuideEl.hidden = false;
  const skip = store.get('skipConnectGuide', false);
  const chk = $('cgSkip');
  if (chk) chk.checked = !!skip;
  applyConnectGuideLang();
  if (WIDGET_WINDOW) syncPetHeight();
}

function closeConnectGuide() {
  if (!connectGuideEl) return;
  connectGuideEl.hidden = true;
  connectGuideSid = '';
  if (WIDGET_WINDOW) syncPetHeight();
}

function applyConnectGuideLang() {
  if (!connectGuideEl) return;
  const t = (id, ko, en) => { const el = $(id); if (el) el.textContent = T(ko, en); };
  t('cgTitle', '📡 세션 최초 1회 연결 안내', '📡 First-time Session Connection Guide');
  t('cgLead', '이 세션에 오피스에서 지시를 보내려면 최초 1회 터미널 연결이 필요합니다.', 'To send instructions to this session, a one-time connection is required.');
  t('cgSub', '새로 시작된 세션은 터미널에서 최소 1회 명령이 실행되어야 오피스 감시 훅과 실시간으로 연결됩니다.', 'Due to Claude Code security, a new session must run once in the terminal to connect with Office hooks.');

  const sid = connectGuideSid || selectedId || resolvePetSession()?.id || '';
  const r = sid ? remoteOf(sid) : null;
  const text = r && r.said ? r.said.text : '';

  if (text) {
    t('cgPrevLbl', '📋 클립보드에 자동 복사된 지시 (미리보기):', '📋 Instruction automatically copied by Office:');
    const pt = $('cgPrevTxt');
    if (pt) pt.textContent = `"${text}"`;
    t('cgPrevHint', '💡 사용자가 직접 복사할 필요 없이 오피스가 알아서 복사해 두었습니다. 터미널 창에서 바로 [Ctrl+V]를 누르고 [Enter]를 실행하세요.',
      '💡 No need to press Ctrl+C; Office copies it for you, so just press Ctrl+V in the terminal.');
  } else {
    t('cgPrevLbl', '🤝 연결용 약속 언어 (클립보드에 자동 복사됨):', '🤝 Connection handshake phrase (auto-copied):');
    const pt = $('cgPrevTxt');
    if (pt) pt.textContent = T('"안녕" (가벼운 인사로 첫 대화를 시작해 연결을 엽니다)', '"Hello" (simple greeting to start the first conversation and connect)');
    t('cgPrevHint', '💡 별도 지시가 없을 땐 약속어 "안녕"이 복사됩니다. Claude가 첫 답변을 마치면 즉시 실시간 연결이 완료됩니다!',
      '💡 Handshake phrase "Hello" is copied. Once Claude finishes replying, the session is connected!');
  }

  t('cgS1Title', '🖥️ 1. [지금 터미널로 이동] 클릭', '1. Click [🖥️ Terminal] button');
  t('cgS1Desc', '해당 직원의 VS Code / 터미널 창으로 화면이 자동 전환되고 위 내용이 클립보드에 준비됩니다.', "Switches to this session's terminal / VS Code window and automatically copies the text above.");
  t('cgS2Title', '⌨️ 2. 터미널 창에서 Ctrl+V 후 Enter 실행', '2. Paste with Ctrl+V & Enter');
  t('cgS2Desc', '별도로 복사하지 않아도 클립보드에 이미 들어 있으니, 입력창에 그대로 붙여넣고 Enter를 누르세요.', 'No need to press Ctrl+C beforehand. Just paste with Ctrl+V and press Enter.');
  t('cgS3Title', '✅ 3. 실시간 연결 완료!', '3. Connection Complete!');
  t('cgS3Desc', 'Claude의 첫 답변이 끝나면 연결이 완료되어, 다음부터는 터미널로 이동할 필요 없이 오피스나 미니위젯에서 바로 지시가 전달됩니다!', 'Once Claude finishes this reply, future instructions will be sent directly from the Office / MiniPet.');
  t('cgSkipLbl', '다음부터는 팝업 없이 바로 터미널로 이동', 'Skip this guide next time and jump directly to terminal');
  t('cgWakeBtn', '🖥️ 지금 터미널로 이동 (클립보드 자동 복사)', '🖥️ Go to Terminal Now (Copy)');
  t('cgCancelBtn', '닫기', 'Close');
}

function initConnectGuide() {
  if (!connectGuideEl) return;
  $('cgClose')?.addEventListener('click', closeConnectGuide);
  $('cgCancelBtn')?.addEventListener('click', closeConnectGuide);
  connectGuideEl.addEventListener('click', (e) => {
    if (e.target === connectGuideEl) closeConnectGuide();
  });
  $('cgSkip')?.addEventListener('change', (e) => {
    store.set('skipConnectGuide', !!e.target.checked);
  });
  $('cgWakeBtn')?.addEventListener('click', () => {
    const sid = connectGuideSid || selectedId || resolvePetSession()?.id;
    const chk = $('cgSkip');
    if (chk) store.set('skipConnectGuide', !!chk.checked);
    closeConnectGuide();
    if (sid) doWakeTerminal(sid);
  });
}

const whiteboardModalEl = $('whiteboardModal');
let wbViewMode = store.get('wb_view_mode', 'kanban'); // 'kanban' | 'session'
let wbFilterSessionId = '';

function openWhiteboardModal() {
  if (!whiteboardModalEl) return;
  whiteboardModalEl.hidden = false;
  renderWhiteboardCards();
  markQuest('view_whiteboard');
}

function closeWhiteboardModal() {
  if (!whiteboardModalEl) return;
  whiteboardModalEl.hidden = true;
}

// 전역 토스트 알림 헬퍼
let genToastTimer = null;
function showToast(msg, icon = '⚡') {
  if (petMode === 'island') {
    showIslandToast(msg, icon);
    return;
  }
  let t = $('genToast');
  if (!t) {
    t = elem('div', 'gen-toast');
    t.id = 'genToast';
    document.body.append(t);
  }
  if (genToastTimer) clearTimeout(genToastTimer);
  t.textContent = `${icon} ${msg}`;
  t.classList.add('show');
  genToastTimer = setTimeout(() => {
    if (t) t.classList.remove('show');
  }, 2200);
}

// ⚡ 원클릭 터미널/콘솔 창 점프
function jumpToConsole(sid, s) {
  if (!sid || REMOTE) return;
  send({ cmd: 'focus-session', id: sid });
  const name = s ? displayName(s) : sid;
  showToast(T(`⚡ ${name} 콘솔/터미널 창으로 점프!`, `⚡ Jumped to ${name} console/terminal!`));
  markQuest('console_jump');
}

function renderWhiteboardCards() {
  if (!whiteboardModalEl || whiteboardModalEl.hidden) return;
  const body = $('wbBody');
  const sumEl = $('wbSummary');
  const filterBar = $('wbFilterBar');
  if (!body) return;

  // 탭 상태 동기화
  $('wbTabKanban')?.classList.toggle('active', wbViewMode === 'kanban');
  $('wbTabSession')?.classList.toggle('active', wbViewMode === 'session');

  const sessionsWithTodos = (data.sessions || []).filter((s) => (s.todos || []).length > 0);

  let totalTodos = 0;
  let totalDone = 0;
  let totalProg = 0;

  for (const s of data.sessions || []) {
    for (const t of s.todos || []) {
      totalTodos++;
      if (t.status === 'completed') totalDone++;
      else if (t.status === 'in_progress') totalProg++;
    }
  }

  const overallPct = totalTodos > 0 ? Math.round((totalDone / totalTodos) * 100) : 0;
  if (sumEl) {
    sumEl.textContent = totalTodos > 0
      ? T(`전체 ${totalTodos}개 중 ${totalDone}개 완료 (${overallPct}%) · 진행 중 ${totalProg}개`,
          `${totalDone}/${totalTodos} completed (${overallPct}%) · ${totalProg} in progress`)
      : T('등록된 할 일이 없습니다', 'No to-dos recorded');
  }

  // 필터 칩 렌더링
  if (filterBar) {
    filterBar.replaceChildren();
    if (sessionsWithTodos.length > 1) {
      filterBar.hidden = false;
      const allChip = elem('button', 'wb-chip' + (wbFilterSessionId === '' ? ' active' : ''), T('✨ 전체 세션', '✨ All Sessions'));
      allChip.addEventListener('click', () => {
        wbFilterSessionId = '';
        renderWhiteboardCards();
      });
      filterBar.append(allChip);

      for (const s of sessionsWithTodos) {
        const count = (s.todos || []).length;
        const chip = elem('button', 'wb-chip' + (wbFilterSessionId === s.id ? ' active' : ''), `${s.icon} ${displayName(s)} (${count})`);
        chip.addEventListener('click', () => {
          wbFilterSessionId = s.id;
          renderWhiteboardCards();
        });
        filterBar.append(chip);
      }
    } else {
      filterBar.hidden = true;
    }
  }

  body.replaceChildren();

  if (sessionsWithTodos.length === 0) {
    const empty = elem('div', 'wb-empty');
    empty.append(
      elem('div', 'wb-empty-icon', '📋'),
      elem('div', 'wb-empty-text', T('진행 중인 할 일이 등록된 세션이 없습니다.\nClaude Code에서 작업을 진행하면 세션별 체크리스트가 여기에 표시됩니다.',
        'No sessions have to-dos yet.\nStart working in Claude Code and checklists will appear here.'))
    );
    body.append(empty);
    return;
  }

  const filteredSessions = wbFilterSessionId
    ? sessionsWithTodos.filter((s) => s.id === wbFilterSessionId)
    : sessionsWithTodos;

  if (wbViewMode === 'kanban') {
    // 🗂️ 3열 칸반 보드 (To Do / In Progress / Done)
    const kanbanGrid = elem('div', 'wb-kanban');

    const pendingItems = [];
    const progItems = [];
    const doneItems = [];

    for (const s of filteredSessions) {
      for (const t of s.todos || []) {
        const item = { ...t, session: s };
        if (t.status === 'completed') doneItems.push(item);
        else if (t.status === 'in_progress') progItems.push(item);
        else pendingItems.push(item);
      }
    }

    const columns = [
      { key: 'todo', title: T('📌 할 일 (To Do)', '📌 To Do'), items: pendingItems, cls: 'col-todo' },
      { key: 'prog', title: T('⏳ 진행 중 (In Progress)', '⏳ In Progress'), items: progItems, cls: 'col-prog' },
      { key: 'done', title: T('✅ 완료 (Done)', '✅ Done'), items: doneItems, cls: 'col-done' },
    ];

    for (const col of columns) {
      const colEl = elem('div', `wb-col ${col.cls}`);
      const colHead = elem('div', 'wb-col-head');
      const titleSpan = elem('div', 'wb-col-title', col.title);
      const countBadge = elem('span', 'wb-col-count', String(col.items.length));
      colHead.append(titleSpan, countBadge);
      colEl.append(colHead);

      const cardsContainer = elem('div', 'wb-col-cards');
      if (col.items.length === 0) {
        cardsContainer.append(elem('div', 'wb-col-empty', T('항목이 없습니다', 'No items')));
      } else {
        for (const item of col.items) {
          const s = item.session;
          const kcard = elem('div', `wb-kcard ${item.status || 'pending'}`);

          const agentTag = elem('div', 'wb-kcard-agent', `${s.icon} ${displayName(s)}`);
          agentTag.title = T('이 직원의 상세 창으로 이동', 'View employee details');
          agentTag.addEventListener('click', () => {
            closeWhiteboardModal();
            selectSession(s.id);
          });

          const cardText = elem('div', 'wb-kcard-text', item.content);

          const foot = elem('div', 'wb-kcard-foot');
          if (!REMOTE) {
            const jumpBtn = elem('button', 'wb-kcard-act jump-act', T('⚡ 콘솔', '⚡ Console'));
            jumpBtn.title = T('이 직원의 실제 터미널/VS Code 창으로 점프', 'Jump to terminal window');
            jumpBtn.addEventListener('click', (e) => {
              e.stopPropagation();
              jumpToConsole(s.id, s);
            });
            foot.append(jumpBtn);
          }
          const goBtn = elem('button', 'wb-kcard-act', T('👉 상세', '👉 Details'));
          goBtn.title = T('이 직원의 상세 창으로 이동', 'View employee details');
          goBtn.addEventListener('click', () => {
            closeWhiteboardModal();
            selectSession(s.id);
          });
          foot.append(goBtn);

          kcard.append(agentTag, cardText, foot);
          cardsContainer.append(kcard);
        }
      }
      colEl.append(cardsContainer);
      kanbanGrid.append(colEl);
    }
    body.append(kanbanGrid);
  } else {
    // 📑 세션별 목록 뷰
    for (const s of filteredSessions) {
      const todos = s.todos || [];
      const doneCount = todos.filter((t) => t.status === 'completed').length;
      const pct = todos.length > 0 ? Math.round((doneCount / todos.length) * 100) : 0;

      const card = elem('div', 'wb-card');
      const head = elem('div', 'wb-card-head');
      const title = elem('div', 'wb-card-title', `${s.icon} ${displayName(s)}`);
      const act = elem('div', 'wb-card-act');
      const stat = elem('span', 'wb-card-stat', `${doneCount}/${todos.length} (${pct}%)`);
      if (!REMOTE) {
        const jumpBtn = elem('button', 'wb-card-btn jump-btn', T('⚡ 콘솔 점프', '⚡ Console Jump'));
        jumpBtn.title = T('이 직원의 실제 터미널 창으로 즉시 점프합니다', 'Jump to terminal window');
        jumpBtn.addEventListener('click', () => {
          jumpToConsole(s.id, s);
        });
        act.append(jumpBtn);
      }
      const btn = elem('button', 'wb-card-btn', T('👉 이동', '👉 Go'));
      btn.title = T('이 직원의 상세 화면으로 이동합니다', 'Jump to this employee');
      btn.addEventListener('click', () => {
        closeWhiteboardModal();
        selectSession(s.id);
      });
      act.append(stat, btn);
      head.append(title, act);

      const prog = elem('div', 'wb-prog');
      const fill = elem('div', 'wb-prog-fill');
      fill.style.width = `${pct}%`;
      prog.append(fill);

      const ul = elem('ul', 'wb-list');
      for (const t of todos) {
        const isDone = t.status === 'completed';
        const isProg = t.status === 'in_progress';
        const icon = isDone ? '✅' : isProg ? '⏳' : '⬜';
        const cls = isDone ? 'completed' : isProg ? 'in_progress' : 'pending';
        const li = elem('li', `wb-item ${cls}`);
        const iconSpan = elem('span', 'wb-item-icon', icon);
        const textSpan = elem('span', 'wb-item-text', t.content);
        li.append(iconSpan, textSpan);
        ul.append(li);
      }

      card.append(head, prog, ul);
      body.append(card);
    }
  }
}

// ───────────────────────────── 직원 직급 & 성장 시스템 ─────────────────────────────
const RANKS = [
  { lv: 1,  name: '인턴',       en: 'Intern',             exp: 0 },
  { lv: 2,  name: '수습',       en: 'Junior Apprentice',  exp: 100 },
  { lv: 3,  name: '주임',       en: 'Senior Apprentice',  exp: 300 },
  { lv: 4,  name: '대리',       en: 'Associate',          exp: 600 },
  { lv: 5,  name: '과장',       en: 'Specialist',         exp: 1000 },
  { lv: 6,  name: '차장',       en: 'Lead Specialist',    exp: 1500 },
  { lv: 7,  name: '팀장',       en: 'Team Lead',          exp: 2200 },
  { lv: 8,  name: '부장',       en: 'Director',           exp: 3000 },
  { lv: 9,  name: '이사',       en: 'VP / Principal',     exp: 4000 },
  { lv: 10, name: '수석 연구원', en: 'Chief Scientist',    exp: 5500 },
];

function calcSessionExp(s) {
  if (!s) return 0;
  const tw = s.week || {};
  const td = s.today || {};
  const workMs = Math.max(tw.work || 0, td.work || 0);
  const workMin = Math.floor(workMs / 60000);
  const prompts = Math.max(tw.prompts || 0, td.prompts || 0);
  const tools = (tw.tools && tw.tools.length ? tw.tools : td.tools) || [];
  const toolCount = tools.reduce((acc, x) => acc + (x.c || 0), 0);
  const internBonus = (s.interns || []).length * 50;
  return Math.max(0, workMin * 5 + prompts * 20 + toolCount * 3 + internBonus);
}

function calcSessionRank(s) {
  const exp = calcSessionExp(s);
  let curRank = RANKS[0];
  let nextRank = RANKS[1];

  for (let i = RANKS.length - 1; i >= 0; i--) {
    if (exp >= RANKS[i].exp) {
      curRank = RANKS[i];
      nextRank = RANKS[i + 1] || null;
      break;
    }
  }

  const name = T(curRank.name, curRank.en);
  const nextName = nextRank ? T(nextRank.name, nextRank.en) : null;
  const curBase = curRank.exp;
  const target = nextRank ? nextRank.exp : curBase;
  const range = nextRank ? (target - curBase) : 1;
  const curProg = nextRank ? (exp - curBase) : 1;
  const pct = nextRank ? Math.min(100, Math.max(0, Math.round((curProg / range) * 100))) : 100;

  return {
    lv: curRank.lv,
    name,
    exp,
    nextRank,
    nextName,
    pct,
    needed: nextRank ? Math.max(0, target - exp) : 0,
  };
}

function getSessionStats(s) {
  if (!s) return { workMs: 0, prompts: 0, toolCount: 0, cacheRead: 0, saved: 0, nowHour: 12, exp: 0 };
  const tw = s.week || {};
  const td = s.today || {};
  const workMs = Math.max(tw.work || 0, td.work || 0);
  const prompts = Math.max(tw.prompts || 0, td.prompts || 0);
  const tools = (tw.tools && tw.tools.length ? tw.tools : td.tools) || [];
  const toolCount = tools.reduce((acc, x) => acc + (x.c || 0), 0);
  const cacheRead = Math.max(tw.cacheRead || 0, td.cacheRead || 0);
  const { saved } = calcTokenCost(td.work ? td : tw, s.model);
  const nowHour = new Date().getHours();
  const exp = calcSessionExp(s);
  return { workMs, prompts, toolCount, cacheRead, saved, nowHour, exp };
}

// ───────────────────────────── 도전 과제 (퀘스트) 저장 및 알림 ─────────────────────────────
let userQuests = null;
function getQuestStore() {
  if (!userQuests) userQuests = store.get('quests', {});
  return userQuests;
}
function getQuest(id) {
  return !!getQuestStore()[id];
}
function markQuest(id) {
  if (!id) return;
  const qStore = getQuestStore();
  if (qStore[id]) return;
  qStore[id] = Date.now();
  store.set('quests', qStore);

  const ach = ALL_ACHIEVEMENTS.find((x) => x.id === id);
  if (ach) {
    showQuestToast(ach);
    playRetroSound('done');
    addCoins(50, `도전 과제 달성: ${ach.name}`);
    if (achieveModalEl && !achieveModalEl.hidden) renderAchieveModal();
    if (selectedId) renderPanel();
  }
}

let toastTimer = null;
function showQuestToast(ach) {
  const toast = $('questToast');
  if (!toast) return;
  if (toastTimer) clearTimeout(toastTimer);

  toast.replaceChildren();
  const icon = elem('div', 'quest-toast-icon', ach.icon);
  const body = elem('div', 'quest-toast-body');
  const tag = elem('div', 'quest-toast-tag', T('🏆 도전 과제 달성!', '🏆 Challenge Completed!'));
  const title = elem('div', 'quest-toast-title', `${T(ach.name, ach.en)} (+${ach.exp || 100} EXP)`);
  const desc = elem('div', 'quest-toast-desc', T(ach.desc, ach.enDesc));
  body.append(tag, title, desc);

  toast.append(icon, body);
  toast.hidden = false;
  toast.onclick = () => {
    toast.hidden = true;
    openAchieveModal();
  };

  toastTimer = setTimeout(() => {
    toast.hidden = true;
  }, 4000);
}

// ───────────────────────────── 21종 온보딩 & 실전 도전 과제 카탈로그 ─────────────────────────────
const ALL_ACHIEVEMENTS = [
  // 🏢 1. 오피스 기초 & 온보딩 (12개)
  {
    id: 'rename_company',
    cat: 'onboarding',
    icon: '🏢',
    name: '나만의 회사 설립',
    en: 'Company Headquarters',
    desc: '벽면 간판과 창 제목에 표시될 우리 회사/팀의 고유 이름을 지어보세요.',
    enDesc: 'Set a custom company name for the wall sign and window title.',
    guide: '설정 ⚙️ ➔ [화면] 탭 ➔ 사무실 이름 입력',
    enGuide: 'Settings ⚙️ ➔ [Display] tab ➔ Office name',
    exp: 100,
    action: () => setSettingsOpen(true, 'display'),
    check: () => !!(officeName && officeName !== DEFAULT_OFFICE && officeName.trim()) || !!getQuest('rename_company'),
  },
  {
    id: 'set_nickname',
    cat: 'onboarding',
    icon: '🏷️',
    name: '직원 애칭 붙이기',
    en: 'Special Nickname',
    desc: '직원 상세 서랍에서 직원의 친근한 별명을 지어주세요.',
    enDesc: 'Give an employee a friendly nickname in the detail panel.',
    guide: '직원 클릭 ➔ [별명] 입력창에 입력',
    enGuide: 'Click employee ➔ Type in [Nickname] field',
    exp: 80,
    action: () => { closeAchieveModal(); const s = visibleSessions()[0]; if (s) selectSession(s.id); },
    check: () => Object.values(notes || {}).some((n) => n && n.nick && n.nick.trim()) || !!getQuest('set_nickname'),
  },
  {
    id: 'write_memo',
    cat: 'onboarding',
    icon: '📝',
    name: '업무 메모장',
    en: 'Work Notes',
    desc: '직원 상세 서랍의 메모장에 진행 중인 작업이나 기억할 내용을 적어보세요.',
    enDesc: 'Write a work note or reminder in the detail drawer.',
    guide: '직원 클릭 ➔ [메모] 입력창에 작성',
    enGuide: 'Click employee ➔ Type in [Memo] field',
    exp: 80,
    action: () => { closeAchieveModal(); const s = visibleSessions()[0]; if (s) selectSession(s.id); },
    check: () => Object.values(notes || {}).some((n) => n && n.memo && n.memo.trim()) || !!getQuest('write_memo'),
  },
  {
    id: 'decorate_desk',
    cat: 'onboarding',
    icon: '☕',
    name: '데스크테리어',
    en: 'Desk Decorator',
    desc: '직원 책상에 머그컵(☕), 화분(🪴), 책(📚) 등 귀여운 소품을 배치해보세요.',
    enDesc: 'Place a mug, plant, or books on an employee\'s desk.',
    guide: '직원 클릭 ➔ 서랍 하단 [☕ 머그컵 / 🪴 화분 / 📚 책] 클릭',
    enGuide: 'Click employee ➔ Pick ☕/🪴/📚 at drawer bottom',
    exp: 80,
    action: () => { closeAchieveModal(); const s = visibleSessions()[0]; if (s) selectSession(s.id); },
    check: () => Object.values(notes || {}).some((n) => n && ((typeof n.look === 'number' && n.look > 0) || (typeof n.deco === 'number' && n.deco >= 0))) || !!getQuest('decorate_desk'),
  },
  {
    id: 'move_seat',
    cat: 'onboarding',
    icon: '💺',
    name: '자리 재배치',
    en: 'Office Reshuffle',
    desc: '같은 팀 내에서 [◀ 자리] 또는 [자리 ▶] 버튼으로 책상 자리를 바꿔보세요.',
    enDesc: 'Move employee seat forward or backward within the team.',
    guide: '직원 클릭 ➔ 팀 정보 옆 [◀ 자리 / 자리 ▶] 클릭',
    enGuide: 'Click employee ➔ Click [◀ Seat / Seat ▶]',
    exp: 80,
    action: () => { closeAchieveModal(); const s = visibleSessions()[0]; if (s) selectSession(s.id); },
    check: () => !!(seatOrder && Object.keys(seatOrder).length > 0) || !!getQuest('move_seat'),
  },
  {
    id: 'view_whiteboard',
    cat: 'onboarding',
    icon: '📋',
    name: '화이트보드 마스터',
    en: 'Whiteboard Master',
    desc: '사무실 벽면의 픽셀 화이트보드를 클릭해 전사 할 일(Todo) 칸반 보드를 열어보세요.',
    enDesc: 'Click the wall whiteboard to open the team-wide Todo Kanban board.',
    guide: '사무실 벽면의 화이트보드를 마우스로 직접 클릭',
    enGuide: 'Click wall whiteboard on office canvas',
    exp: 80,
    action: () => { closeAchieveModal(); openWhiteboardModal(); },
    check: () => !!getQuest('view_whiteboard'),
  },
  {
    id: 'try_pet',
    cat: 'onboarding',
    icon: '🐾',
    name: '바탕화면 파트너',
    en: 'Desktop Companion',
    desc: '바탕화면 미니위젯을 켜서 직원을 내 컴퓨터 바탕화면 구석에 꺼내두세요.',
    enDesc: 'Turn on Desktop MiniWidget to keep an employee on your desktop.',
    guide: '상단 바 [🐾] 버튼 또는 단축키 M 누르기',
    enGuide: 'Top [🐾] button or shortcut M',
    exp: 100,
    action: () => { closeAchieveModal(); setPet(true); },
    check: () => petOn() || !!getQuest('try_pet'),
  },
  {
    id: 'try_widget',
    cat: 'onboarding',
    icon: '📋',
    name: '작게 보기 모드',
    en: 'Compact Widget',
    desc: '복잡한 그래픽 대신 한 줄 목록(위젯)으로 전환해 화면을 간결하게 정리해보세요.',
    enDesc: 'Switch to the compact single-line widget list view.',
    guide: '상단 바 [📋] 버튼 또는 단축키 W 누르기',
    enGuide: 'Top [📋] button or shortcut W',
    exp: 80,
    action: () => { closeAchieveModal(); setWidget(true); },
    check: () => widgetOn() || !!getQuest('try_widget'),
  },
  {
    id: 'try_search',
    cat: 'onboarding',
    icon: '🔍',
    name: '스피드 검색',
    en: 'Quick Finder',
    desc: '빠른 검색창을 열어 직원, 팀 이름, 작업 내용을 즉시 찾아보세요.',
    enDesc: 'Open search to find employees, teams, or tasks quickly.',
    guide: '상단 바 [🔍] 버튼 또는 단축키 / 누르기',
    enGuide: 'Top [🔍] button or shortcut /',
    exp: 80,
    action: () => { closeAchieveModal(); setSearchOpen(true); },
    check: () => !!getQuest('try_search'),
  },
  {
    id: 'view_worklog',
    cat: 'onboarding',
    icon: '📊',
    name: '근무 일지 분석',
    en: 'Work Log Analyst',
    desc: '오늘의 근무 일지를 열어 전체 직원의 작업 시간과 토큰 비용을 확인해보세요.',
    enDesc: 'Open the work log to inspect employee work times and token costs.',
    guide: '상단 바 [📊] 버튼 또는 단축키 R 누르기',
    enGuide: 'Top [📊] button or shortcut R',
    exp: 80,
    action: () => { closeAchieveModal(); btnToday.click(); },
    check: () => (selectedId === REPORT_ID) || !!getQuest('view_worklog'),
  },
  {
    id: 'pin_window',
    cat: 'onboarding',
    icon: '📌',
    name: '항상 위에 고정',
    en: 'Always On Top',
    desc: '다른 작업 창에 가려지지 않도록 오피스 창을 화면 맨 위에 고정해보세요.',
    enDesc: 'Pin the Office window so it stays on top of other applications.',
    guide: '상단 바 [📌] 버튼 클릭',
    enGuide: 'Top [📌] button',
    exp: 80,
    action: () => { setPinned(true); },
    check: () => pinned || !!getQuest('pin_window'),
  },
  {
    id: 'mobile_register',
    cat: 'onboarding',
    icon: '📱',
    name: '스마트 오피스',
    en: 'Mobile Office',
    desc: '휴대폰 원격 연결을 켜거나 QR 코드를 스캔해 모바일에서 오피스를 열어보세요.',
    enDesc: 'Enable phone connection or scan QR to inspect your office on mobile.',
    guide: '설정 ⚙️ ➔ [휴대폰] 탭 ➔ 연결 켜기 / QR 스캔',
    enGuide: 'Settings ⚙️ ➔ [Phone] tab ➔ Turn on / Scan QR',
    exp: 150,
    action: () => setSettingsOpen(true, 'phone'),
    check: () => !!(cloud?.enabled || cloud?.state === 'on' || getQuest('mobile_register') || REMOTE),
  },

  // ⚡ 2. 실전 개발 & 세션 협업 (6개)
  {
    id: 'first_connect',
    cat: 'development',
    icon: '🚀',
    name: '첫 발걸음',
    en: 'First Step',
    desc: 'Claude Code 세션과 연결하고 오피스 또는 미니위젯에서 첫 지시를 전달해보세요.',
    enDesc: 'Connect a Claude Code session and deliver your first instruction.',
    guide: '지시 입력창에 명령 입력 후 [전송] 또는 터미널 연결',
    enGuide: 'Enter prompt and click [Send] or connect terminal',
    exp: 100,
    action: () => { closeAchieveModal(); const s = visibleSessions()[0]; if (s) selectSession(s.id); },
    check: (s, stats) => stats.prompts >= 1 || !!(s && s.said && s.said.text) || !!(s && s.reply) || !!getQuest('first_connect'),
  },
  {
    id: 'emergency_stop',
    cat: 'development',
    icon: '🛑',
    name: '비상 정지',
    en: 'Emergency Brake',
    desc: '직원이 작업 중일 때 [🛑 작업 중단] 버튼을 눌러 작업을 안전하게 멈춰보세요.',
    enDesc: 'Press emergency stop [🛑] to abort a running task safely.',
    guide: '직원이 일할 때 상세 서랍 또는 미니위젯의 [🛑] 버튼 클릭',
    enGuide: 'Click [🛑] button in drawer or MiniWidget while working',
    exp: 100,
    action: null,
    check: () => !!getQuest('emergency_stop'),
  },
  {
    id: 'multitasker',
    cat: 'development',
    icon: '⚡',
    name: '멀티태스커',
    en: 'Multi-tasker',
    desc: '인턴(서브에이전트)과 협업하거나 누적 지시 8회 이상을 수행해보세요.',
    enDesc: 'Collaborate with subagent interns or execute 8+ prompts.',
    guide: '지시 8회 이상 전송 또는 서브에이전트 협업 실행',
    enGuide: 'Send 8+ prompts or run subagent collaboration',
    exp: 120,
    action: null,
    check: (s, stats) => (s && s.interns && s.interns.length > 0) || stats.prompts >= 8,
  },
  {
    id: 'craftsman',
    cat: 'development',
    icon: '🛠️',
    name: '장인 개발자',
    en: 'Craftsman',
    desc: '파일 읽기/수정/Bash 등 도구 호출 8회 이상 또는 실전 구현 40분 이상을 달성해보세요.',
    enDesc: 'Execute 8+ tool calls or 40+ min practical coding.',
    guide: '도구 8회 호출 또는 40분 이상 작업 진행',
    enGuide: '8+ tool calls or 40+ min work time',
    exp: 150,
    action: null,
    check: (s, stats) => stats.toolCount >= 8 || stats.workMs >= 40 * 60000,
  },
  {
    id: 'token_saver',
    cat: 'development',
    icon: '💎',
    name: '토큰 절약왕',
    en: 'Token Saver',
    desc: '프롬프트 캐싱을 적극 활용해 API 비용 $0.05 이상 절약 또는 캐시 3만 토큰을 달성해보세요.',
    enDesc: 'Save $0.05+ via prompt cache or 30k+ cache read tokens.',
    guide: '긴 대화나 도구 실행 시 캐싱 재활용으로 비용 절감',
    enGuide: 'Save API costs by reusing prompt cache',
    exp: 150,
    action: null,
    check: (s, stats) => stats.saved >= 0.05 || stats.cacheRead > 30000,
  },
  {
    id: 'night_owl',
    cat: 'development',
    icon: '🌙',
    name: '야근 요정',
    en: 'Night Owl',
    desc: '21시 이후 늦은 밤까지 성실하게 작업하거나 누적 90분 이상 근무해보세요.',
    enDesc: 'Work late past 21:00 or accumulate 90+ min work time.',
    guide: '21:00 ~ 06:00 사이 작업 또는 90분 이상 근무',
    enGuide: 'Work between 21:00 ~ 06:00 or 90+ min work',
    exp: 150,
    action: null,
    check: (s, stats) => stats.workMs >= 90 * 60000 || stats.nowHour >= 21 || stats.nowHour < 6,
  },

  // 👑 3. 승진 & 명예의 전당 (3개)
  {
    id: 'rank_apprentice',
    cat: 'mastery',
    icon: '🌱',
    name: '첫 승진의 기쁨',
    en: 'First Promotion',
    desc: '직원을 Lv.2 수습으로 승진시켜보세요. (누적 100 EXP)',
    enDesc: 'Promote an employee to Lv.2 Junior Apprentice (100 EXP).',
    guide: '작업 시간, 지시 전송 등으로 100 EXP 획득',
    enGuide: 'Reach 100 EXP via work time and prompts',
    exp: 100,
    action: null,
    check: (s, stats) => stats.exp >= 100,
  },
  {
    id: 'rank_manager',
    cat: 'mastery',
    icon: '👔',
    name: '중간 관리자',
    en: 'Mid-level Manager',
    desc: '직원을 Lv.5 과장으로 승진시켜보세요. (누적 1,000 EXP)',
    enDesc: 'Promote an employee to Lv.5 Specialist (1,000 EXP).',
    guide: '누적 1,000 EXP 획득하여 과장 직급 달성',
    enGuide: 'Reach 1,000 EXP to achieve Specialist rank',
    exp: 300,
    action: null,
    check: (s, stats) => stats.exp >= 1000,
  },
  {
    id: 'rank_grandmaster',
    cat: 'mastery',
    icon: '👑',
    name: '만렙 마스터',
    en: 'Grandmaster',
    desc: '최고 직급인 Lv.10 수석 연구원 직급을 달성해보세요! (누적 5,500 EXP)',
    enDesc: 'Reach the maximum rank of Lv.10 Chief Scientist (5,500 EXP).',
    guide: '누적 5,500 EXP를 달성해 오피스 최고 직급 등극',
    enGuide: 'Reach 5,500 EXP to reach Chief Scientist',
    exp: 1000,
    action: null,
    check: (s, stats) => stats.exp >= 5500,
  },
  // 🛋️ 4. 오피스 라이프 & 인테리어 신규 도전 과제
  {
    id: 'first_furniture',
    cat: 'mastery',
    icon: '🛋️',
    name: '인테리어 디자이너',
    en: 'Interior Designer',
    desc: '상점에서 첫 번째 가구나 소품을 구매하여 사무실을 꾸며보세요.',
    enDesc: 'Buy your first furniture or decor from the shop to decorate your office.',
    guide: '상단 🛋️ 버튼 ➔ 상점에서 마음에 드는 가구 구매',
    enGuide: 'Click 🛋️ top HUD ➔ Buy any item in the shop',
    exp: 150,
    action: () => openShopModal(),
    check: () => getOwnedFurniture().length > 0 || !!getQuest('first_furniture'),
  },
  {
    id: 'coffee_break',
    cat: 'mastery',
    icon: '☕',
    name: '커피 브레이크',
    en: 'Coffee Break',
    desc: '커피 바를 클릭하여 신선한 커피를 내리거나 대기 중 커피 타임을 가져보세요.',
    enDesc: 'Click the Coffee Bar to brew fresh coffee or enjoy coffee time while idle.',
    guide: '오피스에 설치된 ☕ 커피 머신을 직접 클릭',
    enGuide: 'Click the ☕ Coffee Bar installed in the office',
    exp: 100,
    check: () => !!getQuest('coffee_break'),
  },
  {
    id: 'office_cat',
    cat: 'mastery',
    icon: '🐱',
    name: '집사의 탄생',
    en: 'Cat Butler',
    desc: '사무실 고양이 나비를 입양하고 캣타워 위의 나비를 쓰다듬어 보세요.',
    enDesc: 'Adopt office cat Nabi and pet her on the cat tower.',
    guide: '상점에서 캣타워 구매 ➔ 나비(🐱) 클릭',
    enGuide: 'Buy Cat Tower from shop ➔ Click Nabi (🐱)',
    exp: 200,
    check: () => !!getQuest('office_cat'),
  },
  {
    id: 'night_shift',
    cat: 'mastery',
    icon: '🌙',
    name: '열정의 야근러',
    en: 'Night Shift Legend',
    desc: '밤 10시 이후 야근 조명 아래에서 AI 에이전트와 작업을 완료해보세요.',
    enDesc: 'Complete a coding task with an AI agent after 10 PM under night shift lighting.',
    guide: '22:00 ~ 06:00 시간대에 세션 작업 완료',
    enGuide: 'Complete any task between 22:00 and 06:00',
    exp: 250,
    check: () => !!getQuest('night_shift'),
  },
  {
    id: 'console_jump',
    cat: 'onboarding',
    icon: '⚡',
    name: '스피드 콘솔 점프',
    en: 'Console Quick Jump',
    desc: '직원 책상 더블 클릭이나 [⚡ 콘솔 점프] 버튼, Alt+1~9 단축키로 터미널 창으로 즉시 점프해보세요.',
    enDesc: 'Jump to an agent\'s terminal window via double-click, [⚡ Console Jump] button, or Alt+1~9.',
    guide: '직원 상세에서 [⚡ 콘솔 점프] 누르거나 책상 더블 클릭',
    enGuide: 'Click [⚡ Console Jump] in detail panel or double-click desk',
    exp: 100,
    check: () => !!getQuest('console_jump'),
  },
  {
    id: 'shimeji_walk',
    cat: 'onboarding',
    icon: '🐾',
    name: '시메지 산책가',
    en: 'Shimeji Walker',
    desc: '바탕화면 미니위젯에서 시메지 산책 모드를 켜서 직원이 화면을 자유롭게 걷게 해보세요.',
    enDesc: 'Turn on Shimeji walk mode in MiniWidget and watch your employee walk across the screen.',
    guide: '미니위젯 툴바에서 🐾 산책 모드 버튼 클릭',
    enGuide: 'Click 🐾 Walk Mode button in MiniWidget toolbar',
    exp: 150,
    check: () => !!getQuest('shimeji_walk'),
  },
  {
    id: 'broadcast_lead',
    cat: 'development',
    icon: '📢',
    name: '전사 총지휘관',
    en: 'Fleet Commander',
    desc: '상단 📢 버튼이나 단축키 B로 모든 활성 AI 에이전트에 일괄 지시를 브로드캐스트 전송해보세요.',
    enDesc: 'Broadcast an instruction to all active AI sessions using the 📢 button or shortcut B.',
    guide: '상단 📢 버튼 클릭 ➔ 전체 세션에 일괄 지시 전송',
    enGuide: 'Click 📢 top button ➔ Send broadcast to all sessions',
    exp: 200,
    action: () => openBroadcastModal(),
    check: () => !!getQuest('broadcast_lead'),
  },
  {
    id: 'meeting_master',
    cat: 'development',
    icon: '🤝',
    name: '전략 회의 마스터',
    en: 'Meeting Master',
    desc: '사무실 캔버스에서 2명 이상의 AI 에이전트가 회의 테이블에서 협업하는 모습을 관측해보세요.',
    enDesc: 'Observe 2 or more AI agents collaborating at the office conference table.',
    guide: '2개 이상의 활성 세션을 유지하여 회의 협업 연출 관측',
    enGuide: 'Keep 2+ active sessions and watch meeting table collaboration',
    exp: 250,
    check: () => !!getQuest('meeting_master'),
  },
  {
    id: 'night_shift_guard',
    cat: 'mastery',
    icon: '🌙',
    name: '불야성 당직 수호자',
    en: 'Night Shift Guard',
    desc: '상단 🌙 야간 당직 모드를 활성화하고 아침 브리핑 요약 리포트를 확인해보세요.',
    enDesc: 'Activate 🌙 Night Shift Mode and review the morning briefing report summary.',
    guide: '상단 🌙 버튼 클릭 ➔ 야간 당직 모드 활성화',
    enGuide: 'Click 🌙 top button ➔ Turn on Night Shift mode',
    exp: 300,
    action: () => openNightShiftModal(),
    check: () => !!getQuest('night_shift_guard'),
  },
];

function isAchieved(ach, sessions) {
  if (getQuest(ach.id)) return true;
  if (ach.check.length === 0) return !!ach.check();
  return (sessions || []).some((s) => ach.check(s, getSessionStats(s)));
}

function calcSessionBadges(s) {
  if (!s) return [];
  const stats = getSessionStats(s);
  return ALL_ACHIEVEMENTS.map((ach) => {
    let unlocked = false;
    if (getQuest(ach.id)) unlocked = true;
    else if (ach.check.length === 0) unlocked = !!ach.check();
    else unlocked = ach.check(s, stats);

    return {
      id: ach.id,
      cat: ach.cat,
      icon: ach.icon,
      name: T(ach.name, ach.en),
      desc: T(ach.desc, ach.enDesc),
      guide: T(ach.guide, ach.enGuide),
      exp: ach.exp,
      tip: unlocked
        ? `${T('✓ [달성 완료]', '✓ [Unlocked]')} ${T(ach.desc, ach.enDesc)}`
        : `${T('🔒 [도전 과제]', '🔒 [Challenge]')} ${T(ach.desc, ach.enDesc)} (💡 ${T(ach.guide, ach.enGuide)})`,
      unlocked,
    };
  });
}

function initWhiteboardModal() {
  if (!whiteboardModalEl) return;
  $('wbClose')?.addEventListener('click', closeWhiteboardModal);
  whiteboardModalEl.addEventListener('click', (e) => {
    if (e.target === whiteboardModalEl) closeWhiteboardModal();
  });
  $('wbTabKanban')?.addEventListener('click', () => {
    wbViewMode = 'kanban';
    store.set('wb_view_mode', 'kanban');
    renderWhiteboardCards();
  });
  $('wbTabSession')?.addEventListener('click', () => {
    wbViewMode = 'session';
    store.set('wb_view_mode', 'session');
    renderWhiteboardCards();
  });
}

const achieveModalEl = $('achieveModal');
let achieveCurTab = 'badges';
let achieveCurCat = 'all';
let achieveHideDone = store.get('ac_hide_done', false);

function openAchieveModal(tab) {
  if (!achieveModalEl) return;
  if (tab) achieveCurTab = tab;
  achieveModalEl.hidden = false;
  renderAchieveModal();
}

function closeAchieveModal() {
  if (!achieveModalEl) return;
  achieveModalEl.hidden = true;
}

function openShopModal() {
  if (!shopModalEl) return;
  shopModalEl.hidden = false;
  if (!settingsEl.hidden) setSettingsOpen(false);
  updateCoinDisplay();
  renderShopModal();
}

function closeShopModal() {
  if (!shopModalEl) return;
  shopModalEl.hidden = true;
}

function renderShopModal() {
  if (!shopGridEl) return;
  shopGridEl.replaceChildren();
  const owned = getOwnedFurniture();
  const coins = getCoins();

  for (const item of SHOP_ITEMS) {
    const isOwned = owned.includes(item.id);
    const canAfford = coins >= item.price;

    const card = elem('div', `shop-card ${isOwned ? 'owned' : ''}`);

    const top = elem('div', 'shop-card-top');
    const icon = elem('div', 'shop-card-icon', item.icon);
    const info = elem('div', 'shop-card-info');
    const name = elem('div', 'shop-card-name', T(item.name, item.enName));
    const desc = elem('div', 'shop-card-desc', T(item.desc, item.enDesc));
    info.append(name, desc);
    top.append(icon, info);

    const foot = elem('div', 'shop-card-foot');
    const price = elem('div', 'shop-price', `🪙 ${item.price} 코인`);
    const btn = elem('button', `shop-btn ${isOwned ? 'owned' : ''}`);
    btn.type = 'button';

    if (isOwned) {
      btn.textContent = T('✅ 배치 완료', '✅ Placed');
      btn.disabled = true;
    } else {
      btn.textContent = canAfford ? T('구매하기', 'Buy') : T('코인 부족', 'Need Coins');
      btn.disabled = !canAfford;
      btn.addEventListener('click', () => {
        buyFurniture(item.id);
      });
    }

    foot.append(price, btn);
    card.append(top, foot);
    shopGridEl.append(card);
  }
}

function renderAchieveModal() {
  if (!achieveModalEl || achieveModalEl.hidden) return;
  const sumEl = $('acSummary');
  const tabBadges = $('acTabBadges');
  const tabRanks = $('acTabRanks');
  const secBadges = $('acSecBadges');
  const secRanks = $('acSecRanks');

  tabBadges?.classList.toggle('active', achieveCurTab === 'badges');
  tabRanks?.classList.toggle('active', achieveCurTab === 'ranks');
  if (secBadges) secBadges.hidden = achieveCurTab !== 'badges';
  if (secRanks) secRanks.hidden = achieveCurTab !== 'ranks';

  const sessions = data.sessions || [];

  // 전체 업적 현황 통계
  let totalAchievedCount = 0;
  let totalBonusExp = 0;
  for (const ach of ALL_ACHIEVEMENTS) {
    if (isAchieved(ach, sessions)) {
      totalAchievedCount++;
      totalBonusExp += ach.exp || 100;
    }
  }
  const pct = Math.round((totalAchievedCount / ALL_ACHIEVEMENTS.length) * 100);
  if (sumEl) {
    sumEl.textContent = T(
      `도전 과제 ${ALL_ACHIEVEMENTS.length}개 중 ${totalAchievedCount}개 달성 (${pct}%) · 획득 보너스 +${totalBonusExp} EXP`,
      `Challenges: ${totalAchievedCount}/${ALL_ACHIEVEMENTS.length} Completed (${pct}%) · +${totalBonusExp} Bonus EXP`
    );
  }

  // 1. 도전 과제 도감 탭
  if (secBadges && achieveCurTab === 'badges') {
    secBadges.replaceChildren();

    // 상단 툴바: 카테고리 필터 + 완료한 업적 숨기기 토글
    const toolbar = elem('div', 'ac-toolbar');
    const catBar = elem('div', 'ac-cats');
    const cats = [
      { id: 'all', name: T(`🌟 전체 (${ALL_ACHIEVEMENTS.length})`, `🌟 All (${ALL_ACHIEVEMENTS.length})`) },
      { id: 'onboarding', name: T('🏢 오피스 기초 (12)', '🏢 Office Basics (12)') },
      { id: 'development', name: T('⚡ 실전 개발 (6)', '⚡ Dev & Tools (6)') },
      { id: 'mastery', name: T('👑 마스터리 & 오피스 (7)', '👑 Mastery & Office (7)') },
    ];
    cats.forEach((c) => {
      const cBtn = elem('button', `ac-cat-btn ${achieveCurCat === c.id ? 'active' : ''}`, c.name);
      cBtn.type = 'button';
      cBtn.addEventListener('click', () => {
        achieveCurCat = c.id;
        renderAchieveModal();
      });
      catBar.append(cBtn);
    });

    const hideWrap = elem('label', 'ac-hide-wrap');
    hideWrap.title = T('달성 완료한 과제를 목록에서 숨깁니다', 'Hide completed challenges');
    const hideCb = document.createElement('input');
    hideCb.type = 'checkbox';
    hideCb.id = 'acHideDone';
    hideCb.checked = !!achieveHideDone;
    hideCb.addEventListener('change', (e) => {
      achieveHideDone = !!e.target.checked;
      store.set('ac_hide_done', achieveHideDone);
      renderAchieveModal();
    });
    const hideTxt = elem('span', 'ac-hide-txt', T('완료한 업적 숨기기', 'Hide completed'));
    hideWrap.append(hideCb, hideTxt);

    toolbar.append(catBar, hideWrap);
    secBadges.append(toolbar);

    let filtered = achieveCurCat === 'all'
      ? ALL_ACHIEVEMENTS
      : ALL_ACHIEVEMENTS.filter((a) => a.cat === achieveCurCat);

    if (achieveHideDone) {
      filtered = filtered.filter((a) => !isAchieved(a, sessions));
    }

    const grid = elem('div', 'ac-grid');
    if (filtered.length === 0) {
      const empty = elem('div', 'ac-empty-msg');
      empty.textContent = achieveHideDone
        ? T('🎉 해당 항목의 모든 도전 과제를 달성했습니다! 멋진 성과입니다!', '🎉 All challenges in this category have been completed! Awesome!')
        : T('도전 과제가 없습니다.', 'No challenges found.');
      grid.append(empty);
    } else {
      for (const ach of filtered) {
        const unlockedSessions = sessions.filter((s) => {
          if (ach.check.length === 0) return ach.check();
          return ach.check(s, getSessionStats(s));
        });
        const isUnlocked = isAchieved(ach, sessions);

        const card = elem('div', `ac-card ${isUnlocked ? 'unlocked' : 'locked'}`);

        const head = elem('div', 'ac-card-head');
        const titleWrap = elem('div', 'ac-card-title-wrap');
        const name = elem('span', 'ac-card-name', `${ach.icon} ${T(ach.name, ach.en)}`);
        const catBadge = elem('span', 'ac-card-cat',
          ach.cat === 'onboarding' ? T('🏢 기초 온보딩', '🏢 Onboarding') :
          ach.cat === 'development' ? T('⚡ 실전 개발', '⚡ Development') :
          T('👑 승진 마스터', '👑 Mastery')
        );
        titleWrap.append(name, catBadge);

        const metaWrap = elem('div', 'ac-card-meta-wrap');
        const expBadge = elem('span', 'ac-card-exp', `+${ach.exp} EXP`);
        const status = elem(
          'span',
          'ac-card-status',
          isUnlocked ? T('달성 완료 ✓', 'Completed ✓') : T('도전 중 🔒', 'Locked 🔒')
        );
        metaWrap.append(expBadge, status);
        head.append(titleWrap, metaWrap);

        const desc = elem('div', 'ac-card-desc', T(ach.desc, ach.enDesc));

        const guideBox = elem('div', 'ac-card-guide');
        const guideTxt = elem('span', 'ac-card-guide-txt', `💡 ${T('달성 방법: ', 'How to achieve: ')}${T(ach.guide, ach.enGuide)}`);
        guideBox.append(guideTxt);

        if (!isUnlocked && ach.action) {
          const actBtn = elem('button', 'ac-card-action-btn', T('👉 바로가기', '👉 Go'));
          actBtn.type = 'button';
          actBtn.title = T('해당 기능으로 바로 이동하여 도전합니다', 'Jump directly to this feature');
          actBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            ach.action();
          });
          guideBox.append(actBtn);
        }

        const who = elem('div', 'ac-card-who');
        if (isUnlocked) {
          if (unlockedSessions.length > 0) {
            who.textContent = T(
              `🎖️ 달성 직원: ${unlockedSessions.map((s) => displayName(s)).join(', ')}`,
              `🎖️ Achieved by: ${unlockedSessions.map((s) => displayName(s)).join(', ')}`
            );
          } else {
            who.textContent = T('🎖️ 도전 과제 달성 완료! (오피스 전체 적용)', '🎖️ Challenge Completed! (Applied team-wide)');
          }
        } else {
          who.textContent = T(
            '🔒 아직 달성하지 못한 과제입니다. 안내된 방법을 따라 도전해 보세요!',
            '🔒 Not yet achieved. Follow the guide above to complete it!'
          );
        }

        card.append(head, desc, guideBox, who);
        grid.append(card);
      }
    }
    secBadges.append(grid);
  }

  // 2. 직원 직급 랭킹 탭 (명예의 전당)
  if (secRanks && achieveCurTab === 'ranks') {
    secRanks.replaceChildren();
    const list = elem('div', 'ac-lead-list');
    const sorted = [...sessions].sort((a, b) => calcSessionExp(b) - calcSessionExp(a));

    if (sorted.length === 0) {
      const emp = elem('div', 'wb-empty');
      emp.textContent = T('아직 출근한 직원이 없습니다.', 'No employees active yet.');
      secRanks.append(emp);
      return;
    }

    sorted.forEach((s, idx) => {
      const rk = calcSessionRank(s);
      const badges = calcSessionBadges(s).filter((b) => b.unlocked);

      const item = elem('div', 'ac-lead-item');

      const rankText = idx === 0 ? '🥇 1위' : idx === 1 ? '🥈 2위' : idx === 2 ? '🥉 3위' : `#${idx + 1}`;
      const rankEl = elem('div', 'ac-lead-rank', rankText);

      const info = elem('div', 'ac-lead-info');
      const titleRow = elem('div', 'ac-lead-title-row');
      const badge = elem('span', 'rank-badge', `Lv.${rk.lv}`);
      const rTitle = elem('span', 'rank-title', rk.name);
      const name = elem('span', 'ac-lead-name', displayName(s));
      const team = s.team ? elem('span', 'team-tag', s.team) : null;
      titleRow.append(badge, rTitle, name);
      if (team) titleRow.append(team);

      const barWrap = elem('div', 'ac-lead-bar-wrap');
      const bar = elem('div', 'ac-lead-bar');
      const fill = elem('div', 'ac-lead-bar-fill');
      fill.style.width = `${rk.pct}%`;
      bar.append(fill);
      const expText = elem('span', 'ac-lead-exp', `${rk.exp} EXP`);
      barWrap.append(bar, expText);

      const badgeWrap = elem('div', 'ac-lead-badges');
      if (badges.length > 0) {
        for (const b of badges) {
          const bPin = elem('span', 'rank-badge-item unlocked', `${b.icon} ${b.name}`);
          bPin.title = b.tip;
          badgeWrap.append(bPin);
        }
      } else {
        const noBadge = elem('span', 'dim', T('(보유 업적 없음)', '(No badges yet)'));
        noBadge.style.fontSize = '10.5px';
        badgeWrap.append(noBadge);
      }

      info.append(titleRow, barWrap, badgeWrap);

      const btn = elem('button', 'ac-lead-btn', T('👉 이동', '👉 Go'));
      btn.title = T('이 직원의 상세 화면으로 이동합니다', 'Jump to this employee');
      btn.addEventListener('click', () => {
        closeAchieveModal();
        selectSession(s.id);
      });

      item.append(rankEl, info, btn);
      list.append(item);
    });
    secRanks.append(list);
  }
}

function initAchieveModal() {
  if (!achieveModalEl) return;
  $('acClose')?.addEventListener('click', closeAchieveModal);
  achieveModalEl.addEventListener('click', (e) => {
    if (e.target === achieveModalEl) closeAchieveModal();
  });
  $('acTabBadges')?.addEventListener('click', () => {
    achieveCurTab = 'badges';
    renderAchieveModal();
  });
  $('acTabRanks')?.addEventListener('click', () => {
    achieveCurTab = 'ranks';
    renderAchieveModal();
  });
}

function cancelQueuedSay(sid) {
  if (!sid) return;
  const msg = T('🗑️ 대기 중이던 지시를 취소했어요', '🗑️ Cancelled queued instruction');
  if (DEMO) {
    const r = remoteState?.s?.[sid];
    if (r?.said?.state === 'queued') delete r.said;
    petNote = { sid, at: Date.now(), text: msg };
    remoteNote = { sid, at: Date.now(), text: msg };
    renderPet();
    if (selectedId) renderPanel();
    return;
  }
  if (REMOTE) {
    postInbox({ k: 'cancel', sid }).then((ok) => {
      remoteNote = { sid, at: Date.now(), text: ok ? msg : lastPostError };
      renderPanel();
    });
    return;
  }
  send({ cmd: 'pet-cancel-say', sid });
  petNote = { sid, at: Date.now(), text: msg };
  remoteNote = { sid, at: Date.now(), text: msg };
  renderPet();
  if (selectedId) renderPanel();
}

function sendPetSay() {
  const s = resolvePetSession();
  const text = petInputEl ? petInputEl.value.trim() : '';
  if (!s || !text) return;
  petInputEl.value = '';
  if (text.startsWith('/')) {
    wakeTerminal(s.id, text);
    petNote = { sid: s.id, at: Date.now(), text: T(`📋 슬래시 명령(${text})은 터미널 전용입니다! 터미널로 이동됨 (Ctrl+V 후 Enter)`, `📋 Slash command (${text}) is terminal-only! Switched to terminal (Ctrl+V then Enter)`) };
    renderPet();
    return;
  }
  if (DEMO) {
    if (!remoteState) remoteState = { installed: true, away: false, s: {} };
    if (!remoteState.s) remoteState.s = {};
    const cur = remoteState.s[s.id] || { listen: true };
    cur.said = { text: text.slice(0, 1000), t: Date.now(), state: 'delivered' };
    remoteState.s[s.id] = cur;
    s.prompt = text.slice(0, 200);
    petNote = { sid: s.id, at: Date.now(), text: T(`✅ "${text.slice(0, 28)}" 지시를 전했어요`, `✅ Sent "${text.slice(0, 28)}"`) };
    renderPet();
    if (selectedId) renderPanel();
    return;
  }
  send({ cmd: 'pet-say', sid: s.id, text: text.slice(0, 1000) });
  playRetroSound('send');
  const r = remoteOf(s.id);
  const listening = r && r.listen;
  petNote = {
    sid: s.id,
    at: Date.now(),
    text: listening
      ? T('🚀 지시를 보냈어요…', '🚀 Instruction sent…')
      : T('⏳ 최초 1회 연결 필요 — [🖥️ 터미널로] 클릭', '⏳ First connection needed — click [🖥️ Terminal]')
  };
  renderPet();
  if (!listening && !store.get('skipConnectGuide', false)) {
    openConnectGuide(s.id);
  }
}

function renderPet() {
  if (!WIDGET_WINDOW && !(DEMO && petOn())) return;
  ensurePetDom();

  const pref = petSessionPref();
  const s = resolvePetSession();
  const compact = petCompactOn();
  const noBg = petNoBgOn();

  // 세션 선택 드롭다운 갱신: 바 안에는 깔끔하게 직원 이름만 넣고, 상태는 하단 상태 바에 표시
  if (document.activeElement !== petSelEl) {
    const autoLabel = s && !pref
      ? T(`🌟 자동 · ${displayName(s)}`, `🌟 Auto · ${displayName(s)}`)
      : T('🌟 자동 (확인 필요·최근 순)', '🌟 Auto (waiting / recent)');
    const opts = [new Option(autoLabel, '')];
    for (const item of data.sessions) {
      const listening = remoteOf(item.id)?.listen;
      opts.push(new Option(`${item.icon} ${displayName(item)}${listening ? ' · 📡' : ''}`, item.id));
    }
    petSelEl.replaceChildren(...opts);
    petSelEl.value = pref && data.sessions.some((x) => x.id === pref) ? pref : '';
  }

  petBgBtn.classList.toggle('on', !noBg);
  petBgBtn.title = noBg
    ? T('배경 보이기 (구석 방·창문·화분·카펫 표시)', 'Show background (corner room, window, plant, rug)')
    : T('배경 숨기기 (직원·의자·책상·컴퓨터만 보기)', 'Hide background (show only employee, chair, desk, and computer)');

  petFoldBtn.classList.toggle('on', !compact);
  petFoldBtn.title = compact ? T('지시·답변 칸 펼치기', 'Expand instruction & reply bar') : T('지시·답변 칸 접기(캐릭터만 작게 보기)', 'Collapse instruction & reply bar');

  if (petMode === 'island') {
    petEl.classList.add('pet-island');
    if (petAlertEl) petAlertEl.hidden = true;
    renderPetIsland(s);
    if (petIslandEl) petIslandEl.hidden = false;
    syncPetHeight();
    return;
  }
  petEl.classList.remove('pet-island');
  if (petIslandEl) {
    petIslandEl.hidden = true;
    petIslandEl.replaceChildren();
  }

  if (!s) {
    petEl.classList.remove('compact', 'nobg');
    petAlertEl.hidden = true;
    petStageEl.hidden = true;
    petAsksEl.replaceChildren();
    petFeedEl.hidden = true;
    petCmdWrap.hidden = true;
    petEmptyEl.hidden = false;
    petEmptyEl.textContent = T('🏢 아직 출근한 직원이 없어요.\nClaude Code에서 대화를 시작하면 여기에 나타납니다.',
      '🏢 Nobody is at work yet.\nStart a conversation in Claude Code and they will appear here.');
    syncPetHeight();
    return;
  }

  petEmptyEl.hidden = true;
  petStageEl.hidden = false;
  const a = actOf(s);
  if (petStopBtn) petStopBtn.hidden = !((a.busy || a.wait) && s.act !== 'away');
  petStageEl.classList.toggle('wait', !!a.wait);
  petStageEl.classList.toggle('nobg', noBg);
  petEl.classList.toggle('nobg', noBg);

  // 다른 직원이 승인·질문 답변을 기다리고 있으면 상단에 원클릭 전환 배지를 띄운다
  const otherWaiting = data.sessions.filter((x) => x.id !== s.id && x.act !== 'away' && (actOf(x).wait || (remoteOf(x.id)?.asks?.length > 0)));
  if (otherWaiting.length > 0) {
    petAlertTargetId = otherWaiting[0].id;
    petAlertEl.hidden = false;
    const firstNm = displayName(otherWaiting[0]);
    const extra = otherWaiting.length > 1 ? T(` 외 ${otherWaiting.length - 1}명`, ` +${otherWaiting.length - 1}`) : '';
    petAlertEl.replaceChildren(
      elem('span', '', T(`✋ ${firstNm}${extra} 확인 대기 중`, `✋ ${firstNm}${extra} waiting`)),
      elem('span', 'go', T('전환 ▸', 'Switch ▸'))
    );
  } else {
    petAlertTargetId = '';
    petAlertEl.hidden = true;
  }

  const nmSpan = elem('span', 'nm-txt', `${s.icon} ${displayName(s)}`);
  const teamSpan = elem('span', 'team-tag', teamLabel(s.team));
  petBarNm.replaceChildren(nmSpan, teamSpan);
  const dur = s.since ? ' · ' + fmtDur(now() - s.since) : '';
  petBarSt.textContent = `${a.label}${dur}`;
  petBarSt.style.color = a.tone;

  // 승인 요청 카드 (접혀 있어도 승인 요청이 오면 바로 보여 준다)
  const r = remoteOf(s.id) || {};
  const asks = r.asks || [];
  petEl.classList.toggle('compact', compact && asks.length === 0 && otherWaiting.length === 0);

  const keep = new Set(asks.map((x) => x.id));
  for (const [id, el] of petAskEls) if (!keep.has(id) || el.dataset.sid !== s.id) { el.remove(); petAskEls.delete(id); }
  for (const ask of asks) {
    let el = petAskEls.get(ask.id);
    if (!el) { el = makePetAsk(s.id, ask); petAskEls.set(ask.id, el); }
    petAsksEl.append(el);
    el.querySelector('.age').textContent = ' · ' + fmtAgo(now() - ask.t);
    for (const b of el.querySelectorAll('button')) b.disabled = petAnswered.has(ask.id);
  }
  for (const [id, at] of petAnswered) if (Date.now() - at > 120000) petAnswered.delete(id);

  // 마지막 답변 또는 마지막 지시 요약
  if (compact) {
    petFeedEl.hidden = true;
    petCmdWrap.hidden = true;
  } else {
    petCmdWrap.hidden = false;
    const useStop = r.reply && r.reply.text && (!s.reply || (r.reply.t || 0) >= (s.last || 0));
    if (useStop) {
      petFeedEl.hidden = false;
      petFeedHd.textContent = T(`💬 마지막 답 · ${fmtTime(r.reply.t)}`, `💬 Last answer · ${fmtTime(r.reply.t)}`);
      petFeedTx.textContent = r.reply.text;
    } else if (s.reply) {
      petFeedEl.hidden = false;
      petFeedHd.textContent = T(`💬 마지막 답 · ${fmtTime(s.last)}`, `💬 Last answer · ${fmtTime(s.last)}`);
      petFeedTx.textContent = s.reply;
    } else if (s.prompt) {
      petFeedEl.hidden = false;
      petFeedHd.textContent = T('📨 마지막 지시', '📨 Last prompt');
      petFeedTx.textContent = s.prompt;
    } else {
      petFeedEl.hidden = true;
    }

    const connected = DEMO || claudeConnected();
    petInputEl.disabled = !connected;
    petSendBtn.disabled = !connected;
    const isQueued = connected && r.said && r.said.state === 'queued';
    petCancelBtn.hidden = !isQueued;
    if (petWakeBtn) petWakeBtn.hidden = !(isQueued && !r.listen);
    if (petGuideBtn) petGuideBtn.hidden = !(isQueued && !r.listen);
    if (!connected) {
      petNoteEl.textContent = T('🔕 지시·승인을 쓰려면 메인 설정(⚙️)에서 [Claude Code 연결]을 켜 주세요.',
        '🔕 Connect to Claude Code in ⚙️ Settings to send instructions or approve.');
    } else if (petNote && petNote.sid === s.id && Date.now() - petNote.at < 25000) {
      if (r.said && r.said.t > petNote.at) petNote = null;
      petNoteEl.textContent = petNote ? petNote.text : saidText(r.said, a.busy || asks.length > 0);
    } else if (r.said) {
      petNoteEl.textContent = saidText(r.said, a.busy || asks.length > 0);
    } else {
      petNoteEl.textContent = r.listen
        ? T('📡 대기 중 — 바로 이 대화에 지시를 던질 수 있어요', '📡 Ready — send an instruction to continue this conversation')
        : a.busy
          ? T('💻 작업 중 — 보내면 지금 작업이 끝나는 대로 전달돼요', '💻 Working — delivered right after the current work ends')
          : T('⏳ 대기 연결 전 — 터미널에서 한 번 응답을 마치면 바로 연결돼요', '⏳ Not listening yet — finishes connecting after next reply in terminal');
    }
  }

  drawPetFrame(performance.now());
  syncPetHeight();
}

function syncPetHeight() {
  if (!WIDGET_WINDOW) return;
  const guideOpen = connectGuideEl && !connectGuideEl.hidden;
  let h;
  if (guideOpen) {
    const box = connectGuideEl.querySelector('.cg-box');
    h = box ? Math.ceil(box.getBoundingClientRect().height) + 16 : 460;
  } else {
    h = Math.ceil(petEl.getBoundingClientRect().height);
  }
  const minH = (petMode === 'island') ? 40 : 120;
  if (h >= 40 && Math.abs(h - lastPetH) > 1) {
    lastPetH = h;
    send({ cmd: 'pet-size', height: Math.min(620, Math.max(h, minH)) });
  }
}

/** 바탕화면 구석의 작은 방(또는 배경 숨김 시 직원·의자·책상·컴퓨터만)을 그린다. */
function drawPetFrame(ts) {
  if (!petCanvasEl || !petCtx) return;
  const s = resolvePetSession();
  if (!s) return;

  const noBg = petNoBgOn();
  const PW = 94, PH = 64;   // 구석 방과 배경 숨김 모두 64로 두어 마우스 오버 시 상단 툴바가 말풍선을 가리지 않고 여백을 확보
  const dprNow = window.devicePixelRatio || 1;
  const cssWidth = Math.max(220, petStageEl.clientWidth || 266);
  const scale = cssWidth / PW;
  const k = Math.max(2, Math.round(scale * dprNow));
  const cssHeight = Math.round(PH * scale);

  if (petArt.width !== PW || petArt.height !== PH) { petArt.width = PW; petArt.height = PH; }
  if (petCanvasEl.width !== PW * k || petCanvasEl.height !== PH * k) {
    petCanvasEl.width = PW * k;
    petCanvasEl.height = PH * k;
    petCanvasEl.style.height = cssHeight + 'px';
    syncPetHeight();
  }

  if (art.width < PW || art.height < PH) { art.width = Math.max(art.width, PW); art.height = Math.max(art.height, PH); }
  g.clearRect(0, 0, PW, PH);

  if (petMode === 'shimeji') {
    petEl.classList.add('pet-shimeji');
    g.clearRect(0, 0, PW, PH);

    if (shimejiState === 'walk') {
      shimejiX += shimejiVx * 0.7;
      if (shimejiX < 8) { shimejiX = 8; shimejiVx = 1; }
      else if (shimejiX > PW - 22) { shimejiX = PW - 22; shimejiVx = -1; }
      if (ts > shimejiNextSwitch) {
        shimejiState = Math.random() < 0.4 ? 'jump' : 'idle';
        shimejiNextSwitch = ts + 2000 + Math.random() * 3000;
        if (shimejiState === 'jump') shimejiVy = -3.2;
      }
    } else if (shimejiState === 'jump') {
      shimejiY += shimejiVy;
      shimejiVy += 0.4;
      if (shimejiY >= 0) {
        shimejiY = 0;
        shimejiVy = 0;
        shimejiState = 'walk';
        shimejiNextSwitch = ts + 3000 + Math.random() * 3000;
      }
    } else {
      if (ts > shimejiNextSwitch) {
        shimejiState = 'walk';
        shimejiVx = Math.random() < 0.5 ? 1 : -1;
        shimejiNextSwitch = ts + 3000 + Math.random() * 4000;
      }
    }

    const L = looks(s.id);
    const sy = Math.max(8, 34 + Math.round(shimejiY));
    drawShimejiPerson(Math.round(shimejiX), sy, L, shimejiVx, ts, s, shimejiState);

    petCtx.setTransform(1, 0, 0, 1, 0, 0);
    petCtx.imageSmoothingEnabled = false;
    petCtx.clearRect(0, 0, petCanvasEl.width, petCanvasEl.height);
    petCtx.drawImage(art, 0, 0, PW, PH, 0, 0, PW * k, PH * k);

    petCtx.setTransform((PW * k) / cssWidth, 0, 0, (PH * k) / cssHeight, 0, 0);
    const cx = (shimejiX + 6) * scale;
    const ay = (sy - 2) * scale;

    if (currentDevMeme && ts < devMemeExpires) {
      drawPetBubble(petCtx, cx, ay, currentDevMeme, BUBBLE.thought, ts, cssWidth);
    } else {
      const a = actOf(s);
      if (a.bubble !== 'none') {
        let text = `${s.icon} ${s.text}`;
        if (a.bubble === 'thought') text += '.'.repeat(1 + tick(ts, 450, 3));
        drawPetBubble(petCtx, cx, ay, text, BUBBLE[a.bubble] || BUBBLE.normal, ts, cssWidth);
      }
    }
    markQuest('shimeji_walk');
    return;
  }
  petEl.classList.remove('pet-shimeji');

  let deskObj;
  if (noBg) {
    // 배경 숨김: 마우스 오버 시 상단 조작 바가 나타나도 [생각 중...] 등 말풍선을 가리지 않도록
    // desk.y = 6 으로 배치하여 상단 툴바와 말풍선 사이에 깔끔한 여백을 확보
    const hasInterns = (s.interns || []).length > 0;
    deskObj = { s, x: hasInterns ? 20 : 14, y: 6 };
    drawDesk(deskObj, ts);
  } else {
    // 구석 방 배경: 벽·창문·시계·화분·마루·팀 카펫 + 직원·의자·책상·컴퓨터
    // desk.y = 8 로 두어 위쪽 말풍선 여백이 과하지 않으면서 책상 다리(y+53=61) 아래에도 바닥 여백(3px)을 확보
    const wallH = 29;
    R(0, 0, PW, wallH, PAL.wall);
    for (let x = 0; x < PW; x += 10) R(x, 0, 1, wallH - 8, PAL.wallStripe);
    R(0, wallH - 8, PW, 6, PAL.wainscot);
    R(0, wallH - 8, PW, 1, PAL.wainscotHi);
    R(0, wallH - 2, PW, 2, PAL.base);
    R(0, 0, 4, PH, PAL.wainscot);
    R(4, 0, 1, PH, PAL.wainscotHi);

    R(5, wallH, PW - 5, PH - wallH, PAL.floorA);
    for (let y = wallH, row = 0; y < PH; y += 7, row++) {
      if (row % 2) R(5, y + 1, PW - 5, 6, PAL.floorB);
      R(5, y, PW - 5, 1, PAL.floorLine);
    }

    const d = new Date();
    const sky = skyAt(d.getHours() + d.getMinutes() / 60);
    drawWindow(52, 2, sky, ts, 0);
    drawClock(10, 3, d);
    drawPlant(7, wallH - 12);

    const rug = teamColor(s.team);
    R(15, wallH + 3, 72, 29, shade(rug, 0.78));
    R(16, wallH + 4, 70, 27, rug);

    deskObj = { s, x: 20, y: 8 };
    drawDesk(deskObj, ts);
  }

  // 메인 캔버스(petCanvasEl)로 정수 배율 복사
  petCtx.setTransform(1, 0, 0, 1, 0, 0);
  petCtx.imageSmoothingEnabled = false;
  petCtx.clearRect(0, 0, petCanvasEl.width, petCanvasEl.height);
  petCtx.drawImage(art, 0, 0, PW, PH, 0, 0, PW * k, PH * k);

  // 말풍선(선명한 해상도로 위에 그리기, 꼬리가 직원 머리를 가리키도록)
  const a = actOf(s);
  if (a.bubble !== 'none') {
    const h = headAnchor(deskObj, a.pose);
    const cx = h.x * scale, ay = h.y * scale;
    petCtx.setTransform((PW * k) / cssWidth, 0, 0, (PH * k) / cssHeight, 0, 0);
    if (a.bubble === 'zzz') {
      drawPetZzz(petCtx, cx, ay, ts);
    } else {
      let text = `${s.icon} ${s.text}`;
      if (a.bubble === 'thought') text += '.'.repeat(1 + tick(ts, 450, 3));
      drawPetBubble(petCtx, cx, ay, text, BUBBLE[a.bubble] || BUBBLE.normal, ts, cssWidth);
    }
  }
}

function drawPetBubble(c, cx, anchorY, text, st, t, maxW) {
  const fb = 11.5;
  c.font = `600 ${fb}px ${FONT}`;
  c.textAlign = 'left';
  c.textBaseline = 'middle';
  const padX = 7, h = fb + 8;
  let label = text;
  if (c.measureText(label).width > maxW - 20) {
    const chars = Array.from(label);
    while (chars.length > 4 && c.measureText(chars.join('') + '…').width > maxW - 20) chars.pop();
    label = chars.join('') + '…';
  }
  const w = Math.ceil(c.measureText(label).width) + padX * 2;
  const lift = st.pulse ? Math.round(Math.abs(Math.sin(t / 220)) * 2) : 0;
  const x = clamp(Math.round(cx - w / 2), 5, Math.max(5, maxW - w - 5));
  const y = Math.max(2, Math.round(anchorY - h - 6 - lift));

  c.lineWidth = 1.8;
  c.strokeStyle = st.border;
  c.fillStyle = st.bg;
  c.beginPath();
  c.roundRect(x, y, w, h, 6);
  c.fill();
  c.stroke();

  // 말풍선 꼬리(생각 방울 또는 삼각형 꼬리)
  const tx = clamp(Math.round(cx), x + 8, x + w - 8);
  if (st.thought) {
    c.beginPath();
    c.arc(tx - 1, y + h + 3, 2.2, 0, Math.PI * 2);
    c.fill();
    c.stroke();
  } else {
    c.beginPath();
    c.moveTo(tx - 4, y + h - 1);
    c.lineTo(tx, y + h + 5);
    c.lineTo(tx + 4, y + h - 1);
    c.closePath();
    c.fill();
    c.beginPath();
    c.moveTo(tx - 4, y + h);
    c.lineTo(tx, y + h + 5);
    c.lineTo(tx + 4, y + h);
    c.stroke();
  }

  c.fillStyle = st.fg;
  c.fillText(label, x + padX, y + h / 2 + 0.5);
}

function drawPetZzz(c, cx, y, t) {
  c.textAlign = 'left';
  c.textBaseline = 'alphabetic';
  for (let i = 0; i < 3; i++) {
    const p = (t / 1500 + i / 3) % 1;
    c.globalAlpha = 1 - p;
    c.fillStyle = '#ddd8f2';
    c.font = `800 ${10 + i * 2}px ${FONT}`;
    c.fillText('z', cx + 3 + p * 8 + i * 4, y - 2 - p * 16 - i * 3);
  }
  c.globalAlpha = 1;
}
setInterval(() => {
  if (widgetOn()) renderWidget();
  else if (!selectedId || panelEl.hidden || drawerFolded) return;
  else {
    const s = data.sessions.find((x) => x.id === selectedId);
    if (s) $('pLast').textContent = s.last ? `${fmtTime(s.last)} (${fmtAgo(now() - s.last)})` : '-';
  }
  checkDevMemeTimer();
}, 5000);   // "N분 전" 갱신 및 아이스브레이킹 밈 주기 확인

/** 책상이 하나도 없을 때 왜 비었는지 알려 준다. */
function refreshEmpty(shown) {
  emptyEl.hidden = shown > 0;
  if (shown > 0) return;
  const [big, msg, dim] =
    REMOTE && remoteError && data.sessions.length === 0 ? [T('📱 PC와 아직 이어지지 않았어요', '📱 Not connected to the PC yet'), remoteError, '']
    : data.sessions.length === 0 ? [T('🏢 아직 출근한 직원이 없어요', '🏢 Nobody has come to work yet'),
      T('Claude Code에서 대화를 시작하면 책상에 직원이 나타납니다.', 'Start a conversation in Claude Code and an employee appears at a desk.'),
      T(`최근 ${windowLabel()} 안에 대화한 세션을 봅니다(⚙️ 설정 → 표시 기간).`, `Shows sessions active in the last ${windowLabel()} (⚙️ Settings → Display period).`)]
    : searchQuery.trim() ? [T(`🔍 '${searchQuery.trim()}' 검색 결과가 없어요`, `🔍 No matches for '${searchQuery.trim()}'`),
      T('다른 검색어를 입력하거나 Esc를 눌러 검색을 닫아 보세요.', 'Try another keyword or press Esc to clear the search.'), '']
    : waitOnly ? [T('🙌 기다리는 직원이 없어요', '🙌 Nobody is waiting'), T('모두 알아서 일하거나 쉬는 중입니다.', 'Everyone is working or resting on their own.'),
      T("⚙️ 설정 → 직원 표시 → '✋ 기다리는 직원만 보기'를 끄면 모두 보입니다.", "Turn off ⚙️ Settings → Employees → '✋ Only show who's waiting' to see everyone.")]
    : pickMode === 'manual' && pickedCount() === 0 ? [T('🗂️ 보일 직원을 아직 고르지 않았어요', '🗂️ No employees picked yet'),
      T('⚙️ 설정 → 직원 표시에서 체크하면 책상에 앉습니다.', 'Check them in ⚙️ Settings → Employees to seat them.'), T('고르는 방식: 직접 고르기', 'How to pick: Choose manually')]
    : [T('🚪 모두 퇴근했어요', '🚪 Everyone has gone home'),
      T("⚙️ 설정에서 '퇴근한 직원 보이기'를 켜면 빈 책상이 보입니다.", "Turn on 'Show employees who went home' in ⚙️ Settings to see their empty desks."), ''];
  $('emptyBig').textContent = big;
  $('emptyMsg').textContent = msg;
  $('emptyDim').textContent = dim;
}

function refreshButtons() {
  btnAway.classList.toggle('off', !showAway);
  btnAway.title = showAway ? T('퇴근한 직원 숨기기', 'Hide employees who went home') : T('퇴근한 직원 보이기', 'Show employees who went home');
  btnPin.classList.toggle('on', pinned);
  btnPin.title = pinned ? T('항상 위에 표시 끄기', 'Turn off always on top') : T('항상 위에 표시', 'Always on top');
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
  if (v) markQuest('pin_window');
}

function setOfficeName(v) {
  officeName = v.trim();
  store.set('officeName', officeName);
  refreshHud();   // 창 제목
  shareToApp();
  if (officeName && officeName !== DEFAULT_OFFICE) markQuest('rename_company');
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
if (btnPet) btnPet.addEventListener('click', () => setPet(!petOn()));
btnWidget.addEventListener('click', () => setWidget(!widgetOn()));
btnToday.addEventListener('click', () => {   // 📊 오늘의 근무 일지 — 다시 누르면 닫기
  const open = selectedId === REPORT_ID && !panelEl.hidden;
  if (!open && !settingsEl.hidden) setSettingsOpen(false);
  selectedId = open ? null : REPORT_ID;
  drawerFolded = false;
  renderPanel();
  if (!open) markQuest('view_worklog');
});
if (btnAchieve) {
  btnAchieve.addEventListener('click', () => {
    if (achieveModalEl && !achieveModalEl.hidden) {
      closeAchieveModal();
    } else {
      openAchieveModal();
    }
  });
}
if (btnShop) btnShop.addEventListener('click', openShopModal);

// 📢 브로드캐스트 모달 이벤트 바인딩
if (btnBroadcast) btnBroadcast.addEventListener('click', openBroadcastModal);
if (bcCloseBtn) bcCloseBtn.addEventListener('click', closeBroadcastModal);
if (bcSendBtn) bcSendBtn.addEventListener('click', sendBroadcast);
const bcSelectAllBtn = $('bcSelectAll'), bcDeselectAllBtn = $('bcDeselectAll');
if (bcSelectAllBtn) bcSelectAllBtn.addEventListener('click', () => {
  if (bcSessionListEl) bcSessionListEl.querySelectorAll('input[type="checkbox"]').forEach(c => c.checked = true);
});
if (bcDeselectAllBtn) bcDeselectAllBtn.addEventListener('click', () => {
  if (bcSessionListEl) bcSessionListEl.querySelectorAll('input[type="checkbox"]').forEach(c => c.checked = false);
});
document.querySelectorAll('.bc-preset-btn').forEach((btn) => {
  btn.addEventListener('click', () => {
    if (bcPromptInput) {
      bcPromptInput.value = btn.dataset.prompt || '';
      bcPromptInput.focus();
    }
  });
});

// 🌙 야간 당직 모달 이벤트 바인딩
if (btnNightShift) btnNightShift.addEventListener('click', openNightShiftModal);
if (nsCloseBtn) nsCloseBtn.addEventListener('click', closeNightShiftModal);
if (nsToggleBtn) nsToggleBtn.addEventListener('click', toggleNightShift);
if (nsCopyBtn) nsCopyBtn.addEventListener('click', copyNightShiftReport);

if (btnCoins) btnCoins.addEventListener('click', openShopModal);
if (shopCloseBtn) shopCloseBtn.addEventListener('click', closeShopModal);
btnPin.addEventListener('click', () => setPinned(!pinned));
btnHook.addEventListener('click', toggleHooks);

// Claude Code 연결 = 상태 알림(data.hooks)과 휴대폰 지시·승인(remoteState.installed)을 한 번에 넣고 뺀다(앱이 확인 창을 띄운다)
const claudeConnected = () => !!data.hooks && data.hooks.state !== 'off' && !!remoteState?.installed;

function hookStatus() {
  const h = data.hooks;
  if (!h) return { text: T('확인 중…', 'Checking…'), tone: 'var(--dim)' };
  if (h.state === 'error') return { text: '⚠️ ' + (h.error || T('알림을 받지 못하는 중', 'Not receiving')), tone: '#ffb347' };
  if (h.state === 'off') return { text: T('🔕 연결 안 됨 — Claude가 승인을 기다리는지 짐작만 해요', '🔕 Not connected — whether Claude is waiting is only guessed'), tone: 'var(--dim)' };
  if (!remoteState?.installed) return { text: T('⚠️ 휴대폰 지시·승인까지 받으려면 [다시 연결]', '⚠️ Click [Reconnect] to also take phone instructions and approvals'), tone: '#ffb347' };
  return { text: T('🔔 연결됨', '🔔 Connected') + (h.last ? T(` · 마지막 알림 ${fmtTime(h.last)}`, ` · last notice ${fmtTime(h.last)}`) : T(' · 아직 받은 알림 없음', ' · nothing received yet')), tone: '#7dffa0' };
}

/** 상단 🔕 버튼: 연결이 안 됐거나 덜 됐을 때만 보인다(누르면 연결). 다 연결되면 숨긴다. */
function refreshHookButton() {
  const h = data.hooks;
  btnHook.hidden = !h || REMOTE || (h.state === 'on' && !!remoteState?.installed);   // 연결은 PC에서만
  if (btnHook.hidden) return;
  btnHook.classList.toggle('off', h.state === 'off');
  btnHook.classList.toggle('warn', h.state === 'error');
  btnHook.textContent = h.state === 'error' ? '⚠️' : '🔕';
  btnHook.title = hookStatus().text + T('\n클릭: Claude Code와 연결', '\nClick: connect to Claude Code');
}

// ───────────────────────────── 휴대폰 연결(앱 쪽 설정) ─────────────────────────────
// 앱(C#)이 {type:"cloud"} 로 상태를 알려 준다: configured, enabled, state(off·starting·on·error), error, notice, last, local, phoneEdit, url(QR 주소)
// 휴대폰 연결은 무료 서버(모두가 함께 쓰는 Firebase — 내 PC·휴대폰만 아는 키로 잠가서 보내므로 서버도 남도 못 읽음)를 거친다. 따로 설정할 것은 없다.

let cloud = null;
let cloudQrShown = false;      // QR에는 해독 키가 들어 있어 누를 때만 보인다

function onCloud(msg) {
  cloud = msg;
  refreshSettings();
  if (msg && (msg.enabled || msg.state === 'on')) markQuest('mobile_register');
}

// 앱 쪽 설정(app.json·시작 프로그램 등록·알림·새 버전) — 앱이 {type:"app", ...} 로 알려 준다
let appSettings = null;

function onApp(msg) {
  appSettings = msg;
  btnSettings.classList.toggle('dot', !!msg.update?.newer);   // 새 버전이 나오면 ⚙️ 에 점
  if (!WIDGET_WINDOW && !!msg.widget !== document.body.classList.contains('widget')) applyWidget();
  applyPetState();
  if (WIDGET_WINDOW || (DEMO && petOn())) renderPet();
  if (selectedId) renderPanel();
  refreshSettings();
}

/** 앱이 저장한 뒤 실제 값을 {type:"app"} 로 다시 알려 준다(못 바꿨으면 스위치가 되돌아간다). */
const setAppSetting = (key, value) => send({ cmd: 'app-setting', key, value });
const appReady = () => !DEMO && !!appSettings;
const openUrl = (url) => send({ cmd: 'open-url', url });   // 앱이 바깥 브라우저로 연다(정해 둔 주소만)

function cloudStatus() {
  const c = cloud;
  if (!c) return { text: T('확인 중…', 'Checking…'), tone: 'var(--dim)' };
  if (!c.enabled) return c.notice ? { text: c.notice, tone: '#ffb347' } : { text: T('꺼짐', 'Off'), tone: 'var(--dim)' };
  const note = c.notice ? ` · ${c.notice}` : '';   // 예: 무료 서버로 옮겼으니 QR을 다시 찍으라는 안내
  if (c.state === 'error') return { text: '⚠️ ' + (c.error || T('보내지 못하는 중', 'Not sending')) + note, tone: '#ffb347' };
  if (c.state === 'on') return { text: T(`✅ 연결됨 · 마지막 ${fmtTime(c.last)}`, `✅ Connected · last ${fmtTime(c.last)}`) + note, tone: '#7dffa0' };
  return { text: T('연결 중…', 'Connecting…') + note, tone: 'var(--dim)' };
}

const DONE_CHOICES = [[5, T('5분', '5 min')], [10, T('10분', '10 min')], [30, T('30분', '30 min')], [60, T('1시간', '1 hour')]];
const doneLabel = () => (DONE_CHOICES.find(([m]) => m === appSettings?.notifyDoneMinutes)
  || [0, T(`${appSettings?.notifyDoneMinutes ?? 10}분`, `${appSettings?.notifyDoneMinutes ?? 10} min`)])[1];

function updateStatus() {
  const u = appSettings?.update;
  if (!u) return { text: T('확인 중…', 'Checking…'), tone: 'var(--dim)' };
  const ins = u.install || {};   // 앱 안 업데이트 진행: downloading(퍼센트) → installing → (새 버전으로 다시 켜짐) / error
  if (ins.state === 'downloading') return { text: T(`⬇️ 새 버전 받는 중 ${ins.percent}%`, `⬇️ Downloading ${ins.percent}%`), tone: '#a9c8ff' };
  if (ins.state === 'installing') return { text: T('🔧 확인하고 바꾸는 중… 곧 다시 켜집니다', '🔧 Verifying and installing… restarting soon'), tone: '#a9c8ff' };
  if (ins.state === 'error') return { text: `⚠️ ${ins.error || T('업데이트하지 못했습니다', 'Update failed')}`, tone: '#ffb347' };
  if (u.newer) return { text: T(`🎉 새 버전 ${u.latest} 이 나왔어요`, `🎉 Version ${u.latest} is out`), tone: '#7dffa0' };
  if (u.error) return { text: `⚠️ ${u.error}`, tone: '#ffb347' };
  if (u.latest) return { text: T(`최신 버전입니다 · 확인 ${fmtTime(u.checkedAt)}`, `Up to date · checked ${fmtTime(u.checkedAt)}`), tone: 'var(--dim)' };
  return { text: u.checkedAt ? T('아직 공개된 버전이 없어요', 'No release yet') : T('아직 확인 전(켠 뒤 30초쯤)', 'Not checked yet (about 30 s after start)'), tone: 'var(--dim)' };
}

// ───────────────────────────── 설정 패널 ─────────────────────────────
// 항목을 늘릴 때는 SETTINGS 에 한 줄 추가하면 화면은 저절로 그려진다.
// 설정은 가운데 팝업 — 묶음(section)마다 tab(SETTINGS_TABS 의 id)과 소제목(title, 없으면 소제목 없이)을 정한다. 같은 탭의 묶음은 SETTINGS 순서대로.
//   toggle : get() / set(v)                       — 켜고 끄는 스위치
//   number : get() / set(v) / min / max / unit    — − 값 + 조절
//   choice : get() / set(v) / options[[값, 글]]   — 여러 개 중 하나(이름 아래 한 줄로)
//   action : label() / run() / status()           — 버튼(앱이 확인 창을 띄우는 일 등)과 지금 상태 글
//   text   : get() / set(v) / placeholder / max   — 글 입력칸(치는 대로 저장)
//   picker :                                      — 직원 체크 목록(직접 고르기)
//   hours  : getStart/setStart/getEnd/setEnd      — 시작~끝 시각(방해 금지 시간대)
//   cloudqr:                                      — 휴대폰 연결 QR(주소 복사·키 새로 만들기)
//   공통   : name, desc(글 또는 함수), shown()이 false 면 숨김, enabled()가 false 면 잠금(toggle·action)
//            pcOnly: true — PC 앱에서만 바꿀 수 있는 것(휴대폰 보기에서는 빼고 그린다)
// 화면에서만 쓰는 값은 store(localStorage)에 두고, 앱(C#)이 알아야 하는 값은 host.postMessage 로 넘긴다.

const SETTINGS = [
  {
    tab: 'office',
    items: [
      { type: 'text', name: T('사무실 이름', 'Office name'), desc: T('벽 간판과 창 제목(작업표시줄)에 씁니다. 비우면 기본 이름입니다.', 'Shown on the wall sign and the window title (taskbar). Leave empty for the default.'), pcOnly: true,
        placeholder: DEFAULT_OFFICE, max: 30, get: () => officeName, set: setOfficeName },
      // 언어 이름은 두 언어로 — 읽지 못하는 언어로 켜져 있어도 찾을 수 있게
      { type: 'choice', name: '화면 언어 / Language', pcOnly: true,
        desc: T('화면·말풍선·알림을 이 언어로 보여 줍니다. 휴대폰 화면도 PC를 따라갑니다.', 'Screen, speech bubbles and notifications use this language. The phone follows the PC.'),
        options: [['ko', '한국어'], ['en', 'English']], get: () => LANG, set: setLanguage },
    ],
  },
  {
    tab: 'staff',
    // 위에서 아래로 거르는 순서: 표시 기간 → 퇴근 숨김 → 고르는 방식 → 보일 인원
    items: [
      { type: 'choice', name: T('표시 기간', 'Display period'), pcOnly: true,
        desc: () => T(`마지막 대화가 ${windowLabel()} 안에 있는 세션만 사무실에 들입니다(파일 수정 시각이 아니라 실제 대화 시각 기준). 3시간 넘게 조용하면 퇴근(🚪)으로 보입니다.`,
          `Only sessions whose last message is within ${windowLabel()} come to the office (by actual message time, not file edit time). After 3 quiet hours they go home (🚪).`),
        options: WINDOW_CHOICES, get: () => windowHours, set: setWindowHours },
      { type: 'toggle', name: T('퇴근한 직원 보이기', 'Show employees who went home'), desc: T('퇴근(🚪)한 직원의 빈 책상도 사무실에 남겨 둡니다. 상단 🚪 버튼과 같습니다.', 'Keeps the empty desks of employees who went home (🚪). Same as the 🚪 button at the top.'),
        get: () => showAway, set: setShowAway },
      { type: 'toggle', name: T('✋ 기다리는 직원만 보기', "✋ Only show who's waiting"),
        desc: T('승인·질문을 기다리는 직원만 책상에 앉힙니다. 직원이 많을 때 휴대폰에서 확인하기 좋습니다.', 'Seats only employees waiting for an approval or an answer. Handy on a phone when there are many.'),
        get: () => waitOnly, set: (v) => { waitOnly = v; store.set('waitOnly', v); layoutKey = ''; refreshHud(); refreshSettings(); } },
      { type: 'number', name: T('보일 인원', 'Max employees'), desc: T('책상에 앉힐 최대 인원입니다. 넘쳐서 숨겨진 직원은 상단 요약에 🙈로 개수가 표시됩니다.', 'The most employees seated at desks. Any hidden beyond this are counted as 🙈 in the top bar.'),
        min: SHOWN_MIN, max: SHOWN_MAX, unit: T('명', ''), get: () => maxShown, set: setMaxShown },
      { type: 'choice', name: T('고르는 방식', 'How to pick'),
        desc: () => (pickMode === 'manual'
          ? T('체크한 직원만 앉힙니다. 새로 출근한 직원은 아래 목록에서 체크해야 보입니다.', 'Seats only checked employees. New arrivals show up once you check them below.')
          : T(`최근에 일한 순서로 최대 ${maxShown}명을 앉힙니다. 책상 자리는 출근 순서대로 유지됩니다.`, `Seats up to ${maxShown} most recently active. Desk seats stay in arrival order.`)),
        options: [['recent', T('최근 사용순', 'Most recent')], ['manual', T('직접 고르기', 'Choose manually')]],
        get: () => pickMode, set: setPickMode },
      { type: 'picker', shown: () => pickMode === 'manual' },
    ],
  },
  {
    tab: 'office', title: T('보기', 'View'),
    items: [
      { type: 'choice', name: T('사무실 크기', 'Office size'),
        desc: T('화면에 맞춘 크기에서 작게·크게 바꿉니다. 크게 하면 넘치는 만큼 아래로 스크롤합니다.', 'Smaller or larger than fit-to-window. When larger, scroll down for the rest.'),
        options: [[-2, T('더 작게', 'Smallest')], [-1, T('작게', 'Smaller')], [0, T('맞춤', 'Fit')], [1, T('크게', 'Larger')], [2, T('더 크게', 'Largest')]],
        get: () => zoom, set: (v) => { zoom = v; store.set('zoom', v); layoutKey = ''; refreshSettings(); } },
      { type: 'choice', name: T('사무실 조명 (낮/밤)', 'Office Lighting'),
        desc: T('실제 시간 연동 또는 원하는 감성의 오피스 조명 분위기를 선택합니다.', 'Choose ambient lighting mode (Auto synced with PC clock, day, dusk, night shift).'),
        options: [
          ['auto', T('자동 (현재 시각 연동)', 'Auto (Sync clock)')],
          ['day', T('낮 (상쾌한 낮)', 'Day')],
          ['dusk', T('노을 (따스한 저녁)', 'Dusk')],
          ['night', T('야근 (차분한 야경 조명)', 'Night Shift')],
        ],
        get: () => store.get('light_mode', 'auto'),
        set: (v) => { store.set('light_mode', v); refreshSettings(); } },
      { type: 'action', name: T('🛋️ 가구 & 소품 상점', '🛋️ Furniture Shop'),
        desc: T('작업 완료와 도전 과제로 획득한 코인으로 사무실 인테리어 소품을 구매하고 배치합니다.', 'Buy and decorate the office with coins earned from tasks and quests.'),
        label: () => T('상점 열기', 'Open Shop'), run: () => openShopModal() },
      { type: 'toggle', name: T('작게 보기(목록)', 'Compact view (list)'),
        desc: () => (REMOTE ? T('사무실 그림 대신 직원·상태를 한 줄씩 보여 줍니다. 상단 📋 버튼과 같습니다.', 'Shows one line per employee instead of the office picture. Same as the 📋 button.')
          : T('창을 작게 줄이고 직원·상태만 한 줄씩 보여 줍니다(맨 위에 떠 있음). 상단 📋 버튼과 같고, 크기·위치는 보통 창과 따로 기억합니다.',
            'Shrinks the window to one line per employee (stays on top). Same as the 📋 button, and its size and position are remembered separately.')),
        get: widgetOn, set: setWidget, enabled: () => LOCAL_WIDGET || appReady() },
    ],
  },
  {
    tab: 'office', title: T('바탕화면 미니위젯 & 아이스브레이킹', 'Desktop MiniWidget & Icebreaker'),
    items: [
      { type: 'petcfg', pcOnly: true, enabled: () => LOCAL_PET || appReady() },
      {
        type: 'choice',
        name: T('💡 아이스브레이킹 개발자 밈 주기', '💡 Icebreaker Dev Memes Interval'),
        pcOnly: true,
        desc: T('코딩 중 가벼운 활력을 위해 1~3시간 단위로 개발자 유머와 명언을 말풍선으로 띄웁니다 (미니위젯 상단 💡 버튼으로 즉시 보기 가능).',
          'Pops up developer humor and quotes every few hours to break the ice (also available instantly via 💡 in MiniPet).'),
        options: [
          [0, T('끔 (직접 💡 클릭할 때만)', 'Off (manual click only)')],
          [30, T('30분마다', 'Every 30 minutes')],
          [60, T('1시간마다 (권장)', 'Every 1 hour (Recommended)')],
          [120, T('2시간마다', 'Every 2 hours')],
          [180, T('3시간마다', 'Every 3 hours')],
          [240, T('4시간마다', 'Every 4 hours')],
        ],
        get: () => Number(store.get('memeInterval', 60)),
        set: (v) => {
          store.set('memeInterval', Number(v));
          store.set('lastMemeAt', Date.now());
          refreshSettings();
        }
      }
    ],
  },
  {
    tab: 'office', title: T('팀', 'Teams'),
    items: [
      { type: 'teams', pcOnly: true },
    ],
  },
  {
    tab: 'general', title: T('창', 'Window'),
    items: [
      { type: 'toggle', name: T('항상 위에 표시', 'Always on top'), desc: T('다른 창에 가려지지 않게 늘 맨 위에 띄웁니다. 상단 📌 버튼과 같습니다.', 'Keeps the window above other windows. Same as the 📌 button at the top.'), pcOnly: true,
        get: () => pinned, set: setPinned },
      { type: 'toggle', name: T('시작 시 알림 영역에 숨기기', 'Start hidden in the tray'), pcOnly: true,
        desc: T('다음 실행부터 창을 바로 띄우지 않고 작업표시줄 오른쪽 알림 영역(🧑‍💻)에 숨긴 채 시작합니다. 아이콘을 누르면 창이 열리고, 숨어 있는 동안에도 감시와 휴대폰 연결은 계속 돌아갑니다.',
          'From the next launch, starts hidden in the notification area (🧑‍💻) without opening the window. Click the icon to open it. Monitoring and the phone connection keep running.'),
        get: () => !!appSettings?.startMinimized,
        set: (v) => setAppSetting('startMinimized', v),
        enabled: () => !DEMO && !!appSettings },
      { type: 'toggle', name: T('닫기(✕)를 눌러도 숨기기', 'Close (✕) hides instead of quitting'), pcOnly: true,
        desc: T('창 오른쪽 위 ✕를 눌러도 끄지 않고 알림 영역(🧑‍💻)으로 숨깁니다(휴대폰 연결도 유지). 완전히 끝내려면 🧑‍💻 아이콘을 오른쪽 클릭해 [끝내기]를 누르세요.',
          '✕ only hides the window and the app keeps running in the notification area (🧑‍💻), phone connection too. To quit, right-click the 🧑‍💻 icon → Exit.'),
        get: () => !!appSettings?.closeToTray,
        set: (v) => setAppSetting('closeToTray', v),
        enabled: () => !DEMO && !!appSettings },
      { type: 'toggle', name: T('Windows 시작할 때 자동 실행', 'Run at Windows startup'), pcOnly: true,
        desc: () => T('Windows에 로그인할 때 알림 영역(🧑‍💻)에 숨긴 채로 저절로 켜집니다(이 계정의 시작 프로그램에 등록).',
          "Starts hidden in the notification area (🧑‍💻) when you sign in to Windows (registered in this user's startup apps).")
          + (appSettings?.autoStartElsewhere ? T(' ⚠️ 지금은 다른 폴더의 Claude Office가 등록돼 있어요 — 껐다 켜면 이 위치로 바뀝니다.',
            ' ⚠️ A Claude Office in another folder is registered now — turn this off and on to switch to this one.') : ''),
        get: () => !!appSettings?.autoStart,
        set: (v) => setAppSetting('autoStart', v),
        enabled: () => !DEMO && !!appSettings },
    ],
  },
  {
    tab: 'notify',
    // 창을 숨겨 둬도 알아야 하므로 앱(C#)이 판단해 Windows 알림으로 띄운다. 창을 보고 있을 때는 띄우지 않는다
    items: [
      { type: 'toggle', name: T('✋ 기다릴 때 알림', '✋ Notify when waiting'), pcOnly: true,
        desc: T('직원이 승인이나 질문을 기다리기 시작하면 Windows 알림을 띄웁니다. 이 창을 보고 있을 때는 띄우지 않고, 알림을 누르면 해당 직원이 바로 열립니다.',
          "Shows a Windows notification when an employee starts waiting for an approval or an answer. Not while you're looking at the window; clicking it opens that employee."),
        get: () => !!appSettings?.notifyWaiting, set: (v) => setAppSetting('notifyWaiting', v), enabled: appReady },
      { type: 'toggle', name: T('오래 걸린 일이 끝나면 알림', 'Notify when a long task ends'), pcOnly: true,
        desc: () => T(`${doneLabel()} 넘게 일하던 직원이 응답을 마치고 쉬기 시작하면 알립니다.`, `Notifies when an employee who worked over ${doneLabel()} finishes and starts resting.`),
        get: () => !!appSettings?.notifyDone, set: (v) => setAppSetting('notifyDone', v), enabled: appReady },
      { type: 'choice', name: T('오래 걸린 일 기준', 'What counts as long'), pcOnly: true, shown: () => !!appSettings?.notifyDone,
        desc: T('지시를 받은 뒤 응답을 마칠 때까지 이 시간 이상 걸렸을 때만 완료 알림을 띄웁니다.', 'Only notifies when a task took at least this long from prompt to finish.'),
        options: DONE_CHOICES, get: () => appSettings?.notifyDoneMinutes, set: (v) => setAppSetting('notifyDoneMinutes', v) },
      { type: 'toggle', name: T('레트로 알림음', 'Retro sound'), pcOnly: true, desc: T('승인 요청이나 작업 완료 시 귀여운 8비트 차임벨을 재생합니다.', 'Plays a cute 8-bit retro chime on approvals and completions.'),
        get: () => !!appSettings?.notifySound, set: (v) => setAppSetting('notifySound', v), enabled: appReady },
      { type: 'action', name: T('효과음 미리듣기', 'Preview sound'), pcOnly: true, shown: () => !!appSettings?.notifySound,
        desc: T('승인 요청·작업 완료 시 재생되는 8비트 효과음을 들어봅니다.', 'Listen to the 8-bit sound effects for approvals and finished work.'),
        label: () => T('▶ 들어보기', '▶ Play'), run: () => playRetroSound('test', true), enabled: appReady },
      { type: 'toggle', name: T('방해 금지 시간대', 'Quiet hours'), pcOnly: true,
        desc: () => (appSettings?.quietEnabled
          ? T(`${appSettings.quietStart}시부터 ${appSettings.quietEnd}시까지는 PC 알림을 띄우지 않습니다.`, `No notifications from ${appSettings.quietStart}:00 to ${appSettings.quietEnd}:00.`)
          : T('정한 시간대(예: 밤~아침)에는 PC 알림을 띄우지 않습니다.', 'No notifications during the hours you set (e.g. at night).')),
        get: () => !!appSettings?.quietEnabled, set: (v) => setAppSetting('quietEnabled', v), enabled: appReady },
      { type: 'hours', pcOnly: true, shown: () => !!appSettings?.quietEnabled,
        getStart: () => appSettings?.quietStart ?? 22, setStart: (v) => setAppSetting('quietStart', v),
        getEnd: () => appSettings?.quietEnd ?? 8, setEnd: (v) => setAppSetting('quietEnd', v), enabled: appReady },
      { type: 'action', name: T('알림 시험', 'Test notification'), pcOnly: true, desc: T('지금 샘플 알림을 하나 띄워 봅니다(창을 보고 있어도 표시됨).', 'Shows a sample notification now (even while the window is open).'),
        label: () => T('띄워 보기', 'Try it'), run: () => send({ cmd: 'notify-test' }), enabled: () => appReady() && !!appSettings.tray },
    ],
  },
  {
    tab: 'general', title: T('Claude Code 연결', 'Claude Code connection'),
    items: [
      { type: 'action', name: T('Claude Code 연결', 'Connect to Claude Code'), pcOnly: true,
        desc: T('Claude가 승인을 기다리거나 질문하면 직원이 즉시 손을 들고, 세션을 닫으면 바로 퇴근합니다. 휴대폰 지시·승인도 이 연결을 통해 동작합니다. Claude Code 설정(~/.claude/settings.json)에 연결 줄을 추가하므로 확인 창이 먼저 뜹니다(끊으면 추가한 줄만 제거).',
          'Employees raise a hand the moment Claude waits for an approval or asks a question, and go home as soon as a session closes. Phone instructions and approvals come through this too. It adds a few lines to the Claude Code settings file, so you confirm first (disconnecting removes only those lines).'),
        label: () => (claudeConnected() ? T('연결 끊기', 'Disconnect') : data.hooks && data.hooks.state !== 'off' ? T('다시 연결', 'Reconnect') : T('연결하기', 'Connect')),
        enabled: () => !DEMO && !!data.hooks,   // 견본 화면에서는 바꿀 수 없다(앱이 --demo 면 알림을 받지도 않는다)
        run: toggleHooks, status: hookStatus },
    ],
  },
  {
    tab: 'phone',
    items: [
      { type: 'action', name: T('휴대폰 연결', 'Phone connection'), pcOnly: true,
        desc: T('밖에서도 휴대폰으로 사무실을 보고, 자리를 비웠을 때 승인하거나 다음 지시를 보냅니다. [켜기] 후 QR만 한 번 찍으면 되며 로그인·계정이 필요 없습니다. 🔒 무료 서버(화면: GitHub · 전달: Google Firebase)를 거치지만 내 PC와 내 휴대폰만 아는 열쇠로 암호화해 서버도 다른 사람도 볼 수 없고, 대화 원본은 전송하지 않습니다.',
          "See the office on your phone, and instruct and approve while you're away. Turn it on and scan the QR — no sign-in, no setup. 🔒 It goes through a free server (page: GitHub · delivery: Google Firebase), but it's locked with a key only your PC and your phone have, so neither the server nor anyone else can read it. Conversations are never sent."),
        label: () => (cloud?.enabled ? T('끄기', 'Turn off') : T('켜기', 'Turn on')),
        enabled: () => !DEMO && !!cloud?.configured,   // 견본 화면에서는 쓸 수 없다
        run: () => send({ cmd: 'cloud-enable', value: !cloud.enabled }),
        status: cloudStatus },
      { type: 'cloudqr', pcOnly: true, shown: () => !DEMO && !!cloud?.enabled },
    ],
  },
  {
    // 자리 비움(휴대폰의 지시·승인을 받는 상태)은 손으로 켜고 끄지 않는다 — 입력 없음·화면 잠금·휴대폰에서 켜지고, 돌아와 입력하면 꺼진다
    tab: 'phone', title: T('자리를 비웠을 때', "While you're away"),
    items: [
      { type: 'action', name: T('Claude Code 연결이 필요해요', 'Connect to Claude Code first'), pcOnly: true, shown: () => !DEMO && !!remoteState && !remoteState.installed,
        desc: T('자리를 비운 동안 휴대폰에서 지시·승인을 하려면 먼저 Claude Code와 연결해야 합니다(⚙️ → 🖥️ 일반의 연결과 같음).', 'To instruct and approve from the phone, connect to Claude Code (same button as ⚙️ → 🖥️ General).'),
        label: () => T('연결하기', 'Connect'), enabled: () => !DEMO && !!data.hooks, run: toggleHooks },
      { type: 'choice', name: T('입력이 없으면 자리 비움', 'Away when idle'), pcOnly: true, shown: () => !!remoteState?.installed,
        desc: T('자리 비움(🏃) 중일 때 승인 요청과 지시를 휴대폰으로 주고받습니다. 마우스·키보드를 이 시간 동안 안 쓰면 저절로 켜지고, 돌아와 움직이면 꺼집니다. 이 시간이 되기 전이라도 PC를 30초 넘게 안 쓰는 동안 휴대폰 화면을 열거나 지시를 보내면 바로 켜집니다.',
          "While Away, the phone's instructions and approvals are taken. It turns on after this long without mouse or keyboard input and off when you're back (even while hidden in the tray). Even sooner, once the PC has been idle for 30 s, opening the phone page or sending from the phone turns it on."),
        options: [[0, T('끔', 'Off')], [1, T('1분', '1 min')], [3, T('3분', '3 min')], [5, T('5분', '5 min')], [10, T('10분', '10 min')]],
        get: () => appSettings?.remoteAwayIdleMinutes ?? 3, set: (v) => setAppSetting('remoteAwayIdleMinutes', v), status: remoteStatus },
      { type: 'toggle', name: T('화면을 잠그면 자리 비움', 'Away while the screen is locked'), pcOnly: true, shown: () => !!remoteState?.installed,
        desc: T('Windows 화면을 잠그면(⊞+L) 대기 시간 없이 즉시 자리 비움을 켜고, 잠금을 풀면 끕니다. 자리를 뜨자마자 오는 승인 요청도 놓치지 않고 휴대폰으로 받고 싶을 때 유용합니다.',
          'Locking Windows (⊞+L) turns Away on right away, and unlocking turns it off — for when even approval requests right after you leave should go to the phone.'),
        get: () => !!appSettings?.remoteAwayOnLock, set: (v) => setAppSetting('remoteAwayOnLock', v), enabled: appReady },
    ],
  },
  {
    tab: 'phone', title: T('고급', 'Advanced'),
    items: [
      { type: 'toggle', name: T('휴대폰에서 별명·메모 고치기', 'Edit nicknames & memos on the phone'), pcOnly: true, shown: () => !DEMO && !!cloud?.configured,
        desc: T('휴대폰의 직원 상세 화면에서 별명이나 메모를 고치면 몇 초 안에 PC에도 그대로 반영됩니다(같은 열쇠로 암호화).', "Nicknames and memos edited in the phone's employee details reach the PC within seconds (locked with the same key)."),
        get: () => !!cloud?.phoneEdit, set: (v) => send({ cmd: 'cloud-phone-edit', value: v }), enabled: () => !DEMO && !!cloud },
    ],
  },
  {
    tab: 'about',
    items: [
      { type: 'action', name: T('버전', 'Version'), pcOnly: true,
        desc: () => T(`현재 버전 v${appSettings?.version || '?'} — 새 버전이 나오면 [업데이트] 버튼으로 앱 안에서 바로 받아 설치합니다(GitHub 릴리스, SHA-256 무결성 확인).`,
          `Current version v${appSettings?.version || '?'} — when a new version is out, [Update] downloads and installs it (GitHub Releases, SHA-256 checked).`),
        label: () => (appSettings?.update?.newer ? (appSettings.update.canInstall ? T('업데이트', 'Update') : T('받으러 가기', 'Download')) : T('지금 확인', 'Check now')),
        run: () => (!appSettings.update?.newer ? send({ cmd: 'update-check' })
          : appSettings.update.canInstall ? send({ cmd: 'update-install' })   // 앱이 "업데이트할까요?" 확인 창을 띄운다
          : openUrl(appSettings.update.url)),
        enabled: () => appReady() && !['downloading', 'installing'].includes(appSettings.update?.install?.state), status: updateStatus },
      { type: 'toggle', name: T('새 버전 알림', 'New version alerts'), pcOnly: true,
        desc: T('하루 두 번(그리고 설정을 열 때) GitHub에 새 릴리스가 있는지 확인하고, 새 버전당 한 번 알려 드립니다(전송 정보: 앱 버전뿐).',
          'Asks GitHub twice a day (and when you open Settings) whether a new version is out and tells you once (only the app version is sent).'),
        get: () => !!appSettings?.updateCheck, set: (v) => setAppSetting('updateCheck', v), enabled: appReady },
    ],
  },
  {
    tab: 'about', title: T('백업·초기화', 'Backup & reset'),
    items: [
      { type: 'action', name: T('별명·메모 내보내기', 'Export nicknames & memos'), pcOnly: true,
        desc: T('직원에 붙여 둔 별명·메모와 사무실 이름을 JSON 파일 하나로 백업합니다(다른 PC로 옮길 때도 사용).', 'Saves nicknames, memos and the office name to one file (to move to another PC or back up).'),
        label: () => T('내보내기', 'Export'), run: () => send({ cmd: 'share-export' }), enabled: appReady },
      { type: 'action', name: T('별명·메모 가져오기', 'Import nicknames & memos'), pcOnly: true,
        desc: T('백업해 둔 JSON 파일을 읽어 지금 목록과 합칩니다. 이 PC에 없거나 파일 쪽이 더 최근에 수정된 항목만 반영됩니다.', 'Reads an exported file and merges it. Only entries missing here or edited later in the file are applied.'),
        label: () => T('가져오기', 'Import'), run: () => send({ cmd: 'share-import' }), enabled: appReady },
      { type: 'action', name: T('설정 초기화', 'Reset settings'), pcOnly: true,
        desc: T('직원 표시·창·알림 설정을 기본값으로 되돌립니다. 직접 적은 별명·메모·사무실 이름·팀 설정과 화면 언어, 휴대폰·Claude Code 연결, 자동 실행은 유지됩니다.',
          'Restores employee display, window and notification settings to defaults. Nicknames, memos, office and team names, language, phone connection, Claude Code connection and startup stay.'),
        label: () => T('초기화', 'Reset'), run: () => send({ cmd: 'reset-settings' }), enabled: appReady },
    ],
  },
  {
    tab: 'about', title: T('문제 해결', 'Troubleshooting'),
    items: [
      { type: 'action', name: T('문제 보고', 'Report a problem'), pcOnly: true,
        desc: T('GitHub 이슈 작성 페이지를 엽니다. 앱 버전·Windows·WebView2 버전만 양식에 채워지며 대화 기록은 일절 포함되지 않습니다.',
          'Opens a new GitHub issue with only version, Windows and WebView2 info filled in (no conversation content).'),
        label: () => T('GitHub에 쓰기', 'Write on GitHub'), run: () => send({ cmd: 'report-issue' }), enabled: () => !DEMO },
      { type: 'action', name: T('데이터 폴더', 'Data folder'), pcOnly: true,
        desc: T('설정(app.json), 별명·메모 사본(share.json), 휴대폰 연결 열쇠(cloud.json), 동작 기록(*.log)이 저장된 폴더를 탐색기로 엽니다. ⚠️ cloud.json 에는 암호 키가 들어 있으니 외부에 공유하지 마세요.',
          'Opens the folder with settings (app.json), nicknames & memos (share.json), phone key (cloud.json) and logs (*.log). ⚠️ Never share cloud.json with anyone.'),
        label: () => T('열기', 'Open'), run: () => send({ cmd: 'open-data-dir' }), enabled: appReady },
    ],
  },
];

/** 화면 언어 바꾸기: 앱이 저장하고 새 언어로 화면을 다시 연다. 견본은 앱이 저장하지 않으므로 주소(?lang=)만 바꿔 다시 연다. */
function setLanguage(v) {
  if (v === LANG) return;
  if (DEMO) {
    const p = new URLSearchParams(location.search);
    p.set('lang', v);
    location.search = p.toString();
  } else setAppSetting('language', v);
}

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
    minus.setAttribute('aria-label', T(`${it.name} 줄이기`, `Decrease ${it.name}`));
    plus.setAttribute('aria-label', T(`${it.name} 늘리기`, `Increase ${it.name}`));
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

  // 팀(작업 폴더)마다 표시 이름·카펫 색·순서. 팀 목록·색·순서가 바뀔 때만 다시 만든다(이름 칸에 쓰는 도중 사라지지 않게)
  teams(it, row) {
    row.classList.add('stack');
    const box = elem('div', 'teamlist');
    row.append(elem('div', 'desc', T('팀은 작업 폴더의 마지막 두 단계입니다. 이름·카펫 색·순서를 정하면 휴대폰에도 같게 보입니다. 직원 자리는 직원 상세의 ◀ ▶ 로 옮깁니다.',
      "A team is the last two levels of the working folder. Names, rug colors and order look the same on your phone. Move an employee's seat with ◀ ▶ in their details.")), box);
    let builtKey = null;
    const inputs = new Map();
    return () => {
      const teams = currentTeams();
      const key = teams.join('|') + JSON.stringify(teams.map((t) => teamPrefs[t]?.color ?? -1));
      if (key !== builtKey) {
        builtKey = key;
        inputs.clear();
        box.replaceChildren(...(teams.length ? teams.map((team, i) => {
          const r = elem('div', 'team-row'), top = elem('div', 'tr-top');
          const sw = elem('span', 'sw');
          sw.style.background = teamColor(team);
          const name = elem('input', 'field');
          name.type = 'text';
          name.maxLength = 30;
          name.placeholder = team;
          name.spellcheck = false;
          name.addEventListener('input', () => setTeamPref(team, { name: name.value.trim() }));
          inputs.set(team, name);
          const up = elem('button', 'mini', '▲'), down = elem('button', 'mini', '▼');
          up.title = T('앞으로', 'Move up');
          down.title = T('뒤로', 'Move down');
          up.disabled = i === 0;
          down.disabled = i === teams.length - 1;
          up.addEventListener('click', () => moveTeam(team, -1));
          down.addEventListener('click', () => moveTeam(team, 1));
          top.append(sw, name, up, down);
          const colors = elem('div', 'tr-colors');
          const cur = teamPrefs[team]?.color;
          const auto = elem('button', 'c auto' + (Number.isInteger(cur) ? '' : ' on'), T('자동', 'Auto'));
          auto.addEventListener('click', () => setTeamPref(team, { color: undefined }));
          colors.append(auto, ...RUGS.map((c, ci) => {
            const b = elem('button', 'c' + (cur === ci ? ' on' : ''));
            b.style.background = c;
            b.title = T(`색 ${ci + 1}`, `Color ${ci + 1}`);
            b.addEventListener('click', () => setTeamPref(team, { color: ci }));
            return b;
          }));
          r.append(top, elem('div', 'tr-key mono', team), colors);
          return r;
        }) : [elem('div', 'desc', T('아직 사무실에 팀이 없어요.', 'No teams in the office yet.'))]));
      }
      for (const [team, input] of inputs) if (document.activeElement !== input) input.value = teamPrefs[team]?.name || '';
    };
  },

  hours(it, row) {
    const box = elem('div', 'hours');
    const pick = (set) => {
      const sel = elem('select', 'field');
      for (let h = 0; h < 24; h++) sel.append(new Option(T(`${h}시`, `${h}:00`), String(h)));
      sel.addEventListener('change', () => set(Number(sel.value)));
      return sel;
    };
    const from = pick(it.setStart), to = pick(it.setEnd);
    box.append(from, elem('span', '', T('부터', 'to')), to, elem('span', '', T('까지', '')));
    row.append(box);
    return (locked) => {
      from.disabled = to.disabled = locked;
      if (document.activeElement !== from) from.value = String(it.getStart());
      if (document.activeElement !== to) to.value = String(it.getEnd());
    };
  },

  petcfg(it, row) {
    row.classList.add('stack');
    const top = elem('div', 'petcfg-top');
    const txt = elem('div', 'txt');
    txt.append(
      elem('div', 'name', T('바탕화면 미니위젯', 'Desktop MiniWidget')),
      elem('div', 'desc', T('대표 직원 한 명을 바탕화면 구석(기본: 오른쪽 아래, 드래그로 이동 가능)에 꺼내 둡니다. 메인 창을 닫아 알림 영역에 숨겨 두어도 바탕화면에 남아 지시·승인·거절·직원 전환을 바로 할 수 있습니다(상단 🪟 버튼과 같음).',
        'Places one representative employee in the corner of your desktop (bottom-right by default, draggable). Stays on your desktop even when the main window is hidden to the tray (same as the 🪟 button).'))
    );
    const swOn = elem('button', 'switch');
    swOn.setAttribute('role', 'switch');
    swOn.setAttribute('aria-label', T('바탕화면 미니위젯 켜기', 'Enable Desktop MiniWidget'));
    swOn.addEventListener('click', () => setPet(!petOn()));
    top.append(txt, swOn);

    const sub = elem('div', 'petcfg-sub');

    const linePick = elem('div', 'petcfg-line');
    const lblPick = elem('span', 'lbl', T('대표 직원', 'Representative'));
    const sel = elem('select', 'field');
    sel.addEventListener('change', () => setPetSession(sel.value));
    linePick.append(lblPick, sel);

    const lineBg = elem('div', 'petcfg-line');
    const bgLeft = elem('div', '');
    bgLeft.append(
      elem('div', 'lbl', T('배경 숨기기', 'Hide background')),
      elem('div', 'subdesc', T('구석 방 배경(벽·창문·화분·카펫)을 빼고 직원·의자·책상·컴퓨터만 보여 줍니다 (미니위젯 상단 🖼️ 버튼과 같음).',
        'Hides the room background (wall, window, plant, rug) and shows only the employee, chair, desk, and computer (same as the 🖼️ button).'))
    );
    const swBg = elem('button', 'switch');
    swBg.setAttribute('role', 'switch');
    swBg.setAttribute('aria-label', T('배경 숨기기', 'Hide background'));
    swBg.addEventListener('click', () => setPetNoBg(!petNoBgOn()));
    lineBg.append(bgLeft, swBg);

    sub.append(linePick, lineBg);
    row.append(top, sub);

    return (locked) => {
      swOn.disabled = swBg.disabled = sel.disabled = locked;
      swOn.setAttribute('aria-checked', String(!!petOn()));
      swBg.setAttribute('aria-checked', String(!!petNoBgOn()));
      if (document.activeElement !== sel) {
        const pref = petSessionPref();
        const cur = resolvePetSession();
        const autoLabel = cur && !pref
          ? T(`🌟 자동 (확인 필요·최근 순 — 지금: ${displayName(cur)})`, `🌟 Auto (waiting / recent — now: ${displayName(cur)})`)
          : T('🌟 자동 (확인 필요·최근 순)', '🌟 Auto (waiting / recent)');
        const opts = [new Option(autoLabel, '')];
        for (const s of data.sessions) {
          const a = actOf(s);
          opts.push(new Option(`${s.icon} ${displayName(s)} (${teamLabel(s.team)} · ${a.label})`, s.id));
        }
        sel.replaceChildren(...opts);
        sel.value = pref && data.sessions.some((s) => s.id === pref) ? pref : '';
      }
    };
  },

  // 휴대폰 연결: QR(해독 키가 들어 있어 누를 때만 보인다)과 주소 복사·키 새로 만들기
  cloudqr(it, row) {
    row.classList.add('stack');
    const bar = elem('div', 'btnbar');
    const showBtn = elem('button', 'act'), copyBtn = elem('button', 'act ghost', T('주소 복사', 'Copy link'));
    const rekeyBtn = elem('button', 'act ghost', T('키 새로 만들기', 'New key'));
    bar.append(showBtn, copyBtn, rekeyBtn);
    const box = elem('div', 'qrbox'), qrEl = elem('div', 'qr'), urlEl = elem('div', 'qrurl mono');
    box.append(qrEl, urlEl, elem('div', 'desc',
      T('휴대폰 카메라로 찍어 여세요(홈 화면에 추가하면 앱처럼 쓸 수 있어요). 주소에 해독 키가 들어 있으니 남에게 보이지 않게 주의하고, 노출됐다면 [키 새로 만들기]를 누르세요.',
        'Scan it with your phone camera (add it to the home screen to use it like an app). The link contains the decryption key, so keep it private; if someone saw it, click [New key].')));
    row.append(bar, box);

    showBtn.addEventListener('click', () => { cloudQrShown = !cloudQrShown; refreshSettings(); });
    copyBtn.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(cloud.url);
        copyBtn.textContent = T('복사됨', 'Copied');
        setTimeout(() => { copyBtn.textContent = T('주소 복사', 'Copy link'); }, 1500);
      } catch {
        cloudQrShown = true;   // 클립보드를 못 쓰면 주소를 보여 줘서 직접 복사하게
        refreshSettings();
      }
    });
    rekeyBtn.addEventListener('click', () => send({ cmd: 'cloud-rekey' }));   // 앱이 확인 창을 띄운다

    let drawnUrl = null;
    return () => {
      const url = cloud?.url;   // 첫 로그인(익명)이 끝나야 생긴다
      showBtn.textContent = cloudQrShown ? T('QR 숨기기', 'Hide QR') : T('QR 보기', 'Show QR');
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
        ln.meta.textContent = `${orig}${s.team} · ${s.act === 'away' ? T('퇴근', 'Gone home') : fmtAgo(now() - s.last)}`;
        ln.label.classList.toggle('away', s.act === 'away');
      });

      const count = pickedCount();
      head.textContent = data.sessions.length ? T(`선택 ${count} / 최대 ${maxShown}명`, `Picked ${count} / max ${maxShown}`) : T('아직 고를 직원이 없어요', 'No employees to pick yet');
      head.classList.toggle('full', count >= maxShown);

      // 방금 넘치게 고르려 했으면 그 안내를, 아니면 보일 인원보다 많이 체크돼 있을 때(인원을 줄인 경우) 그 안내를
      const blocked = Date.now() < pickNoteUntil;
      note.textContent = blocked ? T(`최대 ${maxShown}명만 선택할 수 있습니다.`, `You can pick up to ${maxShown}.`)
        : count > maxShown ? T(`${count}명이 체크돼 있어 최근에 일한 ${maxShown}명만 보입니다. 보일 인원을 늘리거나 체크를 줄여 주세요.`,
          `${count} are checked, so only the ${maxShown} most recently active show. Raise Max employees or uncheck some.`)
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

// 설정 팝업의 탭. dot(): 이 탭에 볼 것이 있으면 점을 찍는다
const SETTINGS_TABS = [
  { id: 'office', icon: '🏢', name: T('사무실', 'Office') },
  { id: 'staff', icon: '👥', name: T('직원', 'Employees') },
  { id: 'notify', icon: '🔔', name: T('알림', 'Notifications') },
  { id: 'phone', icon: '📱', name: T('휴대폰', 'Phone'), dot: () => !!cloud?.enabled && cloud.state === 'error' },
  { id: 'general', icon: '🖥️', name: T('일반', 'General') },
  { id: 'about', icon: 'ℹ️', name: T('정보', 'About'), dot: () => !!appSettings?.update?.newer },
];

const settingRows = [];
const settingTabs = new Map();   // 탭 id → { tab, btn, pane }
let settingsTab = store.get('settingsTab', 'office');   // 마지막에 본 탭

function buildSettings() {
  for (const tab of SETTINGS_TABS) {
    const secs = SETTINGS.filter((sec) => sec.tab === tab.id)
      .map((sec) => ({ ...sec, items: sec.items.filter((it) => !(REMOTE && it.pcOnly)) }))   // 휴대폰 보기에서는 PC 전용 항목을 뺀다
      .filter((sec) => sec.items.length);
    if (!secs.length) continue;   // 휴대폰 보기에서 PC 전용만 있던 탭은 탭째 뺀다
    const pane = elem('div', 'spane');
    pane.setAttribute('role', 'tabpanel');
    for (const sec of secs) {
      if (sec.title) pane.append(elem('h3', '', sec.title));
      for (const it of sec.items) {
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
        pane.append(row);
        settingRows.push({ it, row, desc, status, refresh });
      }
    }
    const btn = elem('button', 'stab');
    btn.setAttribute('role', 'tab');
    btn.append(elem('span', 'ic', tab.icon), elem('span', 'nm', tab.name), elem('span', 'dot'));
    btn.addEventListener('click', () => showSettingsTab(tab.id));
    $('sTabs').append(btn);
    settingsBody.append(pane);
    settingTabs.set(tab.id, { tab, btn, pane });
  }
  if (!settingTabs.has(settingsTab)) settingsTab = settingTabs.keys().next().value;
  showSettingsTab(settingsTab, false);
}

function showSettingsTab(id, remember = true) {
  if (!settingTabs.has(id)) return;
  settingsTab = id;
  if (remember) store.set('settingsTab', id);
  for (const [key, { btn, pane }] of settingTabs) {
    btn.classList.toggle('on', key === id);
    btn.setAttribute('aria-selected', String(key === id));
    pane.hidden = key !== id;
  }
  settingsBody.scrollTop = 0;
  refreshSettings();
}

/** 버전 "1.1.2" 비교: a 가 더 새것이면 양수 */
function cmpVersion(a, b) {
  const pa = String(a).split('.').map(Number), pb = String(b).split('.').map(Number);
  for (let i = 0; i < 3; i++) if ((pa[i] || 0) !== (pb[i] || 0)) return (pa[i] || 0) - (pb[i] || 0);
  return 0;
}

// 설정 맨 위 버전 줄: PC 는 앱 버전(새 버전이 있으면 [업데이트] 버튼까지), 휴대폰은 화면·PC 앱 버전을 나란히(어느 쪽이 옛것인지).
// 버튼은 한 번만 만든다(스냅샷마다 다시 만들면 누르는 도중에 바뀌어 클릭이 사라진다)
const verText = elem('span'), verWarn = elem('div', 'vwarn'), verBtn = elem('button', 'act');
verBtn.addEventListener('click', () => {
  const u = appSettings?.update;
  if (u?.canInstall) send({ cmd: 'update-install' });   // 앱이 "업데이트할까요?" 확인 창을 띄운다
  else if (u?.url) openUrl(u.url);
});
$('sVer').append(verText, verWarn, verBtn);

function refreshVersionLine() {
  let text, warn = '', btn = '';
  if (REMOTE) {
    text = T(`휴대폰 화면 v${PAGE_VERSION} · PC 앱 `, `Phone page v${PAGE_VERSION} · PC app `)
      + (remotePcVersion ? `v${remotePcVersion}` : T('v1.1.1 이하', 'v1.1.1 or older'));
    const d = cmpVersion(PAGE_VERSION, remotePcVersion || '1.1.1');
    if (d < 0) warn = T('⚠️ 휴대폰 화면이 PC 앱보다 옛 버전이에요 — 새로고침해 보세요(앱으로 설치했다면 완전히 닫았다 열기).',
      '⚠️ The phone page is older than the PC app — reload it (if installed as an app, close it fully and reopen).');
    else if (d > 0) warn = T('⚠️ PC 앱이 옛 버전이에요 — PC에서 ⚙️ 설정을 열면 맨 위에 [업데이트]가 나와요(안 보이면 맨 아래 정보 → [지금 확인]).',
      '⚠️ The PC app is older — open ⚙️ Settings on the PC and [Update] appears at the top (if not, About at the bottom → [Check now]).');
  } else {
    text = `Claude Office v${appSettings?.version || PAGE_VERSION}`;
    const up = appSettings?.update, ins = up?.install || {};
    if (['downloading', 'installing', 'error'].includes(ins.state)) warn = updateStatus().text;
    else if (up?.newer) {
      warn = T(`🎉 새 버전 v${up.latest} 이 나왔어요`, `🎉 Version ${up.latest} is out`);
      btn = up.canInstall ? T('업데이트', 'Update') : T('받으러 가기', 'Download');
    }
  }
  verText.textContent = text;
  verWarn.textContent = warn;
  verWarn.hidden = !warn;
  verBtn.textContent = btn;
  verBtn.hidden = !btn || DEMO;
}

function refreshSettings() {
  if (settingsEl.hidden) return;
  refreshVersionLine();
  for (const { tab, btn } of settingTabs.values()) btn.querySelector('.dot').hidden = !tab.dot?.();
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

/** 설정 팝업 열기·닫기. 가운데에 뜨고 뒤(사무실·직원 상세 패널)는 그대로 둔다. tab: 그 탭으로 열기 */
function setSettingsOpen(open, tab) {
  settingsEl.hidden = !open;
  btnSettings.classList.toggle('on', open);
  if (open) {
    tooltipEl.hidden = true;
    if (tab) showSettingsTab(tab);
  }
  refreshSettings();
  // 새 버전 확인은 12시간마다라, 설정을 열 때 10분 넘게 지났으면 한 번 더 물어본다(새 버전 알림을 켜 둔 경우만)
  const u = appSettings?.update;
  if (open && !DEMO && !REMOTE && appSettings?.updateCheck && !['downloading', 'installing'].includes(u?.install?.state)
      && Date.now() - (u?.checkedAt || 0) > 10 * 60 * 1000) send({ cmd: 'update-check' });
}

// ───────────────────────────── 오른쪽 서랍(직원 상세 패널·근무 일지) ─────────────────────────────
// 한 번에 하나만 열린다(설정은 가운데 팝업이라 서랍이 아니다). 펼치면 사무실을 서랍 자리를 뺀 폭에 맞춰 다시 배치해 직원을 가리지 않고,
// 접으면 오른쪽 끝에 손잡이만 남기고 사무실이 창 전체를 쓴다. ✕ 는 완전히 닫기.

const DRAWER_W = 330;        // office.css 의 --drawer-w 와 같게
const DRAWER_MIN_VIEW = 360; // 창이 너무 좁아 이만큼도 안 남으면 비켜 주지 않고 예전처럼 서랍이 위에 덮는다
let drawerFolded = false;

const drawerOpen = () => !panelEl.hidden;

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
  const s = data.sessions.find((x) => x.id === selectedId);
  drawerTab.querySelector('.arrow').textContent = drawerFolded ? '◀' : '▶';
  drawerTab.querySelector('.lbl').textContent = drawerFolded ? (s ? displayName(s) : selectedId === REPORT_ID ? T('근무 일지', 'Work log') : '') : '';
  drawerTab.title = drawerFolded ? T('펼치기', 'Expand') : T('접기 — 사무실을 넓게 보기', 'Fold — see more of the office');
}

drawerTab.addEventListener('click', () => setDrawerFolded(!drawerFolded));

btnSettings.addEventListener('click', () => setSettingsOpen(settingsEl.hidden));
$('sClose').addEventListener('click', () => setSettingsOpen(false));
settingsEl.addEventListener('click', (e) => { if (e.target === settingsEl) setSettingsOpen(false); });   // 팝업 바깥(어두운 곳)을 누르면 닫기

// ───────────────────────────── 📱 캔버스 터치 핀치 줌 & 팬 & 더블 탭 포커스 ─────────────────────────────

let canvasZoomScale = 1.0;
let canvasPanX = 0, canvasPanY = 0;
let touchState = {
  active: false,
  mode: 'none', // 'pan' | 'pinch'
  startX: 0,
  startY: 0,
  initPanX: 0,
  initPanY: 0,
  initDist: 0,
  initScale: 1.0,
  lastTapTime: 0,
};

function applyCanvasTransform(animate = false) {
  if (animate) canvas.style.transition = 'transform 0.22s ease-out';
  else canvas.style.transition = 'none';

  canvas.style.transformOrigin = '0 0';
  canvas.style.transform = `translate3d(${canvasPanX}px, ${canvasPanY}px, 0) scale(${canvasZoomScale})`;
}

function resetCanvasZoom(animate = true) {
  canvasZoomScale = 1.0;
  canvasPanX = 0;
  canvasPanY = 0;
  applyCanvasTransform(animate);
}

function zoomToPoint(cx, cy, targetScale = 2.2) {
  canvasZoomScale = targetScale;
  canvasPanX = clamp(innerWidth / 2 - cx * targetScale, innerWidth - (cssW * targetScale), 0);
  canvasPanY = clamp(innerHeight / 2 - cy * targetScale, innerHeight - (logicalH * U * targetScale), 0);
  applyCanvasTransform(true);
}

function getCanvasPos(e) {
  if (canvasZoomScale !== 1.0) {
    const rect = canvas.getBoundingClientRect();
    if (rect.width > 0 && rect.height > 0) {
      const normX = (e.clientX - rect.left) / rect.width;
      const normY = (e.clientY - rect.top) / rect.height;
      return { x: normX * cssW, y: normY * (logicalH * U) };
    }
  }
  return { x: e.offsetX, y: e.offsetY };
}

canvas.addEventListener('touchstart', (e) => {
  if (WIDGET_WINDOW || widgetOn()) return;

  const now = Date.now();
  if (e.touches.length === 2) {
    e.preventDefault();
    touchState.mode = 'pinch';
    touchState.initDist = Math.hypot(
      e.touches[0].clientX - e.touches[1].clientX,
      e.touches[0].clientY - e.touches[1].clientY
    );
    touchState.initScale = canvasZoomScale;
    touchState.initPanX = canvasPanX;
    touchState.initPanY = canvasPanY;
  } else if (e.touches.length === 1) {
    touchState.active = true;
    touchState.startX = e.touches[0].clientX;
    touchState.startY = e.touches[0].clientY;
    touchState.initPanX = canvasPanX;
    touchState.initPanY = canvasPanY;

    // 더블 탭 감지 (320ms 이내): 확대 리셋 또는 해당 위치 확대
    if (now - touchState.lastTapTime < 320) {
      e.preventDefault();
      touchState.lastTapTime = 0;
      if (canvasZoomScale > 1.2) {
        resetCanvasZoom(true);
      } else {
        const rect = canvas.getBoundingClientRect();
        const tapX = (e.touches[0].clientX - rect.left) / canvasZoomScale;
        const tapY = (e.touches[0].clientY - rect.top) / canvasZoomScale;
        zoomToPoint(tapX, tapY, 2.2);
      }
      return;
    }
    touchState.lastTapTime = now;
    touchState.mode = canvasZoomScale > 1.05 ? 'pan' : 'none';
  }
}, { passive: false });

canvas.addEventListener('touchmove', (e) => {
  if (WIDGET_WINDOW || widgetOn()) return;

  if (touchState.mode === 'pinch' && e.touches.length === 2) {
    e.preventDefault();
    const curDist = Math.hypot(
      e.touches[0].clientX - e.touches[1].clientX,
      e.touches[0].clientY - e.touches[1].clientY
    );
    if (touchState.initDist > 0) {
      const factor = curDist / touchState.initDist;
      canvasZoomScale = clamp(touchState.initScale * factor, 1.0, 3.5);
      applyCanvasTransform(false);
    }
  } else if (touchState.mode === 'pan' && e.touches.length === 1 && canvasZoomScale > 1.05) {
    e.preventDefault();
    const dx = e.touches[0].clientX - touchState.startX;
    const dy = e.touches[0].clientY - touchState.startY;
    canvasPanX = touchState.initPanX + dx;
    canvasPanY = touchState.initPanY + dy;
    applyCanvasTransform(false);
  }
}, { passive: false });

canvas.addEventListener('touchend', (e) => {
  if (e.touches.length === 0) {
    touchState.mode = 'none';
    touchState.active = false;
    if (canvasZoomScale <= 1.05) {
      resetCanvasZoom(true);
    }
  } else if (e.touches.length === 1) {
    touchState.mode = canvasZoomScale > 1.05 ? 'pan' : 'none';
    touchState.startX = e.touches[0].clientX;
    touchState.startY = e.touches[0].clientY;
    touchState.initPanX = canvasPanX;
    touchState.initPanY = canvasPanY;
  }
});

function hitTest(px, py) {
  const lx = px / U, ly = py / U;
  return desks.find((d) => lx >= d.x && lx < d.x + CELL_W && ly >= d.y && ly < d.y + CELL_H) || null;
}

function hitWhiteboard(px, py) {
  if (!curWhiteboardBox) return false;
  const lx = px / U, ly = py / U;
  return lx >= curWhiteboardBox.x - 1 && lx <= curWhiteboardBox.x + curWhiteboardBox.w + 1 &&
         ly >= curWhiteboardBox.y - 1 && ly <= curWhiteboardBox.y + curWhiteboardBox.h + 1;
}

canvas.addEventListener('mousemove', (e) => {
  const pos = getCanvasPos(e);
  const hitWb = hitWhiteboard(pos.x, pos.y);
  if (hitWb) {
    hoverId = null;
    canvas.style.cursor = 'pointer';
    tooltipEl.replaceChildren();
    const add = (tag, text, cls) => { const el = document.createElement(tag); el.textContent = text; if (cls) el.className = cls; tooltipEl.append(el); };
    add('b', T('📋 전사 화이트보드 (Todo 칸반)', '📋 Team Whiteboard (Todo Kanban)'));
    add('div', T('모든 세션의 진행 중인 할 일 목록을 한눈에 확인합니다.', 'View active to-dos across all sessions at a glance.'));
    add('div', T('클릭하여 열기', 'Click to open'), 'dim');
    tooltipEl.hidden = false;
    const r = tooltipEl.getBoundingClientRect();
    tooltipEl.style.left = clamp(e.clientX + 14, 4, viewW() - r.width - 4) + 'px';
    tooltipEl.style.top = clamp(e.clientY + 14, 4, innerHeight - r.height - 4) + 'px';
    return;
  }
  const fHit = hitFurniture(pos.x, pos.y);
  if (fHit) {
    hoverId = null;
    canvas.style.cursor = 'pointer';
    tooltipEl.replaceChildren();
    const add = (tag, text, cls) => { const el = document.createElement(tag); el.textContent = text; if (cls) el.className = cls; tooltipEl.append(el); };
    add('b', fHit.name || T('🛋️ 오피스 가구', '🛋️ Office Decor'));
    add('div', T('클릭하여 상호작용하기', 'Click to interact'), 'dim');
    tooltipEl.hidden = false;
    const r = tooltipEl.getBoundingClientRect();
    tooltipEl.style.left = clamp(e.clientX + 14, 4, viewW() - r.width - 4) + 'px';
    tooltipEl.style.top = clamp(e.clientY + 14, 4, innerHeight - r.height - 4) + 'px';
    return;
  }
  const hit = hitTest(pos.x, pos.y);
  hoverId = hit ? hit.s.id : null;
  canvas.style.cursor = hit ? 'pointer' : 'default';
  if (!hit) { tooltipEl.hidden = true; return; }
  const s = hit.s, a = actOf(s);
  tooltipEl.replaceChildren();
  const add = (tag, text, cls) => { const el = document.createElement(tag); el.textContent = text; if (cls) el.className = cls; tooltipEl.append(el); };
  const clip = (t, n) => (t.length > n ? t.slice(0, n - 1) + '…' : t);
  const note = noteOf(s.id);
  add('b', displayName(s));
  if (note.nick) add('div', T(`세션 이름: ${s.name}`, `Session name: ${s.name}`), 'dim');
  add('div', `${s.icon} ${a.label} — ${s.text}`);
  if (note.memo) add('div', '📝 ' + clip(note.memo.replace(/\s+/g, ' ').trim(), 90));
  if (s.reply && s.act === 'idle') add('div', '💬 ' + clip(s.reply, 90));
  if (s.prompt) add('div', '📨 ' + clip(s.prompt, 90), 'dim');
  (s.interns || []).forEach((it) => add('div', `${it.icon} ${it.name}: ${it.text}`, 'dim'));   // 이름이 이미 "탐색 인턴" 꼴
  add('div', T('클릭하면 자세히 보기', 'Click for details'), 'dim');
  tooltipEl.hidden = false;
  const r = tooltipEl.getBoundingClientRect();
  tooltipEl.style.left = clamp(e.clientX + 14, 4, viewW() - r.width - 4) + 'px';
  tooltipEl.style.top = clamp(e.clientY + 14, 4, innerHeight - r.height - 4) + 'px';
});

canvas.addEventListener('mouseleave', () => { hoverId = null; tooltipEl.hidden = true; });

// ⚡ 책상 더블 클릭 시 해당 세션의 콘솔/터미널 창으로 즉시 점프
canvas.addEventListener('dblclick', (e) => {
  if (WIDGET_WINDOW || widgetOn() || REMOTE) return;
  const pos = getCanvasPos(e);
  const hit = hitTest(pos.x, pos.y);
  if (hit && hit.s) {
    jumpToConsole(hit.s.id, hit.s);
  }
});

canvas.addEventListener('click', (e) => {
  const pos = getCanvasPos(e);
  if (hitWhiteboard(pos.x, pos.y)) {
    openWhiteboardModal();
    return;
  }
  const fHit = hitFurniture(pos.x, pos.y);
  if (fHit) {
    interactFurniture(fHit);
    return;
  }
  const hit = hitTest(pos.x, pos.y);
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
if ($('pPet')) {
  $('pPet').addEventListener('click', () => {
    if (!selectedId) return;
    const active = petOn() && petSessionPref() === selectedId;
    if (active) {
      setPetSession('');
    } else {
      setPetSession(selectedId);
      if (!petOn()) setPet(true);
    }
  });
}
$('pSeatL').addEventListener('click', () => { if (selectedId) { moveSeat(selectedId, -1); markQuest('move_seat'); } });
$('pSeatR').addEventListener('click', () => { if (selectedId) { moveSeat(selectedId, 1); markQuest('move_seat'); } });
// 작업 폴더를 탐색기로 — 경로는 앱이 자기 스냅샷에서 찾는다(화면이 보낸 경로를 그대로 열지 않음)
$('pOpenDir').addEventListener('click', () => { if (selectedId) send({ cmd: 'open-folder', id: selectedId }); });
if ($('pFocusWin')) {
  $('pFocusWin').addEventListener('click', () => { if (selectedId) send({ cmd: 'focus-session', id: selectedId }); });
}

// 빠른 직원 검색(HUD 🔍 버튼 및 / 단축키)
const btnSearch = $('btnSearch'), searchWrapEl = $('searchWrap'), hudSearchEl = $('hudSearch'), hudSearchClearEl = $('hudSearchClear');

function setSearchOpen(open, clear = false) {
  if (!searchWrapEl || !hudSearchEl) return;
  if (open) markQuest('try_search');
  if (clear || !open) {
    searchQuery = '';
    hudSearchEl.value = '';
    layoutKey = '';
  }
  searchWrapEl.hidden = !open;
  if (btnSearch) btnSearch.classList.toggle('on', open);
  refreshHud();
  if (open) hudSearchEl.focus();
}

if (btnSearch) btnSearch.addEventListener('click', () => setSearchOpen(searchWrapEl.hidden));
if (hudSearchClearEl) hudSearchClearEl.addEventListener('click', () => setSearchOpen(false, true));
if (hudSearchEl) {
  hudSearchEl.addEventListener('input', () => {
    searchQuery = hudSearchEl.value;
    layoutKey = '';
    refreshHud();
  });
  hudSearchEl.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      setSearchOpen(false, true);
    } else if (e.key === 'Enter') {
      e.preventDefault();
      const first = visibleSessions()[0];
      if (first) selectSession(first.id);
    }
  });
}

// 키보드 단축키: Esc(닫기), /(직원 검색), 1~9(책상 직원 선택), M(미니위젯), W(작게 보기), R(근무 일지), A(업적 도감)
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') {
    if (connectGuideEl && !connectGuideEl.hidden) { closeConnectGuide(); return; }
    if (whiteboardModalEl && !whiteboardModalEl.hidden) { closeWhiteboardModal(); return; }
    if (achieveModalEl && !achieveModalEl.hidden) { closeAchieveModal(); return; }
    if (WIDGET_WINDOW) return;
    const inInput = e.target instanceof Element && e.target.matches('input, textarea, select');
    if (inInput) { e.target.blur(); return; }
    if (searchWrapEl && !searchWrapEl.hidden) { setSearchOpen(false, true); return; }
    if (broadcastModalEl && !broadcastModalEl.hidden) { closeBroadcastModal(); return; }
    if (nightShiftModalEl && !nightShiftModalEl.hidden) { closeNightShiftModal(); return; }
    if (shopModalEl && !shopModalEl.hidden) { closeShopModal(); return; }
    if (!settingsEl.hidden) { setSettingsOpen(false); return; }
    selectedId = null;
    renderPanel();
    return;
  }
  if (WIDGET_WINDOW) return;
  const inInput = e.target instanceof Element && e.target.matches('input, textarea, select');
  if (inInput) return;

  // ⚡ Alt+1~9 또는 Ctrl+1~9: 해당 직원의 콘솔/터미널 창으로 즉시 점프
  if ((e.altKey || e.ctrlKey) && e.key >= '1' && e.key <= '9' && !REMOTE) {
    e.preventDefault();
    const idx = Number(e.key) - 1;
    const list = visibleSessions();
    if (list[idx]) {
      jumpToConsole(list[idx].id, list[idx]);
    }
    return;
  }

  // ⚡ J 키: 현재 선택된 직원의 콘솔/터미널 창으로 점프
  if (e.key.toLowerCase() === 'j' && selectedId && !e.ctrlKey && !e.altKey && !e.metaKey && !REMOTE) {
    e.preventDefault();
    const s = data.sessions.find((x) => x.id === selectedId);
    if (s) jumpToConsole(selectedId, s);
    return;
  }

  if (e.ctrlKey || e.altKey || e.metaKey) return;
  if (e.key === '/') {
    e.preventDefault();
    setSearchOpen(true);
    return;
  }
  if (e.key >= '1' && e.key <= '9' && settingsEl.hidden) {
    const idx = Number(e.key) - 1;
    const list = visibleSessions();
    if (list[idx]) selectSession(list[idx].id);
    return;
  }
  const k = e.key.toLowerCase();
  if (k === 'm' && !REMOTE) {
    e.preventDefault();
    setPet(!petOn());
  } else if (k === 'w') {
    e.preventDefault();
    setWidget(!widgetOn());
  } else if (k === 'r') {
    e.preventDefault();
    btnToday.click();
  } else if (k === 'a') {
    e.preventDefault();
    if (btnAchieve) btnAchieve.click();
  } else if (k === 's') {
    e.preventDefault();
    if (shopModalEl && !shopModalEl.hidden) closeShopModal();
    else openShopModal();
  } else if (k === 'b') {
    e.preventDefault();
    if (broadcastModalEl && !broadcastModalEl.hidden) closeBroadcastModal();
    else openBroadcastModal();
  } else if (k === 'n') {
    e.preventDefault();
    if (nightShiftModalEl && !nightShiftModalEl.hidden) closeNightShiftModal();
    else openNightShiftModal();
  }
});

// 상세 패널의 별명·메모·캐릭터 꾸미기 칸. 글자를 칠 때마다 바로 저장한다(따로 저장 버튼 없음)
const nickEl = $('pNick'), memoEl = $('pMemo'), lookBarEl = $('pLookBar');
let noteFor = null;   // 입력칸이 지금 어느 세션 것인지

const DECO_ITEMS = [
  [0, '☕', T('머그컵', 'Mug')],
  [1, '🪴', T('화분', 'Plant')],
  [2, '📚', T('책', 'Books')],
  [3, '🖼️', T('액자', 'Frame')],
];

function renderLookBar(s) {
  if (!lookBarEl || REMOTE) return;
  const n = noteOf(s.id);
  const L = looks(s.id);
  const roll = elem('button', 'mini', T('🎲 모습 바꾸기', '🎲 Change look'));
  roll.title = T('직원의 헤어·안경·옷 색상을 다른 조합으로 바꿉니다', 'Change hair, glasses, and outfit colors');
  roll.addEventListener('click', () => {
    setNote(s.id, { look: ((noteOf(s.id).look || 0) % 99) + 1 });
    renderLookBar(s);
    if (petOn()) renderPet();
    markQuest('decorate_desk');
  });
  const decos = DECO_ITEMS.map(([idx, ic, label]) => {
    const b = elem('button', 'mini' + (L.deco === idx ? ' on' : ''), ic);
    b.title = T(`책상 소품: ${label}`, `Desk item: ${label}`);
    b.addEventListener('click', () => {
      setNote(s.id, { deco: idx });
      renderLookBar(s);
      if (petOn()) renderPet();
      markQuest('decorate_desk');
    });
    return b;
  });
  const children = [roll, ...decos];
  if (n.look || n.deco !== undefined) {
    const rst = elem('button', 'mini', '↺');
    rst.title = T('원래 기본 모습·소품으로 되돌리기', 'Restore default look & item');
    rst.addEventListener('click', () => {
      setNote(s.id, { look: 0, deco: -1 });
      renderLookBar(s);
      if (petOn()) renderPet();
    });
    children.push(rst);
  }
  lookBarEl.replaceChildren(...children);
}

nickEl.addEventListener('input', () => {
  if (!noteFor) return;
  if (REMOTE) { queuePhoneEdit(noteFor); return; }   // 휴대폰: PC로 보낸다(PC가 반영해 다시 올리면 보임)
  setNote(noteFor, { nick: nickEl.value.trim() });
  renderPanel();
  refreshSettings();   // 체크 목록의 이름도
  if (nickEl.value.trim()) markQuest('set_nickname');
});
memoEl.addEventListener('input', () => {
  if (!noteFor) return;
  if (REMOTE) queuePhoneEdit(noteFor);
  else setNote(noteFor, { memo: memoEl.value });
  if (memoEl.value.trim()) markQuest('write_memo');
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

// ───────────────────────────── 오늘의 근무 일지 · 비용 · 문맥 ─────────────────────────────
// 앱이 세션마다 보내는 today / yesterday / week: {work(ms), prompts, in, out, cacheRead, cacheWrite, tools:[{n, c}]} — 인턴 몫 포함

const REPORT_ID = '@today';
let reportRange = 'today';   // 'today' | 'yesterday' | 'week'

function fmtTok(n) {
  if (!n) return '0';
  if (n < 1000) return String(n);
  if (n < 1e6) return (n / 1e3).toFixed(n < 1e4 ? 1 : 0) + 'K';
  return (n / 1e6).toFixed(n < 1e7 ? 2 : 1) + 'M';
}
const tokIn = (t) => (t.in || 0) + (t.cacheRead || 0) + (t.cacheWrite || 0);   // 모델이 읽은 전체(캐시 포함)
const fmtWork = (ms) => (ms >= 60000 ? fmtDur(ms) : ms > 0 ? T('1분 미만', 'under 1m') : T('0분', '0m'));

/** 모델별 100만 토큰당 단가($)로 예상 API 비용과 캐시 절약액을 계산한다. */
function calcTokenCost(t, model) {
  if (!t) return { cost: 0, saved: 0 };
  const m = String(model || '').toLowerCase();
  let p = { in: 3, out: 15, cw: 3.75, cr: 0.30 };   // 기본: Sonnet 4 / 3.7
  if (m.includes('haiku')) p = { in: 0.80, out: 4, cw: 1.00, cr: 0.08 };
  else if (m.includes('opus-4-5') || m.includes('opus-4-6') || m.includes('opus-5')) p = { in: 5, out: 25, cw: 6.25, cr: 0.50 };
  else if (m.includes('opus')) p = { in: 15, out: 75, cw: 18.75, cr: 1.50 };

  const ti = t.in || 0, to = t.out || 0, cr = t.cacheRead || 0, cw = t.cacheWrite || 0;
  const cost = (ti * p.in + to * p.out + cw * p.cw + cr * p.cr) / 1e6;
  const fullInCost = ((ti + cr + cw) * p.in + to * p.out) / 1e6;
  const saved = Math.max(0, fullInCost - cost);
  return { cost, saved };
}

function fmtUsd(v) {
  if (!v || v < 0.005) return '$0.00';
  if (v < 10) return '$' + v.toFixed(2);
  return '$' + v.toFixed(1);
}

const MAX_CTX_TOKENS = 200000;

function makeCtxBox(ctx) {
  if (!ctx || ctx <= 0) return null;
  const pct = Math.min(100, Math.round((ctx / MAX_CTX_TOKENS) * 100));
  const wrap = elem('div', 'ctx-wrap');
  const warnHint = pct >= 80 ? T(' · /compact 권장', ' · /compact recommended') : '';
  wrap.append(elem('div', '', T(`📐 대화 문맥 ${fmtTok(ctx)} / 200K (${pct}%)${warnHint}`, `📐 Context ${fmtTok(ctx)} / 200K (${pct}%)${warnHint}`)));
  const bar = elem('div', 'ctx-bar' + (pct >= 80 ? ' high' : pct >= 60 ? ' warn' : ''));
  const fill = elem('i');
  fill.style.width = `${pct}%`;
  bar.append(fill);
  wrap.append(bar);
  return wrap;
}

// 📊 대화 기억량 (Context Window) 게이지 렌더링
function renderContextMeter(s) {
  const el = $('pCtxMeter');
  if (!el) return;
  if (!s || !s.ctx || s.ctx <= 0) {
    el.hidden = true;
    return;
  }
  el.hidden = false;
  el.replaceChildren();

  const tokens = s.ctx || 0;
  const pct = Math.min(100, Math.round((tokens / MAX_CTX_TOKENS) * 100));
  const remain = Math.max(0, MAX_CTX_TOKENS - tokens);

  let level = 'ok';
  let badgeText = T('✨ 쾌적한 상태', '✨ Optimal');
  let isWarning = false;

  if (pct >= 90) {
    level = 'danger';
    badgeText = T('🚨 뇌 용량 포화! 즉시 /compact 권장', '🚨 Context Full! /compact now');
    isWarning = true;
  } else if (pct >= 80) {
    level = 'warn';
    badgeText = T('⚠️ /compact 권장', '⚠️ /compact recommended');
    isWarning = true;
  } else if (pct >= 60) {
    level = 'mid';
    badgeText = T('💡 대화 누적 중', '💡 Growing context');
  }

  const head = elem('div', 'ctx-meter-head');
  const title = elem('span', 'ctx-meter-title', T('🧠 대화 기억량 (Context Window)', '🧠 Context Window'));
  const val = elem('span', 'ctx-meter-val', `${fmtTok(tokens)} / 200K (${pct}%)`);
  head.append(title, val);

  const track = elem('div', 'ctx-meter-track');
  const fill = elem('div', `ctx-meter-fill ctx-${level}`);
  fill.style.width = `${Math.max(4, pct)}%`;
  track.append(fill);

  const sub = elem('div', 'ctx-meter-sub');
  const remainSpan = elem('span', 'ctx-meter-remain', T(`남은 기억: ${fmtTok(remain)} (${100 - pct}%)`, `Remaining: ${fmtTok(remain)} (${100 - pct}%)`));
  const badge = elem('span', `ctx-meter-badge ctx-badge-${level}`, badgeText);

  if (isWarning) {
    badge.title = T('클릭하여 /compact 명령어를 클립보드에 복사합니다', 'Click to copy /compact to clipboard');
    badge.addEventListener('click', () => {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText('/compact').then(() => {
          showToast(T('/compact 명령어가 복사되었습니다!', 'Copied /compact to clipboard!'), '📋');
        }).catch(() => {});
      }
    });
  }
  sub.append(remainSpan, badge);

  el.append(head, track, sub);
}

function todayLines(t, s) {
  const lines = [];
  const ctxBox = s ? makeCtxBox(s.ctx) : null;
  if (ctxBox) lines.push(ctxBox);
  if (!t) {
    lines.push(elem('div', 'dimline', T('오늘 기록이 없어요', 'Nothing recorded today')));
    return lines;
  }
  const { cost, saved } = calcTokenCost(t, s?.model);
  const tools = (t.tools || []).slice(0, 4).map((x) => `${x.n} ${x.c}`).join(' · ');
  lines.push(
    elem('div', '', T(`⏱️ 일한 시간 ${fmtWork(t.work)} · 💬 지시 ${t.prompts}번`, `⏱️ Worked ${fmtWork(t.work)} · 💬 ${t.prompts} prompts`)),
    elem('div', '', T(`🪙 토큰 입력 ${fmtTok(tokIn(t))} (캐시 읽기 ${fmtTok(t.cacheRead)}) · 출력 ${fmtTok(t.out)}`,
      `🪙 Tokens in ${fmtTok(tokIn(t))} (cache read ${fmtTok(t.cacheRead)}) · out ${fmtTok(t.out)}`)),
    elem('div', '', T(`💵 API 환산 약 ${fmtUsd(cost)}` + (saved >= 0.01 ? ` (캐시로 ${fmtUsd(saved)} 절약)` : ''),
      `💵 Est. API ${fmtUsd(cost)}` + (saved >= 0.01 ? ` (saved ${fmtUsd(saved)} via cache)` : '')))
  );
  if (tools) lines.push(elem('div', 'dimline', `🔧 ${tools}`));
  return lines;
}

function renderReport() {
  const getStat = (s) => (reportRange === 'yesterday' ? s.yesterday : reportRange === 'week' ? (s.week || s.today) : s.today);
  const rows = data.sessions.filter((s) => getStat(s)).sort((a, b) => getStat(b).work - getStat(a).work);
  const sum = (f) => rows.reduce((n, s) => n + f(getStat(s), s), 0);
  const maxWork = Math.max(1, ...rows.map((s) => getStat(s).work));
  const d = new Date();
  const rangeTitle = reportRange === 'yesterday'
    ? T('어제', 'Yesterday')
    : reportRange === 'week'
      ? T('최근 7일', 'Last 7 days')
      : T(`${d.getMonth() + 1}월 ${d.getDate()}일(오늘)`, `${d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })} (Today)`);

  const tabs = elem('div', 'rtabs');
  for (const [id, label] of [['today', T('오늘', 'Today')], ['yesterday', T('어제', 'Yesterday')], ['week', T('최근 7일', 'Last 7 days')]]) {
    const b = elem('button', reportRange === id ? 'on' : '', label);
    b.addEventListener('click', () => { reportRange = id; renderReport(); });
    tabs.append(b);
  }

  const totalCost = sum((t, s) => calcTokenCost(t, s.model).cost);
  const totalSaved = sum((t, s) => calcTokenCost(t, s.model).saved);

  const copyBtn = elem('button', 'btn-copy-md', T('📋 일지 복사', '📋 Copy Markdown'));
  copyBtn.title = T('근무 일지를 마크다운 형식으로 클립보드에 복사합니다', 'Copy work log as Markdown to clipboard');
  copyBtn.addEventListener('click', () => {
    let md = `# 📊 Claude Office ${T('근무 일지', 'Work Log')} (${rangeTitle})\n\n`;
    md += `### ⏱️ ${T('전체 요약', 'Summary')}\n`;
    md += `- **${T('일한 시간', 'Work time')}**: ${fmtWork(sum((t) => t.work))}\n`;
    md += `- **${T('지시', 'Prompts')}**: ${sum((t) => t.prompts)}` + T('번\n', '\n');
    md += `- **${T('토큰 (입력+출력)', 'Tokens (in+out)')}**: ${fmtTok(sum((t) => tokIn(t) + t.out))} (${T('입력', 'in')} ${fmtTok(sum((t) => tokIn(t)))}, ${T('출력', 'out')} ${fmtTok(sum((t) => t.out))})\n`;
    md += `- **${T('API 환산 비용', 'Est. API cost')}**: ${fmtUsd(totalCost)}` + (totalSaved >= 0.01 ? ` (${T('캐시 절약', 'saved')} ${fmtUsd(totalSaved)})\n` : '\n');
    md += `\n### 👥 ${T('직원별 근무 내역', 'Employee Details')}\n`;
    for (const s of rows) {
      const t = getStat(s);
      const { cost } = calcTokenCost(t, s.model);
      const tools = (t.tools || []).map((x) => `${x.n} (${x.c})`).join(', ');
      md += `- **${displayName(s)}** (${s.model || 'Claude'}): ${fmtWork(t.work)} · ${t.prompts}${T('번 지시', ' prompts')} · ${T('토큰', 'tokens')} ${fmtTok(tokIn(t))}/${fmtTok(t.out)} · ${fmtUsd(cost)}\n`;
      if (tools) md += `  - ${T('주요 도구', 'Top tools')}: ${tools}\n`;
    }
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(md).then(() => {
        copyBtn.textContent = T('✅ 복사 완료!', '✅ Copied!');
        setTimeout(() => { copyBtn.textContent = T('📋 일지 복사', '📋 Copy Markdown'); }, 2000);
      }).catch(() => {});
    }
  });

  const tabRow = elem('div', 'rrow');
  tabRow.append(tabs, copyBtn);

  const head = [
    elem('h2', '', T('📊 근무 일지', '📊 Work log')),
    elem('div', 'dim', T(`${rangeTitle} · 직원 ${rows.length}명 · 인턴 몫 포함`, `${rangeTitle} · ${rows.length} employees · interns included`)),
    tabRow,
  ];

  const tot = elem('div', 'rtot');
  for (const [label, value, sub] of [
    [T('일한 시간', 'Work time'), fmtWork(sum((t) => t.work)), ''],
    [T('지시', 'Prompts'), T(`${sum((t) => t.prompts)}번`, `${sum((t) => t.prompts)}`), ''],
    [T('토큰(입력+출력)', 'Tokens (in+out)'), fmtTok(sum((t) => tokIn(t) + t.out)), ''],
    [T('API 환산 비용', 'Est. API cost'), fmtUsd(totalCost), totalSaved >= 0.01 ? T(`캐시 절약 ${fmtUsd(totalSaved)}`, `Saved ${fmtUsd(totalSaved)}`) : ''],
  ]) {
    const box = elem('div', '', label);
    box.append(elem('b', '', value));
    if (sub) box.append(elem('span', 'sub', sub));
    tot.append(box);
  }

  const list = rows.map((s) => {
    const t = getStat(s);
    const { cost } = calcTokenCost(t, s.model);
    const row = elem('div', 'rrow');
    const h = elem('div', 'rh');
    h.append(elem('span', '', `${s.icon} ${displayName(s)}`), elem('span', '', `${fmtWork(t.work)} · ${fmtUsd(cost)}`));
    const bar = elem('div', 'bar'), fill = elem('i');
    fill.style.width = `${Math.round((t.work / maxWork) * 100)}%`;
    bar.append(fill);
    const tools = (t.tools || []).slice(0, 3).map((x) => `${x.n} ${x.c}`).join(' · ');
    const ctxPct = s.ctx ? ` · ${T('문맥', 'ctx')} ${Math.min(100, Math.round((s.ctx / MAX_CTX_TOKENS) * 100))}%` : '';
    row.append(h, bar, elem('div', 'rd', T(`지시 ${t.prompts}번 · 토큰 ${fmtTok(tokIn(t))}/${fmtTok(t.out)}${ctxPct}`,
      `${t.prompts} prompts · tokens ${fmtTok(tokIn(t))}/${fmtTok(t.out)}${ctxPct}`) + (tools ? ' · ' + tools : '')));
    row.title = T('누르면 이 직원 보기', 'Click to view this employee');
    row.addEventListener('click', () => selectSession(s.id));
    return row;
  });

  const emptyMsg = reportRange === 'yesterday'
    ? T('어제 기록된 근무 내역이 없어요.', 'No work recorded yesterday.')
    : reportRange === 'week'
      ? T('최근 7일 동안 기록된 근무 내역이 없어요.', 'No work recorded in the last 7 days.')
      : T('오늘은 아직 일한 직원이 없어요.', 'Nobody has worked today yet.');

  const acBanner = elem('button', 'rank-open-btn', T('🏆 전사 업적 도감 & 명예의 전당 보기', '🏆 View Achievements & Hall of Fame'));
  acBanner.style.width = '100%';
  acBanner.style.padding = '6px 10px';
  acBanner.style.margin = '4px 0 8px';
  acBanner.style.textAlign = 'center';
  acBanner.style.fontSize = '12px';
  acBanner.addEventListener('click', () => openAchieveModal());

  $('pReport').replaceChildren(...head, acBanner, tot, ...(list.length ? list : [elem('div', 'dim', emptyMsg)]),
    elem('p', 'rnote', T('일한 시간: 지시를 받은 뒤 답을 마칠 때까지(승인 대기·명령 실행 포함, 30분 넘게 조용한 틈 제외). 비용은 모델별 API 표준 단가 환산 추정치입니다.',
      'Work time: from receiving a prompt until the answer finishes (approval waits and running commands included, quiet gaps over 30 min excluded). Cost is an API rate estimate.')));
}

function renderPanel() {
  const report = selectedId === REPORT_ID;
  btnToday.classList.toggle('on', report);
  $('pReport').hidden = !report;
  $('pMain').hidden = report;
  if (report) {
    panelEl.hidden = false;
    noteFor = null;
    syncDrawers();
    renderReport();
    return;
  }
  const s = selectedId ? data.sessions.find((x) => x.id === selectedId) : null;
  panelEl.hidden = !s;
  syncDrawers();
  if (!s) { noteFor = null; return; }
  const a = actOf(s);
  const note = noteOf(s.id);
  $('pName').textContent = displayName(s);
  // 별명을 붙였으면 원래 세션 이름(/rename 이름·자동 제목)을 아래에 남겨 둔다
  $('pTitle').textContent = note.nick ? T('세션 이름: ', 'Session name: ') + `${s.name}${s.title ? ' · ' + s.title : ''}` : (s.title || '');

  // 세션 클릭 시에는 업적/직급 카드를 표시하지 않고 기존 서랍 내용을 깔끔하게 유지
  const oldRankCard = $('pRankCard');
  if (oldRankCard) oldRankCard.remove();

  // 입력칸은 다른 직원으로 바꿨을 때만 채운다(스냅샷마다 채우면 쓰는 도중 글자가 되돌아간다)
  if (noteFor !== s.id) {
    noteFor = s.id;
    $('pSync').textContent = '';
    nickEl.value = note.nick || '';
    nickEl.placeholder = T(`별명 (비우면 "${s.name}")`, `Nickname (empty = "${s.name}")`);
    memoEl.value = note.memo || '';
  }
  renderLookBar(s);
  // ⚡ 상단 콘솔 점프 버튼 & 📊 컨텍스트 게이지
  const jumpBtnTop = $('pFocusConsoleTop');
  if (jumpBtnTop) {
    jumpBtnTop.hidden = REMOTE || !s.cwd;
    jumpBtnTop.onclick = () => {
      jumpToConsole(s.id, s);
    };
  }
  renderContextMeter(s);
  const st = $('pState');
  st.textContent = `${s.icon} ${a.label} — ${s.text}`;
  st.style.color = a.tone;

  let secretEl = $('pSecretAlert');
  if (!secretEl) {
    secretEl = elem('div', 'secret-alert');
    secretEl.id = 'pSecretAlert';
    $('pState').insertAdjacentElement('afterend', secretEl);
  }
  secretEl.hidden = !s.secret;
  if (s.secret) {
    secretEl.innerHTML = T(
      '🛡️ <strong>시크릿 노출 주의:</strong> API 키 또는 인증 토큰 패턴이 감지되었습니다. 키 재발급 및 환경변수(env) 분리를 권장합니다.',
      '🛡️ <strong>Secret Exposure Alert:</strong> API key or token pattern detected. Rotating the key and using environment variables is recommended.'
    );
  }

  // 🛑 긴급 작업 중단 버튼
  let stopBtn = $('pStopBtn');
  if (!stopBtn) {
    stopBtn = elem('button', 'mini btn-stop', T('🛑 작업 중단', '🛑 Stop task'));
    stopBtn.id = 'pStopBtn';
    stopBtn.title = T('현재 실행 중인 명령/작업을 긴급 중단합니다', 'Halt current task in this session');
    stopBtn.addEventListener('click', async () => {
      if (selectedId) {
        stopBtn.disabled = true;
        stopBtn.textContent = T('🛑 신호 전송 중…', '🛑 Sending stop…');
        await stopSession(selectedId);
        stopBtn.textContent = T('🛑 중단 전송됨', '🛑 Stop Sent');
        setTimeout(() => {
          if (stopBtn) {
            stopBtn.textContent = T('🛑 작업 중단', '🛑 Stop task');
            stopBtn.disabled = false;
          }
        }, 2000);
        markQuest('emergency_stop');
      }
    });
    if ($('pPet')) $('pPet').insertAdjacentElement('beforebegin', stopBtn);
  }
  const isBusy = (a.busy || a.wait) && s.act !== 'away';
  if (stopBtn) stopBtn.hidden = !isBusy;

  if ($('pPet')) {
    const isRep = petOn() && resolvePetSession()?.id === s.id;
    const pinnedRep = petOn() && petSessionPref() === s.id;
    $('pPet').textContent = pinnedRep ? T('🪟 대표 직원 중', '🪟 Pinned MiniWidget')
      : isRep ? T('🪟 미니위젯 고정', '🪟 Pin MiniWidget')
      : T('🪟 미니위젯', '🪟 MiniWidget');
    $('pPet').style.borderColor = isRep ? 'var(--accent)' : '';
  }
  renderRemote(s);
  $('pCwd').textContent = s.cwd || s.team;
  $('pOpenDir').hidden = DEMO || !s.cwd;
  if ($('pFocusWin')) $('pFocusWin').hidden = REMOTE || !s.cwd;
  $('pTeam').textContent = teamLabel(s.team);
  $('pEntry').textContent = entryName(s.entry);
  $('pModel').textContent = s.model || '-';
  $('pBranch').textContent = s.branch || '-';
  $('pLast').textContent = s.last ? `${fmtTime(s.last)} (${fmtAgo(now() - s.last)})` : '-';
  $('pPrompt').textContent = s.prompt || T('(없음)', '(none)');

  let thinkEl = $('pThinking');
  if (!thinkEl) {
    thinkEl = elem('details', 'thinking-accordion');
    thinkEl.id = 'pThinking';
    const sum = elem('summary', '', T('🧠 Claude 생각 엿보기', "🧠 Peek at Claude's Thinking"));
    const body = elem('div', 'thinking-body');
    thinkEl.append(sum, body);
    $('pPrompt').insertAdjacentElement('afterend', thinkEl);
  }
  thinkEl.hidden = !s.thinking;
  if (s.thinking) {
    thinkEl.querySelector('.thinking-body').textContent = s.thinking;
  }

  if ($('pReplyWrap')) {
    const showFallbackReply = $('pRemote').hidden && !!s.reply;
    $('pReplyWrap').hidden = !showFallbackReply;
    if (showFallbackReply) $('pReply').textContent = s.reply;
  }

  let todoCard = $('pWhiteboardCard');
  if (!todoCard) {
    todoCard = elem('div', 'whiteboard-card');
    todoCard.id = 'pWhiteboardCard';
    $('pToday').insertAdjacentElement('beforebegin', todoCard);
  }
  const todos = s.todos || [];
  todoCard.hidden = todos.length === 0;
  if (todos.length > 0) {
    const doneCount = todos.filter((t) => t.status === 'completed').length;
    const head = elem('div', 'whiteboard-head');
    head.append(
      elem('span', '', T('📋 화이트보드 할 일', '📋 Whiteboard To-dos')),
      elem('span', 'dim', `${doneCount}/${todos.length} ` + T('완료', 'done'))
    );
    const ul = elem('ul', 'todo-list');
    for (const t of todos) {
      const isDone = t.status === 'completed', isProg = t.status === 'in_progress';
      const icon = isDone ? '✅' : isProg ? '⏳' : '⬜';
      const cls = isDone ? 'completed' : isProg ? 'in_progress' : 'pending';
      const li = elem('li', `todo-item ${cls}`);
      li.append(elem('span', 'todo-icon', icon), elem('span', 'todo-text', t.content));
      ul.append(li);
    }
    todoCard.replaceChildren(head, ul);
  }

  $('pToday').replaceChildren(...todayLines(s.today, s));
  const interns = s.interns || [];
  $('pInternsWrap').hidden = interns.length === 0;
  $('pInterns').replaceChildren(...interns.map((it) => listItem(it.icon, `${it.name}${it.desc ? ' · ' + it.desc : ''} — ${it.text}`)));
  $('pLog').replaceChildren(...(s.recent || []).map((r) => listItem(r.icon, r.text, fmtTime(r.t))));
  $('pId').textContent = T(`세션 ID ${s.id}`, `Session ID ${s.id}`);
}

// ───────────────────────────── 휴대폰 보기(원격) ─────────────────────────────
// PC가 Firebase Realtime Database 의 offices/<u> 에 올린 { v, z, t, iv, ct } 를 실시간 스트림(EventSource)으로 받아
// 이 기기 안에서 해독(WebCrypto AES-GCM) → 압축 풀기(gzip) → 사무실 화면에 그린다. 키는 주소의 # 뒤에만 있다.

let remoteAt = 0;        // PC가 마지막으로 올린 시각(ms) — 1분마다 생존 신호가 온다
let remoteError = '';
let remoteNode = null;   // 데이터베이스에서 받은 현재 값
let remoteKey = null;    // 해독(그리고 휴대폰에서 고친 별명·메모를 PC로 보낼 때 암호화) 키
let remoteEdit = false;  // PC가 휴대폰에서 고치기를 허용했는지(PC가 올린 요약의 edit)

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
  if (!remoteAt) return T('📡 PC 연결 중…', '📡 Connecting to the PC…');
  const off = remoteOffAt();
  if (off) return T(`💤 PC에서 꺼짐 · ${fmtTime(off)} (${fmtAgo(Date.now() - off)})`, `💤 Off on the PC · ${fmtTime(off)} (${fmtAgo(Date.now() - off)})`);
  const age = Date.now() - remoteAt;
  return age < 90000 ? T('📡 실시간', '📡 Live') : T(`📡 PC 응답 없음 · ${fmtAgo(age)}`, `📡 PC not responding · ${fmtAgo(age)}`);   // 생존 신호가 1분마다라 1분 반 넘게 없으면 끊긴 것
}

// ── 휴대폰에서 별명·메모 고치기(PC가 허용했을 때) ──
// 고친 것을 같은 키로 암호화해 inbox/<u> 에 남기면(추가만 가능한 규칙) PC가 몇 초 안에 가져가 반영하고 다시 올린다.
const bytesToB64 = (b) => btoa(String.fromCharCode(...b));
const NOTES_ON_PC = T('별명·메모는 PC에서 정합니다', 'Nicknames and memos are set on the PC');
const phoneEditTimers = new Map();

function applyRemoteEdit() {
  nickEl.readOnly = memoEl.readOnly = !remoteEdit;
  nickEl.placeholder = remoteEdit ? T('별명 (PC로 보냅니다)', 'Nickname (sent to the PC)') : NOTES_ON_PC;
  memoEl.placeholder = remoteEdit ? T('메모 (쓰면 PC로 보냅니다)', 'Memo (sent to the PC as you type)') : NOTES_ON_PC;
}

function setSync(text) { $('pSync').textContent = text; }

function queuePhoneEdit(id) {
  if (!remoteEdit || !remoteKey) return;
  clearTimeout(phoneEditTimers.get(id));
  setSync(T('✏️ 쓰는 중…', '✏️ Typing…'));
  phoneEditTimers.set(id, setTimeout(() => sendPhoneEdit(id), 1200));   // 다 쓸 때까지 기다렸다가 한 번에
}

async function sendPhoneEdit(id) {
  phoneEditTimers.delete(id);
  const ok = await postInbox({ id, nick: nickEl.value.trim().slice(0, 30), memo: memoEl.value.slice(0, 2000) });
  setSync(ok ? T('📤 PC로 보냈어요 — PC가 켜져 있으면 몇 초 뒤 반영돼요', '📤 Sent to the PC — applied in a few seconds if the PC is on') : lastPostError);
}

/**
 * 휴대폰 → PC: 같은 키로 암호화해 inbox/<u> 에 남긴다(추가만 가능한 규칙 — PC만 읽고 지운다).
 * 보낸 시각 t 와 한 번만 쓰는 무작위 값 n 을 붙인다(PC가 오래된 것·같은 것을 두 번 처리하지 않게). 실패하면 lastPostError 에 이유.
 */
let lastPostError = '';
async function postInbox(msg) {
  try {
    const n = Array.from(crypto.getRandomValues(new Uint8Array(12)), (x) => x.toString(16).padStart(2, '0')).join('');
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const plain = new TextEncoder().encode(JSON.stringify({ ...msg, t: Date.now(), n }));
    const additionalData = new TextEncoder().encode('inbox:' + remoteLink.u);
    const ct = new Uint8Array(await crypto.subtle.encrypt({ name: 'AES-GCM', iv, additionalData }, remoteKey, plain));
    const res = await fetch(`${remoteLink.db.replace(/\/+$/, '')}/inbox/${encodeURIComponent(remoteLink.u)}.json`,
      { method: 'POST', body: JSON.stringify({ v: 2, iv: bytesToB64(iv), ct: bytesToB64(ct) }) });
    lastPostError = res.ok ? '' : T(`⚠️ 보내지 못했어요(${res.status}) — 잠시 뒤 다시 보내 주세요`, `⚠️ Couldn't send (${res.status}) — try again in a moment`);
    return res.ok;
  } catch {
    lastPostError = T('⚠️ 보내지 못했어요 — 인터넷 연결을 확인해 주세요', "⚠️ Couldn't send — check your internet connection");
    return false;
  }
}

// ── 휴대폰 지시·승인: Claude Code 와 연결했고 🏃 자리 비움일 때 휴대폰에서 ──
// 상태는 PC가 요약과 함께 올린 remote(PC 앱 화면은 {type:"remote"} — 설정의 상태 줄·상단 🏃 표시에만 쓴다):
//   {installed, away, since, s:{세션:{listen, asks:[{id,text,detail,t}], said:{text,t,state}, reply:{text,t}}}}
// 휴대폰은 지시(say)·승인(ok)·거절(no)·자리 비움(away)을 postInbox 로 보낸다.
// 자리 비움은 손으로 켜지 않는다 — PC가 입력 없음·화면 잠금으로 켜고, 휴대폰은 PC를 아무도 안 쓸 때(idle)만 켤 수 있다(화면을 보고 있거나 지시를 보내면 저절로).
// 돌아와 입력하거나 잠금을 풀면 꺼진다.
const remoteOf =(id) => (remoteState?.installed ? remoteState.s?.[id] || {} : null);
const answered = new Map();      // 휴대폰에서 답한 승인 요청 → 누른 시각(PC에서 사라질 때까지 버튼을 잠근다)
const askEls = new Map();        // 승인 요청 → 그 칸(다시 그려도 거절 이유 칸에 쓰던 글이 남게)
const rAsksEl = $('rAsks'), sayEl = $('rSay'), rVoiceBtn = $('rVoice');
if (rVoiceBtn && sayEl) setupVoicePrompt(rVoiceBtn, sayEl);
let sayFor = null;               // 지시 칸이 어느 세션 것인지
let remoteNote = null;           // 방금 보낸 것의 안내 {sid, at, text}

let prevAskIds = null;

/** PC 앱 화면: 앱이 원격 상태를 알려 줬다. */
function onRemote(st) {
  if (st?.s) {
    const curAskIds = new Set();
    for (const sid of Object.keys(st.s)) {
      const asks = st.s[sid]?.asks;
      if (asks) for (const a of asks) curAskIds.add(a.id);
    }
    if (prevAskIds !== null) {
      let hasNew = false;
      for (const id of curAskIds) if (!prevAskIds.has(id)) { hasNew = true; break; }
      if (hasNew) playRetroSound('ask');
    }
    prevAskIds = curAskIds;
  }
  remoteState = st || null;
  if (WIDGET_WINDOW) { renderPet(); return; }
  refreshHookButton();
  refreshHud();
  renderPanel();
  refreshSettings();
  if (DEMO && petOn()) renderPet();
}

function remoteStatus() {
  const st = remoteState;
  if (!st) return { text: T('확인 중…', 'Checking…'), tone: 'var(--dim)' };
  if (!st.installed) return { text: T('🔕 Claude Code 연결 안 됨', '🔕 Not connected to Claude Code'), tone: 'var(--dim)' };
  if (!cloud?.enabled) return { text: T('⚠️ 휴대폰 연결이 꺼져 있어 휴대폰에서 보낼 수 없어요', '⚠️ The phone connection is off, so the phone cannot send'), tone: '#ffb347' };
  const n = Object.values(st.s || {}).filter((x) => x.listen).length;
  return { text: T(`📡 지시를 받을 수 있는 세션 ${n}개`, `📡 ${n} sessions can take instructions`)
    + (st.away ? T(` · 🏃 자리 비움 ${fmtTime(st.since)}부터`, ` · 🏃 Away since ${fmtTime(st.since)}`) : ''), tone: '#7dffa0' };
}

let rCancelBtn = null, rWakeBtn = null, rGuideBtn = null;

/**
 * 상세 패널의 지시·승인 칸 — 휴대폰 화면뿐 아니라 PC 본창 상세 패널에서도(Claude Code 와 연결했을 때) 바로 지시를 보내고 승인·거절·질문 선택을 할 수 있다.
 */
function renderRemote(s) {
  const r = remoteOf(s.id);
  const canShow = DEMO || (REMOTE ? !!r : claudeConnected() && s.act !== 'away');
  $('pRemote').hidden = !r || !canShow;
  if ($('pRemote').hidden) return;
  const h3 = $('pRemote').querySelector('h3');
  if (h3) h3.textContent = REMOTE ? T('📱 휴대폰 지시·승인', '📱 Instruct & approve from the phone') : T('💬 지시 · 승인', '💬 Instruct & approve');

  const away = !!remoteState.away, idle = !!remoteState.idle;
  const asks = r.asks || [];
  $('rStatus').textContent = !REMOTE
    ? (asks.length ? T(`✋ 지금 확인·답변을 기다리는 요청이 ${asks.length}건 있어요.`, `✋ ${asks.length} request(s) waiting for your approval or answer.`)
      : actOf(s).busy ? T('💻 일하는 중 — 보낸 지시는 지금 작업이 끝나는 대로 이어서 전달됩니다.',
        '💻 Working — an instruction will be delivered right after the current work ends.')
      : r.listen ? T('📡 대기 중 — 보내면 바로 이 대화에서 이어서 일합니다.', '📡 Ready — an instruction continues this conversation right away.')
      : T('⏳ 이 세션이 한 번 응답을 마치거나 새로 열리면 연결되어 지시가 전달됩니다.',
        "⏳ Connects after this session's next answer or when reopened; instructions are delivered then."))
    : !away ? (idle ? T('💤 PC를 안 쓰고 있어요 — 지금부터 여기서 지시·승인할 수 있어요.', '💤 Nobody is using the PC — you can instruct and approve here from now on.')
      : T('🖥️ PC를 쓰는 중이에요 — 자리를 뜨면 30초쯤 뒤부터 여기서 지시·승인할 수 있어요.',
        '🖥️ Someone is using the PC — about 30 s after you leave it, you can instruct and approve here.'))
    : actOf(s).busy || asks.length ? T('💻 일하는 중 — 보낸 지시는 이 대화에 이어서(지금 일 중간이나 끝나는 대로) 전합니다.',
      '💻 Working — an instruction joins this conversation (during the current work or right after it).')
    : r.listen ? T('📡 연결됨 — 보내면 바로 이 대화에서 이어서 일합니다.', '📡 Connected — an instruction continues this conversation right away.')
    : T('⏳ 아직 연결 전 — 이 세션이 한 번 더 답을 마치거나 새로 열면 연결돼요. 보낸 지시는 그때 전합니다.',
      "⏳ Not connected yet — it connects after this session's next answer or when it is reopened. Instructions are delivered then.");

  // 🛑 긴급 작업 중단 버튼 (일하는 중일 때 상시 노출)
  let rStopBtn = $('rStopBtn');
  if (!rStopBtn) {
    rStopBtn = elem('button', 'btn-stop-remote', T('🛑 긴급 작업 중단 (작업 즉시 중지)', '🛑 Emergency Stop (Halt task)'));
    rStopBtn.id = 'rStopBtn';
    rStopBtn.title = T('현재 실행 중인 작업을 즉시 중단합니다', 'Halt current task immediately');
    rStopBtn.addEventListener('click', async () => {
      if (selectedId) {
        rStopBtn.disabled = true;
        rStopBtn.textContent = T('🛑 비상정지 신호 전송 중…', '🛑 Sending stop…');
        await stopSession(selectedId);
        rStopBtn.textContent = T('🛑 비상정지 신호 전송됨', '🛑 Stop Signal Sent');
        setTimeout(() => {
          if (rStopBtn) {
            rStopBtn.textContent = T('🛑 긴급 작업 중단 (작업 즉시 중지)', '🛑 Emergency Stop (Halt task)');
            rStopBtn.disabled = false;
          }
        }, 2200);
        markQuest('emergency_stop');
      }
    });
    $('rStatus').insertAdjacentElement('afterend', rStopBtn);
  }
  const isWorking = (actOf(s).busy || asks.length > 0) && s.act !== 'away';
  rStopBtn.hidden = !isWorking;

  // 승인·질문 요청: 칸은 요청마다 한 번만 만든다
  const keep = new Set(asks.map((a) => a.id));
  for (const [id, el] of askEls) if (!keep.has(id) || el.dataset.sid !== s.id) { el.remove(); askEls.delete(id); }
  for (const a of asks) {
    let el = askEls.get(a.id);
    if (!el) { el = makeAsk(s.id, a); askEls.set(a.id, el); }
    rAsksEl.append(el);
    el.querySelector('.age').textContent = ' · ' + fmtAgo(now() - a.t);
    for (const b of el.querySelectorAll('button')) b.disabled = answered.has(a.id);
  }
  for (const [id, at] of answered) if (Date.now() - at > 120000) answered.delete(id);

  // 지시 보내기: PC 본창에서는 늘 보이고, 휴대폰에서는 자리 비움이거나 PC를 아무도 안 쓸 때 보인다
  $('rSayWrap').hidden = REMOTE && !(away || idle);
  if (sayFor !== s.id) { sayFor = s.id; sayEl.value = ''; }
  $('rQuickChips')?.remove();
  if (remoteNote && r.said && r.said.t > remoteNote.at && remoteNote.sid === s.id) remoteNote = null;   // PC가 처리했다
  $('rSaid').textContent = remoteNote && remoteNote.sid === s.id && Date.now() - remoteNote.at < 30000 ? remoteNote.text
    : saidText(r.said, actOf(s).busy || asks.length > 0);
  if (!rCancelBtn) {
    const rSaidEl = $('rSaid');
    rCancelBtn = elem('button', 'cancel-q', T('취소', 'Cancel'));
    rCancelBtn.title = T('아직 전달되지 않은 대기 중 지시를 취소합니다', 'Cancel the queued instruction');
    rCancelBtn.addEventListener('click', () => { if (selectedId) cancelQueuedSay(selectedId); });
    rWakeBtn = elem('button', 'wake-term', T('🖥️ 터미널로', '🖥️ Terminal'));
    rWakeBtn.title = T('터미널 창으로 이동 후 Ctrl+V로 붙여넣고 Enter를 누르세요 (지시가 복사됨)', 'Focus terminal & press Ctrl+V then Enter (copied)');
    rWakeBtn.addEventListener('click', () => {
      if (selectedId) onWakeTerminal(selectedId);
    });
    rGuideBtn = elem('button', 'guide-link', T('ℹ️ 연결 안내', 'ℹ️ Guide'));
    rGuideBtn.title = T('세션 최초 연결 방법 안내 보기', 'View first-time connection guide');
    rGuideBtn.addEventListener('click', () => {
      if (selectedId) openConnectGuide(selectedId);
    });
    const wrap = elem('span', 'said-wrap');
    rSaidEl.replaceWith(wrap);
    wrap.append(rSaidEl, rCancelBtn, rWakeBtn, rGuideBtn);
  }
  const isQueued = r.said && r.said.state === 'queued';
  rCancelBtn.hidden = !isQueued;
  if (rWakeBtn) rWakeBtn.hidden = !(isQueued && !r.listen);
  if (rGuideBtn) rGuideBtn.hidden = !(isQueued && !r.listen);

  // Claude 의 마지막 답(Stop 훅이 알려 준 것 또는 대화 기록의 마지막 답변 요약 중 더 최신인 것)
  const useStopReply = r.reply && r.reply.text && (!s.reply || (r.reply.t || 0) >= (s.last || 0));
  const replyObj = useStopReply ? r.reply : (s.reply ? { text: s.reply, t: s.last } : r.reply);
  $('rReplyWrap').hidden = !replyObj;
  if (replyObj) {
    $('rReplyHead').textContent = T(`💬 마지막 답 · ${fmtTime(replyObj.t)}`, `💬 Last answer · ${fmtTime(replyObj.t)}`);
    if ($('rReply').textContent !== replyObj.text) $('rReply').textContent = replyObj.text;   // 같으면 그대로(읽던 위치가 튀지 않게)
  }
}

/** 마지막 지시의 처리 상태. working: 그 세션이 일하는 중(아니면 못 건넨 까닭은 세션이 아직 연결 전이라서). */
function saidText(d, working) {
  if (!d) return '';
  const when = fmtTime(d.t);
  return d.state === 'delivered' ? T(`✅ 지시 전달 완료 · ${when}`, `✅ Delivered · ${when}`)
    : d.state === 'queued' ? (working
      ? T(`⏳ 대기열 등록됨 · ${when} (작업 완료 후 자동 전달)`, `⏳ Queued · ${when} (delivered after current task)`)
      : T(`⏳ 최초 1회 연결 필요 · ${when} — [🖥️ 터미널로] 클릭`, `⏳ First-time connection needed · ${when} — click [🖥️ Terminal]`))
    : T(`⚠️ 전하지 못했어요 · ${when} (PC 작업 중)`, `⚠️ Not delivered · ${when} (PC in use)`);
}

/**
 * 휴대폰 화면을 보고 있는 동안 PC를 아무도 안 쓰면(30초) PC에 자리 비움을 켜 달라고 한다 — 그때부터 승인도 휴대폰으로.
 * 예전 [지금 자리 비움으로] 버튼 대신(1.2.0). PC에 돌아와 움직이면 PC가 끈다. PC가 받아 켤 때까지 20초마다 다시.
 * PC도 그 순간 정말 비어 있는지 다시 본다(RemoteHub.AwayFromPhone) — 요약이 늦게 와도 PC를 쓰는 사람에게 끼어들지 않는다.
 */
let awayAskedAt = 0;
function askAwayWhileWatching() {
  if (!REMOTE || !remoteKey || document.visibilityState !== 'visible') return;
  const st = remoteState;
  if (!st?.installed || st.away || !st.idle || Date.now() - awayAskedAt < 20000) return;
  awayAskedAt = Date.now();
  postInbox({ k: 'away' });
}
if (REMOTE) document.addEventListener('visibilitychange', askAwayWhileWatching);

function makeAsk(sid, a) {
  const isQuestion = a.kind === 'question' || (Array.isArray(a.options) && a.options.length > 0);
  const el = elem('div', 'rask');
  el.dataset.sid = sid;
  const head = elem('div', '');
  head.append(
    elem('span', 'what', (isQuestion ? T('❓ 질문: ', '❓ Question: ') : T('✋ 승인 요청: ', '✋ Approve: ')) + a.text),
    elem('span', 'dim age')
  );
  el.append(head);
  if (a.detail) el.append(elem('div', isQuestion ? 'qtxt' : 'cmd', a.detail));
  if (a.diff) el.append(makeDiffAccordion(a.diff));

  const btns = elem('div', 'btns');
  const why = elem('input', 'field');
  why.type = 'text';
  why.maxLength = 1000;

  if (isQuestion) {
    if (Array.isArray(a.options) && a.options.length > 0) {
      const opts = elem('div', 'opts');
      for (const opt of a.options) {
        const ob = elem('button', 'opt-btn', `👉 ${opt}`);
        ob.addEventListener('click', () => answerAsk(sid, a.id, true, opt, true));
        opts.append(ob);
      }
      el.append(opts);
    }
    why.placeholder = T('직접 답 입력 (선택)', 'Type a custom answer (optional)');
    const sendAns = () => {
      const val = why.value.trim() || (Array.isArray(a.options) && a.options[0]) || '';
      if (val) answerAsk(sid, a.id, true, val, true);
    };
    why.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); sendAns(); } });
    btns.append(why);
    if (!REMOTE) {
      const term = elem('button', 'mini rbtn', T('🖥️ 터미널에서', '🖥️ In terminal'));
      term.addEventListener('click', () => answerAsk(sid, a.id, null, '', true));
      btns.append(term);
    }
    const ok = elem('button', 'mini rbtn ok', T('💬 답 보내기', '💬 Send answer'));
    ok.addEventListener('click', sendAns);
    btns.append(ok);
    el.append(btns);
    return el;
  }

  why.placeholder = T('대신 할 일 (선택)', 'Do instead (optional)');
  why.title = T('거절할 때 Claude 에게 대신 시킬 일 — 거절한 뒤 지시로 따로 전합니다', 'What Claude should do instead when you deny — sent as a separate instruction after the denial');
  btns.append(why);
  if (!REMOTE) {
    const term = elem('button', 'mini rbtn', T('🖥️ 터미널에서', '🖥️ In terminal'));
    term.addEventListener('click', () => answerAsk(sid, a.id, null, '', false));
    btns.append(term);
  }
  const no = elem('button', 'mini rbtn no', T('거절', 'Deny'));
  const ok = elem('button', 'mini rbtn ok', T('승인', 'Approve'));
  no.addEventListener('click', () => answerAsk(sid, a.id, false, why.value, false));
  ok.addEventListener('click', () => answerAsk(sid, a.id, true, '', false));
  btns.append(no, ok);
  el.append(btns);
  return el;
}

async function answerAsk(sid, id, allow, why, isQuestion = false) {
  if (!REMOTE) {
    answerPetAsk(sid, id, allow, why, isQuestion);
    return;
  }
  if (!remoteKey) return;
  answered.set(id, Date.now());
  renderPanel();
  const text = (why || '').trim().slice(0, 1000);
  const ok = await postInbox({ k: allow ? 'ok' : 'no', sid, req: id, text: allow && !isQuestion ? '' : text });
  const doneMsg = allow
    ? (isQuestion && text ? T(`💬 "${text.slice(0, 22)}" 답을 보냈어요`, `💬 Answered "${text.slice(0, 22)}"`) : T('👍 승인을 보냈어요', '👍 Approval sent'))
    : T('🚫 거절을 보냈어요', '🚫 Denial sent');
  remoteNote = { sid, at: Date.now(), text: ok ? doneMsg : lastPostError };
  if (!ok) answered.delete(id);
  renderPanel();
}

async function sendSay() {
  const sid = selectedId, text = sayEl.value.trim();
  if (!sid || !text) return;
  if (text.startsWith('/')) {
    sayEl.value = '';
    wakeTerminal(sid, text);
    remoteNote = { sid, at: Date.now(), text: T(`📋 슬래시 명령(${text})은 터미널 전용입니다! 터미널로 이동됨 (Ctrl+V 후 Enter)`, `📋 Slash command (${text}) is terminal-only! Switched to terminal (Ctrl+V then Enter)`) };
    renderPanel();
    return;
  }
  if (!REMOTE) {
    sayEl.value = '';
    if (DEMO) {
      if (!remoteState) remoteState = { installed: true, away: false, s: {} };
      if (!remoteState.s) remoteState.s = {};
      const cur = remoteState.s[sid] || { listen: true };
      const s = data.sessions.find((x) => x.id === sid);
      const busy = s ? actOf(s).busy : false;
      const isQueued = busy || !cur.listen;
      cur.said = { text: text.slice(0, 1000), t: Date.now(), state: isQueued ? 'queued' : 'delivered' };
      remoteState.s[sid] = cur;
      if (s && !busy && cur.listen) s.prompt = text.slice(0, 200);
      remoteNote = {
        sid,
        at: Date.now(),
        text: busy
          ? T('⏳ 작업이 끝나는 대로 전달하도록 대기열에 넣었어요', '⏳ Queued for delivery when current work ends')
          : (!cur.listen
            ? T('⏳ 최초 1회 연결 필요 — [🖥️ 터미널로] 클릭', '⏳ First connection needed — click [🖥️ Terminal]')
            : T(`✅ "${text.slice(0, 28)}" 지시를 전했어요`, `✅ Sent "${text.slice(0, 28)}"`))
      };
      renderPanel();
      if (petOn()) renderPet();
      if (!cur.listen && !store.get('skipConnectGuide', false)) {
        openConnectGuide(sid);
      }
      return;
    }
    send({ cmd: 'pet-say', sid, text: text.slice(0, 1000) });
    playRetroSound('send');
    const r = remoteOf(sid);
    const listening = r && r.listen;
    remoteNote = {
      sid,
      at: Date.now(),
      text: listening
        ? T('🚀 지시를 보냈어요…', '🚀 Instruction sent…')
        : T('⏳ 최초 1회 연결 필요 — [🖥️ 터미널로] 클릭', '⏳ First connection needed — click [🖥️ Terminal]')
    };
    renderPanel();
    if (!listening && !store.get('skipConnectGuide', false)) {
      openConnectGuide(sid);
    }
    return;
  }
  if (!remoteKey) return;
  $('rSend').disabled = true;
  const ok = await postInbox({ k: 'say', sid, text: text.slice(0, 1000) });
  $('rSend').disabled = false;
  if (ok) sayEl.value = '';
  remoteNote = { sid, at: Date.now(), text: ok ? T('📤 보냈어요 — PC가 받는 중…', '📤 Sent — waiting for the PC…') : lastPostError };
  renderPanel();
}
$('rSend').addEventListener('click', sendSay);
sayEl.addEventListener('keydown', (e) => {
  if (e.key === 'Enter' && (e.ctrlKey || e.metaKey || (!REMOTE && !e.shiftKey))) {
    e.preventDefault();
    sendSay();
  }
});

const RECONNECTING = T('연결이 끊겨 다시 잇는 중…', 'Disconnected, reconnecting…');

/**
 * PC가 요약에 실어 보낸 화면 언어(lang)를 따른다: 지금과 다르면 이 기기에 저장하고 다시 연다(true).
 * lang 이 없는 예전 PC 프로그램이거나 주소에 ?lang= 을 직접 붙였으면 그대로. 저장이 안 되는 기기도 그대로(다시 열어도 같아서 끝없이 반복되므로).
 */
function followPcLanguage(lang) {
  if ((lang !== 'en' && lang !== 'ko') || lang === LANG || params.has('lang')) return false;
  try {
    localStorage.setItem(LANG_KEY, lang);
    if (localStorage.getItem(LANG_KEY) !== lang) return false;
  } catch {
    return false;
  }
  location.reload();
  return true;
}

async function startRemote() {
  if (!remoteLink) {
    remoteError = T('이 기기에 연결 정보가 없어요 — PC의 Claude Office ⚙️ → 📱 휴대폰 → QR 보기를 찍어 주세요',
      'No connection info on this device — scan the QR from Claude Office on your PC (⚙️ → 📱 Phone → Show QR)');
    refreshHud();
    return;
  }
  let key;
  try {
    key = await crypto.subtle.importKey('raw', b64ToBytes(remoteLink.k), 'AES-GCM', false, ['decrypt', 'encrypt']);
    remoteKey = key;
  } catch {
    remoteError = T('주소의 키가 잘못됐어요 — PC에서 QR을 다시 찍어 주세요', 'The key in the link is invalid — scan the QR on the PC again');
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
      remoteError = T('PC가 아직 아무것도 올리지 않았어요 — PC에서 ⚙️ → 📱 휴대폰 → 휴대폰 연결을 켜 주세요', 'The PC has not uploaded anything yet — turn on ⚙️ → 📱 Phone → Phone connection on the PC');
      refreshHud();
      return;
    }
    try {
      const p = remoteNode;
      const params = p.v >= 2
        ? { name: 'AES-GCM', iv: b64ToBytes(p.iv), additionalData: new TextEncoder().encode('office:' + remoteLink.u) }
        : { name: 'AES-GCM', iv: b64ToBytes(p.iv) };
      const plain = new Uint8Array(await crypto.subtle.decrypt(params, key, b64ToBytes(p.ct)));
      const obj = JSON.parse(new TextDecoder().decode(p.z ? await gunzip(plain) : plain));
      if (followPcLanguage(obj.lang)) return;
      remoteShare = obj.share || {};
      remoteState = obj.remote || null;   // 자리 비움 원격(아래 onSnapshot 이 상세 패널을 다시 그린다)
      remotePcVersion = typeof obj.ver === 'string' ? obj.ver : '';
      if (remoteEdit !== (obj.edit === true)) { remoteEdit = obj.edit === true; applyRemoteEdit(); }
      officeName = String(remoteShare.office || '').trim();
      remoteAt = p.t || Date.now();
      remoteError = '';
      onSnapshot(obj.snap);
      askAwayWhileWatching();
    } catch {
      remoteError = T('해독하지 못했어요 — PC에서 키를 새로 만들었다면 QR을 다시 찍어 주세요', "Couldn't decrypt — if you made a new key on the PC, scan the QR again");
      refreshHud();
    }
  };

  es.addEventListener('put', (e) => apply('put', e.data));
  es.addEventListener('patch', (e) => apply('patch', e.data));
  es.addEventListener('cancel', () => { remoteError = T('무료 서버가 읽기를 거절했어요 — 잠시 뒤 다시 열거나 PC에서 QR을 다시 찍어 주세요', 'The free server refused to read — reopen in a moment or scan the QR on the PC again'); refreshHud(); });
  es.addEventListener('error', () => { if (!remoteError) { remoteError = RECONNECTING; refreshHud(); } });   // EventSource 가 알아서 다시 잇는다
  es.addEventListener('open', () => { if (remoteError === RECONNECTING) { remoteError = ''; refreshHud(); } });
  setInterval(refreshHud, 5000);   // "N분 전"·끊김 표시를 갱신
}

// ───────────────────────────── 견본(데모) ─────────────────────────────

function startDemo() {
  const base = Date.now();
  const people = [
    // 견본은 가상의 회사·프로젝트(공개 스크린샷에도 쓰인다)
    { id: 'demo-a1', name: T('장바구니 개선', 'Cart fixes'), team: 'acme/web-shop', title: T('장바구니 수량 버그 수정', 'Fix the cart quantity bug') },
    { id: 'demo-a2', name: T('디자인 정리', 'Design cleanup'), team: 'acme/web-shop', title: T('버튼 색상 통일', 'Unify button colors') },
    { id: 'demo-b1', name: T('결제 연동', 'Payments'), team: 'acme/api-server', title: T('결제 응답 재시도 처리', 'Retry payment responses') },
    { id: 'demo-b2', name: T('로그 점검', 'Log review'), team: 'acme/api-server', title: T('오류 로그 분석', 'Analyze error logs') },
    { id: 'demo-b3', name: T('아주아주긴세션이름이라서잘려야하는세션', 'AVeryVeryLongSessionNameThatGetsCut'), team: 'acme/api-server' },
    { id: 'demo-c1', name: T('사무실 만들기', 'Building the office'), team: 'toy/pixel-office', title: 'Claude Pixel Office' },
    { id: 'demo-c2', name: T('어제 작업', "Yesterday's work"), team: 'toy/pixel-office' },
  ];
  const script = [
    ['thinking', '💭', T('생각 중', 'Thinking')],
    ['reading', '📄', T('CartService.cs 읽는 중', 'Reading CartService.cs')],
    ['writing', '✏️', T('PaymentApi.cs 수정 중', 'Editing PaymentApi.cs')],
    ['running', '⌨️', T('빌드하는 중', 'Building')],
    ['searching', '🔍', T("'ProcessOrder' 찾는 중", "Finding 'ProcessOrder'")],
    ['browsing', '🌐', T("'재시도 정책' 검색 중", "Searching 'retry policy'")],
    ['delegating', '👥', T('인턴에게: 결제 모듈 구조 분석', 'To intern: map the payment module')],
    ['waiting', '✋', T('승인해 주세요 · 빌드하는 중', 'Approve? · Building')],
    ['talking', '💬', T('답변 쓰는 중', 'Writing an answer')],
    ['asking', '✋', T('질문에 답해주세요!', 'Got a question!')],
    ['planning', '📋', T('할 일 정리 중', 'Sorting to-dos')],
    ['idle', '☕', T('대기 중', 'Standing by')],
    ['sleeping', '💤', 'zzz'],
  ];

  function snapshot() {
    const t = Date.now();
    const step = Math.floor((t - base) / 4000);
    return {
      now: t,
      hooks: { state: 'on', last: t - 30000 },
      sessions: people.map((p, i) => {
        const [act, icon, text] = p.id === 'demo-c2' ? ['away', '🚪', T('퇴근', 'Gone home')] : script[(step + i * 2) % script.length];
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
          prompt: T('결제 실패 시 재시도 로직을 점검하고, 실패한 요청은 로그에 남겨줘', 'Check the retry logic for failed payments and log the failed requests'),
          reply: T('장바구니 수량 계산과 결제 재시도 로직을 점검하고 단위 테스트 12개를 통과했습니다.', 'Checked the cart quantity and payment retry logic; all 12 unit tests pass.'),
          thinking: p.id === 'demo-a1' ? T('결제 실패 재시도 정책을 분석 중입니다. PaymentClient에서 네트워크 예외를 잡아서 재시도 큐에 넣고 지수 백오프(exponential backoff)를 적용할 예정입니다.',
            'Analyzing payment retry policy. Catching network exceptions in PaymentClient, placing them in retry queue, and applying exponential backoff.') : null,
          todos: p.id === 'demo-a1' ? [
            { content: T('장바구니 수량 계산 버그 수정', 'Fix cart quantity calculation bug'), status: 'completed' },
            { content: T('결제 재시도 로직 단위 테스트 작성', 'Write unit tests for retry logic'), status: 'in_progress' },
            { content: T('오류 로깅 모듈 추가', 'Add error logging module'), status: 'pending' },
          ] : p.id === 'demo-b1' ? [
            { content: T('API 엔드포인트 보안 감사 완료', 'API endpoint security audit'), status: 'completed' },
            { content: T('보안 토큰 환경변수 분리 리팩토링', 'Refactor auth token to env vars'), status: 'in_progress' },
            { content: T('CI/CD 파이프라인 시크릿 스캔 연동', 'Integrate secret scan into CI/CD'), status: 'pending' },
          ] : p.id === 'demo-c1' ? [
            { content: T('오피스 3D 가구 모델링 검토', 'Review 3D office furniture assets'), status: 'completed' },
            { content: T('칸반 보드 3열 컬럼 UI 스타일링', 'Style 3-column Kanban UI'), status: 'in_progress' },
            { content: T('모바일 터치 제스처 최적화', 'Optimize mobile touch gestures'), status: 'pending' },
          ] : null,
          secret: p.id === 'demo-b1',
          ctx: p.id === 'demo-c2' ? null : (p.id === 'demo-a1' ? 172000 : p.id === 'demo-b1' ? 188000 : 94000),
          recent: [
            { t: t - 4000, icon, text },
            { t: t - 9000, icon: '📄', text: T('Program.cs 읽는 중', 'Reading Program.cs') },
            { t: t - 15000, icon: '📨', text: T('지시: 로그 확인해줘', 'Prompt: check the logs') },
          ],
          interns: delegating ? [
            { id: p.id + '-i1', name: T('일반 인턴', 'General intern'), desc: T('결제 모듈 구조 분석', 'Map the payment module'), act: 'reading', icon: '📄',
              text: T('PaymentClient.cs 읽는 중', 'Reading PaymentClient.cs'), since: t - 3000 },
            { id: p.id + '-i2', name: T('탐색 인턴', 'Explore intern'), desc: T('호출부 찾기', 'Find the callers'), act: step % 2 ? 'idle' : 'searching', icon: step % 2 ? '✅' : '🔍',
              text: step % 2 ? T('다 했어요!', 'All done!') : T("'ProcessOrderAsync' 찾는 중", "Finding 'ProcessOrderAsync'"), since: t - 2000 },
          ] : [],
          today: p.id === 'demo-c2' ? null : {   // 견본 근무 일지(직원마다 다르게)
            work: (95 - i * 13) * 60000, prompts: 14 - i, in: 41000 + i * 3000, out: 52000 - i * 6000,
            cacheRead: 3200000 - i * 400000, cacheWrite: 180000 - i * 20000,
            tools: [{ n: 'Bash', c: 34 - i * 3 }, { n: 'Read', c: 22 - i }, { n: 'Edit', c: 12 - i }, { n: 'Grep', c: 9 }],
          },
          yesterday: {
            work: (110 - i * 12) * 60000, prompts: 18 - i, in: 52000 + i * 2000, out: 64000 - i * 5000,
            cacheRead: 3900000 - i * 350000, cacheWrite: 210000 - i * 18000,
            tools: [{ n: 'Read', c: 28 - i * 2 }, { n: 'Edit', c: 19 - i }, { n: 'Bash', c: 15 }],
          },
          week: {
            work: (420 - i * 45) * 60000, prompts: 68 - i * 5, in: 210000 + i * 10000, out: 250000 - i * 20000,
            cacheRead: 14500000 - i * 1200000, cacheWrite: 780000 - i * 60000,
            tools: [{ n: 'Bash', c: 120 - i * 10 }, { n: 'Read', c: 95 - i * 6 }, { n: 'Edit', c: 54 - i * 4 }, { n: 'Grep', c: 38 }],
          },
        };
      }),
    };
  }

  // 자리 비움 원격 견본: 한 직원은 명령 승인을 기다리고, 한 직원은 객관식 질문을 묻고, 한 직원은 지시를 받아 답했다
  remoteState = { installed: true, away: true, since: base - 600000, s: {
    'demo-b1': { listen: false, asks: [{
      id: 'demo-ask',
      kind: 'tool',
      text: T('코드 수정 승인 요청', 'Code Edit Approval'),
      detail: 'CartService.cs',
      diff: `--- a/CartService.cs\n+++ b/CartService.cs\n@@ -24,4 +24,7 @@\n-    public decimal GetTotal() => 0;\n+    public decimal GetTotal() {\n+        // 합계 및 할인율 계산 적용\n+        return Items.Sum(i => i.Price * i.Qty);\n+    }`,
      t: base - 40000
    }] },
    'demo-a2': { listen: false, asks: [{ id: 'demo-q1', kind: 'question', text: T('질문에 답해주세요!', 'Got a question!'),
      detail: T('버튼 강조 색상을 어떤 톤으로 통일할까요?', 'Which accent tone should we use for buttons?'),
      options: [T('보라색 (#7c6be0 · 기본)', 'Purple (#7c6be0 · default)'), T('민트색 (#3ccf91)', 'Mint (#3ccf91)'), T('앰버 골드 (#ffb347)', 'Amber gold (#ffb347)')],
      t: base - 25000 }] },
    'demo-a1': { listen: true, said: { text: T('장바구니 테스트도 돌려줘', 'Run the cart tests too'), t: base - 120000, state: 'delivered' },
      reply: { text: T('장바구니 수량 버그를 고치고 테스트 12개를 모두 통과했어요. 바뀐 파일은 CartService.cs 하나입니다.',
        'Fixed the cart quantity bug and all 12 tests pass. Only CartService.cs changed.'), t: base - 30000 } },
  } };

  onSnapshot(snapshot());
  setInterval(() => onSnapshot(snapshot()), 1000);
}

// ───────────────────────────── 시작 ─────────────────────────────

buildSettings();
initConnectGuide();
initWhiteboardModal();
initAchieveModal();
refreshButtons();
applyPetState();
refreshHud();
if (selectedId === 'settings') setSettingsOpen(true, params.get('tab'));   // 화면 점검용: ?select=settings&tab=phone
if (selectedId === 'pet') selectedId = null;   // ?select=pet 은 바탕화면 미니위젯 미리보기를 켠다
window.addEventListener('resize', () => { layoutKey = ''; });

if (REMOTE) {
  // 휴대폰 보기: PC 전용 조작(항상 위·훅)은 숨기고, 별명·메모는 PC에서 정한 것을 보여 주기만 한다
  document.body.classList.add('remote');
  btnPin.hidden = true;
  nickEl.readOnly = memoEl.readOnly = true;
  nickEl.placeholder = memoEl.placeholder = NOTES_ON_PC;
  startRemote();
  if (listView) applyWidget();
  // 설치형 앱(주소창 없이 열기)에 필요. 끊겼을 때 마지막 화면 파일을 보여 주는 정도만 한다(sw.js)
  if ('serviceWorker' in navigator && window.isSecureContext && !host) navigator.serviceWorker.register('sw.js').catch(() => {});
} else if (DEMO) {
  startDemo();
  if (listView) applyWidget();
} else {
  host.addEventListener('message', (e) => {
    const m = e.data;
    if (m?.type === 'cloud') onCloud(m);
    else if (m?.type === 'app') onApp(m);
    else if (m?.type === 'share') onSavedShare(m.data);
    else if (m?.type === 'remote') onRemote(m.data);
    else if (m?.type === 'select') selectSession(m.id);
    else if (m?.type === 'reset') resetScreenSettings();
    else if (m?.type === 'visible') windowHidden = !m.value;
    else if (m?.type === 'sound') playRetroSound(m.kind);
    else onSnapshot(m);
  });
  send({ cmd: 'ready' });   // 앱이 저장해 둔 별명·메모(share)를 돌려주면 합쳐서 다시 보낸다(onSavedShare)
  if (pinned) send({ cmd: 'topmost', value: true });
  send({ cmd: 'window', hours: windowHours });   // 앱은 늘 12시간으로 시작하므로 저장해 둔 값을 알려 준다
}

requestAnimationFrame(frame);
