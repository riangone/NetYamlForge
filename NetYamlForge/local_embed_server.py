#!/usr/bin/env python3
"""
Local multilingual embedding server using sentence-transformers.
Model: paraphrase-multilingual-MiniLM-L12-v2 (384 dims, 50+ languages)

Endpoints:
  GET  /health  -> {"status":"ok","model":"...","dims":384}
  POST /embed   {"text":"..."} -> {"embedding":[...],"dims":384}
"""

import sys
import json
from http.server import HTTPServer, BaseHTTPRequestHandler

MODEL_NAME = "paraphrase-multilingual-MiniLM-L12-v2"
PORT = 5789

print(f"[EmbedServer] Loading {MODEL_NAME} ...", flush=True)
from sentence_transformers import SentenceTransformer
_model = SentenceTransformer(MODEL_NAME)
_dims = _model.get_embedding_dimension()
print(f"[EmbedServer] Ready on port {PORT}, dims={_dims}", flush=True)


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        pass  # suppress access log noise

    def _json(self, code: int, data: dict):
        body = json.dumps(data).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", len(body))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/health":
            self._json(200, {"status": "ok", "model": MODEL_NAME, "dims": _dims})
        else:
            self._json(404, {"error": "not found"})

    def do_POST(self):
        if self.path == "/embed":
            self._handle_single()
        elif self.path == "/embed_batch":
            self._handle_batch()
        else:
            self._json(404, {"error": "not found"})

    def _handle_single(self):
        try:
            length = int(self.headers.get("Content-Length", 0))
            body = json.loads(self.rfile.read(length))
            text = str(body.get("text", "")).strip()
            if not text:
                self._json(400, {"error": "text is empty"})
                return
            vec = _model.encode(text, normalize_embeddings=True).tolist()
            self._json(200, {"embedding": vec, "dims": len(vec)})
        except Exception as e:
            self._json(500, {"error": str(e)})

    def _handle_batch(self):
        try:
            length = int(self.headers.get("Content-Length", 0))
            body = json.loads(self.rfile.read(length))
            texts = body.get("texts", [])
            if not texts:
                self._json(400, {"error": "texts is empty"})
                return
            vecs = _model.encode(texts, normalize_embeddings=True).tolist()
            self._json(200, {"embeddings": vecs, "dims": len(vecs[0]) if vecs else 0})
        except Exception as e:
            self._json(500, {"error": str(e)})


if __name__ == "__main__":
    server = HTTPServer(("127.0.0.1", PORT), Handler)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("[EmbedServer] Stopped.", flush=True)
