"""
Rôle    : skill MCP general. 2 outils :
          - generate_structured_report : titre + sections → Markdown structuré.
          - extract_key_info : regex FR+EN pour emails/dates/montants/IBAN/SIRET/etc.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

_HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(_HERE.parent))
from _shared.mcp_server import MCPServer  # noqa: E402

server = MCPServer(
    name="general",
    version="1.0.0",
    description_fr="Utilitaires généralistes : rapports structurés et extraction d'entités.",
    description_en="General utilities: structured reports and entity extraction.",
)


@server.tool(
    name="generate_structured_report",
    description="Génère un rapport Markdown structuré à partir d'un titre et de sections (avec listes/tableaux).",
    input_schema={
        "type": "object",
        "properties": {
            "title": {"type": "string"},
            "author": {"type": "string"},
            "date": {"type": "string"},
            "sections": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "heading": {"type": "string"},
                        "content": {"type": "string"},
                        "bullets": {"type": "array", "items": {"type": "string"}},
                        "table": {"type": "array"},
                    },
                    "required": ["heading"],
                },
            },
        },
        "required": ["title", "sections"],
    },
)
def generate_structured_report(args: dict) -> dict:
    lines: list[str] = [f"# {args['title']}", ""]
    if args.get("author") or args.get("date"):
        meta = []
        if args.get("author"):
            meta.append(f"**Auteur** : {args['author']}")
        if args.get("date"):
            meta.append(f"**Date** : {args['date']}")
        lines.append(" — ".join(meta))
        lines.append("")

    for sec in args.get("sections", []):
        lines.append(f"## {sec['heading']}")
        lines.append("")
        if sec.get("content"):
            lines.append(sec["content"].strip())
            lines.append("")
        if sec.get("bullets"):
            for b in sec["bullets"]:
                lines.append(f"- {b}")
            lines.append("")
        table = sec.get("table")
        if isinstance(table, list) and table and isinstance(table[0], list):
            headers = table[0]
            lines.append("| " + " | ".join(str(h) for h in headers) + " |")
            lines.append("| " + " | ".join("---" for _ in headers) + " |")
            for row in table[1:]:
                lines.append("| " + " | ".join(str(c) for c in row) + " |")
            lines.append("")

    markdown = "\n".join(lines).rstrip() + "\n"
    return {"markdown": markdown, "line_count": len(lines),
            "sections_count": len(args.get("sections", []))}


# ── extract_key_info : regex FR + EN ─────────────────────────────────────────
_PATTERNS = {
    "emails": re.compile(r"\b[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}\b"),
    "urls": re.compile(r"https?://[^\s<>\"')]+", re.IGNORECASE),
    "ipv4": re.compile(r"\b(?:\d{1,3}\.){3}\d{1,3}\b"),
    "phones_fr": re.compile(r"\b(?:\+33\s?|0)[1-9](?:[\s.-]?\d{2}){4}\b"),
    "phones_intl": re.compile(r"\+\d{1,3}[\s.-]?\d{4,14}"),
    "ibans": re.compile(r"\b[A-Z]{2}\d{2}[A-Z0-9]{4,30}\b"),
    "sirets": re.compile(r"\b\d{14}\b"),
    "amounts": re.compile(
        r"(?:€\s?)?(\d{1,3}(?:[ .,]\d{3})*(?:[.,]\d{2})?)\s?(?:€|EUR|USD|\$|GBP|£)",
        re.IGNORECASE),
    "dates_iso": re.compile(r"\b\d{4}-\d{2}-\d{2}\b"),
    "dates_fr": re.compile(r"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b"),
}


@server.tool(
    name="extract_key_info",
    description="Extrait entités (emails, URLs, IPs, téléphones FR/intl, IBAN, SIRET, montants, dates) d'un texte français ou anglais.",
    input_schema={
        "type": "object",
        "properties": {"text": {"type": "string"}},
        "required": ["text"],
    },
)
def extract_key_info(args: dict) -> dict:
    text = args["text"]
    out: dict[str, list[str]] = {}
    for name, pat in _PATTERNS.items():
        seen: set[str] = set()
        matches: list[str] = []
        for m in pat.finditer(text):
            val = m.group(0)
            if val not in seen:
                seen.add(val)
                matches.append(val)
        out[name] = matches
    total = sum(len(v) for v in out.values())
    return {"entities": out, "counts": {k: len(v) for k, v in out.items()},
            "total_entities": total}


if __name__ == "__main__":
    server.run()
