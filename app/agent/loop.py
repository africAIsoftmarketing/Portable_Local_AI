"""
Rôle    : boucle agentique complète. Enchaîne appels llama-server ↔ MCP jusqu'à
          réponse finale (max_tool_rounds). Exécution parallèle des tool_calls
          via asyncio.gather (échec partiel toléré). Double stratégie tool
          calling : natif OpenAI + fallback JSON (```json / inline / <tool_call>).
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import asyncio
import json
import logging
import re
from typing import Any, Optional

import httpx

from app.agent.trace import BUS, new_session_id
from app.mcp.client import MCPError
from app.mcp.registry import MCPRegistry

logger = logging.getLogger("studio.agent")


class AgentLoop:
    def __init__(self, llama_client: httpx.AsyncClient, registry: MCPRegistry,
                 max_rounds: int = 5, total_timeout: float = 120.0,
                 allow_parallel: bool = True):
        self.llama = llama_client
        self.registry = registry
        self.max_rounds = max_rounds
        self.total_timeout = total_timeout
        self.allow_parallel = allow_parallel

    async def run(self, payload: dict,
                  client_tools: Optional[list[dict]] = None,
                  auto_inject_mcp: bool = True,
                  session_id: Optional[str] = None) -> dict:
        """
        Exécute une conversation agentique complète.

        Retourne un dict au format OpenAI /v1/chat/completions avec un champ
        supplémentaire `metadata.trace` contenant tous les événements.

        Paramètres :
          - client_tools : liste d'outils fournie explicitement par le client.
                           Si non vide, ce sont ces outils qui sont exposés au
                           modèle (pas d'injection MCP).
          - auto_inject_mcp : si True et client_tools est None, tous les outils
                              MCP du registre sont injectés automatiquement.
        """
        session_id = session_id or new_session_id()
        BUS.emit(session_id, "start", {"max_rounds": self.max_rounds})

        # Sélection des outils exposés au modèle selon la stratégie choisie.
        if client_tools:
            all_tools = list(client_tools)
        elif auto_inject_mcp:
            all_tools = list(self.registry.all_openai_tools())
        else:
            all_tools = []

        messages = list(payload.get("messages", []))
        tool_choice = payload.get("tool_choice", "auto")
        trace_events: list[dict] = []

        deadline = asyncio.get_event_loop().time() + self.total_timeout
        final_response: dict = {}

        for round_idx in range(self.max_rounds):
            if asyncio.get_event_loop().time() > deadline:
                BUS.emit(session_id, "timeout", {"round": round_idx})
                break

            call_payload = {k: v for k, v in payload.items()
                            if k not in ("messages", "tools", "tool_choice",
                                         "stream", "system")}
            call_payload["messages"] = messages
            call_payload["stream"] = False
            if all_tools:
                call_payload["tools"] = all_tools
                if tool_choice is not None:
                    call_payload["tool_choice"] = tool_choice

            evt = BUS.emit(session_id, "thought",
                           {"round": round_idx, "tools_offered": len(all_tools)})
            trace_events.append(evt)

            try:
                r = await self.llama.post("/v1/chat/completions", json=call_payload)
                if r.status_code != 200:
                    err = {"role": "assistant",
                           "content": f"(upstream {r.status_code}: {r.text[:400]})"}
                    final_response = _wrap(err, trace_events)
                    break
                data = r.json()
            except httpx.HTTPError as e:
                err = {"role": "assistant", "content": f"(upstream error: {e})"}
                final_response = _wrap(err, trace_events)
                break

            msg = data.get("choices", [{}])[0].get("message", {})
            tool_calls = msg.get("tool_calls") or []

            # ── Détection fallback JSON si pas de tool_calls natifs ──────────
            if not tool_calls and all_tools and msg.get("content"):
                parsed = _parse_fallback_tool_calls(msg["content"])
                if parsed:
                    tool_calls = parsed
                    msg["tool_calls"] = tool_calls
                    BUS.emit(session_id, "fallback_parse",
                             {"round": round_idx, "count": len(tool_calls)})

            if not tool_calls:
                # ── Réponse finale ────────────────────────────────────────────
                evt = BUS.emit(session_id, "final",
                               {"round": round_idx,
                                "content_preview": (msg.get("content") or "")[:200]})
                trace_events.append(evt)
                final_response = _wrap(msg, trace_events, upstream=data)
                break

            # ── Exécution des tool_calls ─────────────────────────────────────
            evt = BUS.emit(session_id, "tool_calls",
                           {"round": round_idx,
                            "calls": [{"id": tc.get("id"),
                                       "name": tc.get("function", {}).get("name")}
                                      for tc in tool_calls]})
            trace_events.append(evt)

            messages.append({"role": "assistant",
                             "content": msg.get("content") or "",
                             "tool_calls": tool_calls})

            if self.allow_parallel:
                results = await asyncio.gather(
                    *[self._exec_tool(tc, session_id) for tc in tool_calls],
                    return_exceptions=True,
                )
            else:
                results = []
                for tc in tool_calls:
                    try:
                        results.append(await self._exec_tool(tc, session_id))
                    except Exception as e:  # noqa: BLE001
                        results.append(e)

            for tc, res in zip(tool_calls, results):
                if isinstance(res, Exception):
                    payload_content = json.dumps({"error": str(res)},
                                                  ensure_ascii=False)
                else:
                    payload_content = json.dumps(res, ensure_ascii=False,
                                                 default=str)
                messages.append({
                    "role": "tool",
                    "tool_call_id": tc.get("id") or f"call_{round_idx}",
                    "name": tc.get("function", {}).get("name"),
                    "content": payload_content,
                })

            evt = BUS.emit(session_id, "tool_results",
                           {"round": round_idx,
                            "results": [{"ok": not isinstance(r, Exception),
                                         "error": str(r) if isinstance(r, Exception) else None}
                                        for r in results]})
            trace_events.append(evt)

        else:
            # Boucle épuisée sans réponse finale
            BUS.emit(session_id, "max_rounds", {"rounds": self.max_rounds})
            final_response = _wrap(
                {"role": "assistant",
                 "content": f"(max_tool_rounds={self.max_rounds} atteint sans réponse finale)"},
                trace_events)

        BUS.close(session_id)
        final_response.setdefault("metadata", {})["trace"] = trace_events
        final_response["metadata"]["session_id"] = session_id
        return final_response

    async def _exec_tool(self, tool_call: dict, session_id: str) -> Any:
        fn = tool_call.get("function", {})
        name = fn.get("name", "")
        raw_args = fn.get("arguments", "{}")
        try:
            args = json.loads(raw_args) if isinstance(raw_args, str) else (raw_args or {})
        except json.JSONDecodeError:
            return {"error": "invalid_arguments_json", "raw": raw_args}

        client, tool_local = self.registry.find_tool(name)
        if client is None:
            return {"error": "unknown_tool", "tool": name}
        try:
            return await client.call(tool_local, args)
        except MCPError as e:
            return {"error": "mcp_error", "detail": e.args[0] if e.args else str(e)}


def _wrap(final_msg: dict, trace: list[dict],
          upstream: Optional[dict] = None) -> dict:
    """Construit une réponse au format /v1/chat/completions."""
    base = upstream or {
        "id": "studio-" + trace[0]["ts"] if trace else "studio",
        "object": "chat.completion",
        "choices": [{"index": 0, "finish_reason": "stop", "message": final_msg}],
        "usage": {},
    }
    # Assure que le message est bien celui-ci (upstream peut avoir tool_calls).
    if base.get("choices"):
        base["choices"][0]["message"] = final_msg
        base["choices"][0]["finish_reason"] = "stop"
    base.setdefault("metadata", {})["trace"] = trace
    return base


# ── Parsing fallback JSON ────────────────────────────────────────────────────
_JSON_BLOCK_RE = re.compile(r"```json\s*(\{.*?\})\s*```", re.DOTALL)
_XML_TAG_RE = re.compile(r"<tool_call\s+name=['\"](\w+(?:__\w+)?)['\"]\s*>(.*?)</tool_call>",
                          re.DOTALL)


def _parse_fallback_tool_calls(text: str) -> list[dict]:
    """
    Tente d'extraire des tool_calls d'un texte libre selon 3 stratégies :
      1. ```json { "tool": "x", "arguments": {...} } ```
      2. <tool_call name="x">{...}</tool_call>
      3. JSON top-level `{"tool":..., "arguments":{...}}` via raw_decode.
    """
    calls: list[dict] = []

    # 1. bloc ```json ... ```
    for i, match in enumerate(_JSON_BLOCK_RE.finditer(text)):
        try:
            obj = json.loads(match.group(1))
            tc = _obj_to_tool_call(obj, i)
            if tc:
                calls.append(tc)
        except json.JSONDecodeError:
            continue

    # 2. balises <tool_call>
    if not calls:
        for i, m in enumerate(_XML_TAG_RE.finditer(text)):
            name = m.group(1)
            try:
                args = json.loads(m.group(2))
            except json.JSONDecodeError:
                args = {}
            calls.append({
                "id": f"fbxml_{i}",
                "type": "function",
                "function": {"name": name,
                             "arguments": json.dumps(args, ensure_ascii=False)},
            })

    # 3. Scan positions candidates pour un JSON top-level.
    if not calls:
        for pos in range(len(text)):
            if text[pos] != "{":
                continue
            dec = json.JSONDecoder()
            try:
                obj, _ = dec.raw_decode(text[pos:])
                tc = _obj_to_tool_call(obj, 0)
                if tc:
                    calls.append(tc)
                    break
            except json.JSONDecodeError:
                continue

    return calls


def _obj_to_tool_call(obj: Any, idx: int) -> Optional[dict]:
    """Normalise un objet {tool, arguments} → format OpenAI tool_call."""
    if not isinstance(obj, dict):
        return None
    name = obj.get("tool") or obj.get("name")
    args = obj.get("arguments") or obj.get("args") or {}
    if not isinstance(name, str) or not name.strip():
        return None
    return {
        "id": f"fb_{idx}",
        "type": "function",
        "function": {"name": name,
                     "arguments": json.dumps(args, ensure_ascii=False)},
    }
