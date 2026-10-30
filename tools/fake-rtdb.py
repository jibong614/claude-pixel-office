"""
Firebase Realtime Database 흉내 — 휴대폰 보기 시험용. 진짜 Firebase 없이 "PC가 올림 → 휴대폰 화면이 받음"을 확인한다.

  PUT /offices/<id>.json                      값 저장(로그인 없이)
  PATCH /offices/<id>.json                    값 덧붙이기(앱을 끝낼 때 {"off": 시각}) — 스트림에는 event: patch
  DELETE /offices/<id>.json                   값 지우기(휴대폰 보기를 끌 때)
  GET /offices/<id>.json                      값 읽기
  GET /offices/<id>.json + Accept: text/event-stream
                                              Firebase REST 스트리밍과 같은 모양으로 계속 보냄
                                              (event: put / data: {"path":"/","data":...}, 25초마다 keep-alive)
  POST /inbox/<id>.json                       휴대폰 → PC 변경(별명·메모) 추가 → {"name": 키}
  GET /inbox/<id>.json · DELETE /inbox/<id>/<키>.json   PC가 가져가고 지움
  그 밖의 GET                                 --web 폴더의 파일(휴대폰 화면 페이지, / 는 index.html)

예) python tools/fake-rtdb.py --port 8765 --web src/ClaudeOffice/wwwroot
    그 뒤 Claude Office 설정 → 휴대폰에서 보기 → 설정하기 에
    { databaseURL: "http://127.0.0.1:8765" } 를 붙여 넣는다(127.0.0.1 이면 로그인 없이 쓰고 화면 주소도 이 서버).
"""
import argparse
import json
import mimetypes
import os
import re
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

store = {}       # id → 값
versions = {}    # id → 바뀐 횟수(스트림이 새 값을 알아채는 데 씀)
patches = {}     # id → (그 PATCH 의 버전, 덧붙인 값) — 바로 다음 이벤트는 Firebase 처럼 patch 로 보낸다
cond = threading.Condition()
WEB_ROOT = ""
inbox = {}       # id → {키: 값} — 휴대폰이 보낸 암호화 변경
inbox_seq = [0]


