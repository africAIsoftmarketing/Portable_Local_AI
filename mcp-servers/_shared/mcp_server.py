"""
Rôle    : framework minimal pour serveurs MCP (JSON-RPC 2.0 line-delimited sur stdio).
          Un skill = une instance MCPServer + des tools enregistrés via .tool(...).
          Les logs vont sur stderr (jamais stdout, réservé au JSON-RPC).
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import json
import sys
import traceback
from typing import Any, Callable


def _log(msg: str) -> None:
    """Log vers stderr (stdout = JSON-RPC uniquement)."""
    sys.stderr.write(msg + "\n")
    sys.stderr.flush()


class MCPServer:
    def __init__(self, name: str, version: str = "0.1.0",
                 description_fr: str = "", description_en: str = ""):
        self.name = name
        self.version = version
        self.description_fr = description_fr
        self.description_en = description_en
        self.tools: dict[str, tuple[dict, Callable[[dict], Any]]] = {}

    def tool(self, name: str, description: str, input_schema: dict):
        """Décorateur pour enregistrer un outil."""
        def _deco(fn: Callable[[dict], Any]):
            self.tools[name] = ({
                "name": name,
                "description": description,
                "inputSchema": input_schema,
            }, fn)
            return fn
        return _deco

    def run(self) -> None:
        """Boucle principale : lit stdin ligne par ligne, dispatch, écrit stdout."""
        for line in sys.stdin:
            line = line.strip()
            if not line:
                continue
            try:
                msg = json.loads(line)
            except json.JSONDecodeError as e:
                self._send_error(None, -32700, f"Parse error: {e}")
                continue
            self._dispatch(msg)

    def _dispatch(self, msg: dict) -> None:
        req_id = msg.get("id")
        method = msg.get("method", "")
        params = msg.get("params", {}) or {}

        if method == "initialize":
            self._send_result(req_id, {
                "protocolVersion": "0.1",
                "capabilities": {"tools": {}},
                "serverInfo": {
                    "name": self.name,
                    "version": self.version,
                    "description": {
                        "fr": self.description_fr,
                        "en": self.description_en,
                    },
                },
            })
        elif method == "tools/list":
            self._send_result(req_id, {
                "tools": [schema for schema, _ in self.tools.values()]
            })
        elif method == "tools/call":
            tname = params.get("name")
            args = params.get("arguments", {}) or {}
            if tname not in self.tools:
                self._send_error(req_id, -32601, f"Tool not found: {tname}")
                return
            _, handler = self.tools[tname]
            try:
                result = handler(args)
            except Exception as e:  # noqa: BLE001
                _log(f"[{self.name}] tool '{tname}' error: {e}\n"
                     f"{traceback.format_exc()}")
                self._send_error(req_id, -32603, f"Tool error: {e}")
                return
            self._send_result(req_id, result if isinstance(result, dict)
                              else {"result": result})
        elif method in ("notifications/initialized", "notifications/cancelled"):
            # Notifications sans réponse.
            return
        else:
            self._send_error(req_id, -32601, f"Method not found: {method}")

    def _send_result(self, req_id: Any, result: dict) -> None:
        self._send({"jsonrpc": "2.0", "id": req_id, "result": result})

    def _send_error(self, req_id: Any, code: int, message: str) -> None:
        self._send({"jsonrpc": "2.0", "id": req_id,
                    "error": {"code": code, "message": message}})

    def _send(self, obj: dict) -> None:
        line = json.dumps(obj, ensure_ascii=False, default=str)
        sys.stdout.write(line + "\n")
        sys.stdout.flush()