def office_key(path):
    if path.startswith("/offices/") and path.endswith(".json"):
        return path[len("/offices/"):-len(".json")]
    return None


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.0"   # 스트림은 연결이 끊길 때까지가 본문 — 길이를 몰라도 된다

    def log_message(self, fmt, *args):
        pass

    def _send(self, code, body=b"", ctype="application/json"):
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Cache-Control", "no-cache")
        self.end_headers()
        self.wfile.write(body)

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, PUT, POST, PATCH, DELETE, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def do_POST(self):   # 휴대폰 → PC: inbox/<id> 에 새 키로 추가(Firebase push 와 같은 응답)
        m = re.match(r"^/inbox/([^/]+)\.json$", self.path.split("?")[0])
        if not m:
            return self._send(404, b'{"error":"not found"}')
        raw = self.rfile.read(int(self.headers.get("Content-Length", "0")))
        try:
            value = json.loads(raw)
        except ValueError:
            return self._send(400, b'{"error":"Invalid data"}')
        with cond:
            inbox_seq[0] += 1
            name = f"-N{inbox_seq[0]:06d}"
            inbox.setdefault(m.group(1), {})[name] = value
        print(f"POST inbox/{m.group(1)} {len(raw)}B", flush=True)
        self._send(200, json.dumps({"name": name}).encode())

    def do_PUT(self):
        key = office_key(self.path.split("?")[0])
        if key is None:
            return self._send(404, b'{"error":"not found"}')
        raw = self.rfile.read(int(self.headers.get("Content-Length", "0")))
        try:
            value = json.loads(raw)
        except ValueError:
            return self._send(400, b'{"error":"Invalid data; couldn\'t parse JSON object."}')
        with cond:
            store[key] = value
            versions[key] = versions.get(key, 0) + 1
            cond.notify_all()
        print(f"PUT {key} {len(raw)}B", flush=True)
        self._send(200, raw)

    def do_PATCH(self):   # 앱을 끝낼 때 {"off": 시각} 만 덧붙인다 → 스트림에는 event: patch
        key = office_key(self.path.split("?")[0])
        if key is None:
            return self._send(404, b'{"error":"not found"}')
        raw = self.rfile.read(int(self.headers.get("Content-Length", "0")))
        try:
            part = json.loads(raw)
        except ValueError:
            return self._send(400, b'{"error":"Invalid data"}')
        with cond:
            store[key] = {**(store.get(key) or {}), **part}
            versions[key] = versions.get(key, 0) + 1
            patches[key] = (versions[key], part)
            cond.notify_all()
        print(f"PATCH {key} {raw.decode(errors='replace')}", flush=True)
        self._send(200, raw)

    def do_DELETE(self):   # 휴대폰 보기를 끄면 앱이 지운다 → 스트림에는 data: null 이 간다
        m = re.match(r"^/inbox/([^/]+)/([^/]+)\.json$", self.path.split("?")[0])
        if m:
            with cond:
                inbox.get(m.group(1), {}).pop(m.group(2), None)
            print(f"DELETE inbox/{m.group(1)}/{m.group(2)}", flush=True)
            return self._send(200, b"null")
        key = office_key(self.path.split("?")[0])
        if key is None:
            return self._send(404, b'{"error":"not found"}')
        with cond:
            store.pop(key, None)
            versions[key] = versions.get(key, 0) + 1
            cond.notify_all()
        print(f"DELETE {key}", flush=True)
        self._send(200, b"null")

    def do_GET(self):
        path = self.path.split("?")[0]
        m = re.match(r"^/inbox/([^/]+)\.json$", path)
        if m:
            with cond:
                value = inbox.get(m.group(1)) or None
            return self._send(200, json.dumps(value).encode())
        key = office_key(path)
        if key is not None:
            if "text/event-stream" in self.headers.get("Accept", ""):
                return self._stream(key)
            with cond:
                value = store.get(key)
            return self._send(200, json.dumps(value).encode())
        self._static(path)

    def _stream(self, key):
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.send_header("Cache-Control", "no-cache")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        seen = -1
        try:
            while True:
                with cond:
                    if versions.get(key, 0) == seen:
                        cond.wait(timeout=25)
                    ver = versions.get(key, 0)
                    value = store.get(key)
                    patch = patches.get(key)
                if ver != seen:
                    if patch and patch[0] == ver and seen == ver - 1:   # 바로 앞 값을 가진 쪽에는 덧붙인 것만
                        msg, kind = json.dumps({"path": "/", "data": patch[1]}), "patch"
                    else:
                        msg, kind = json.dumps({"path": "/", "data": value}), "put"
                    seen = ver
                    self.wfile.write(f"event: {kind}\ndata: {msg}\n\n".encode())
                else:
                    self.wfile.write(b"event: keep-alive\ndata: null\n\n")
                self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass

    def _static(self, path):
        rel = "index.html" if path in ("", "/") else path.lstrip("/")
        full = os.path.normpath(os.path.join(WEB_ROOT, rel))
        if not full.startswith(WEB_ROOT + os.sep) or not os.path.isfile(full):   # 폴더 밖으로 나가는 경로는 막는다
            return self._send(404, b"not found", "text/plain")
        with open(full, "rb") as f:
            body = f.read()
        ctype = mimetypes.guess_type(full)[0] or "application/octet-stream"
        if ctype.startswith("text/") or ctype in ("application/javascript", "image/svg+xml"):
            ctype += "; charset=utf-8"
        self._send(200, body, ctype)


def main():
    global WEB_ROOT
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--web", required=True, help="휴대폰 화면 파일 폴더(src/ClaudeOffice/wwwroot)")
    args = ap.parse_args()
    WEB_ROOT = os.path.abspath(args.web)
    mimetypes.add_type("application/javascript", ".js")
    mimetypes.add_type("application/manifest+json", ".webmanifest")
    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    server.daemon_threads = True
    print(f"fake rtdb on http://127.0.0.1:{args.port}  web={WEB_ROOT}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
