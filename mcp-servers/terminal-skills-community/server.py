"""
Rôle    : skill MCP terminal-skills-community — expose la bibliothèque
          terminal-skills (63 fiches SKILL.md organisées par catégorie ×
          service) sous forme de 2 outils MCP OFFLINE :
            - list_topics       : arbre catégorie → sujet.
            - lookup_cheatsheet : recherche full-text dans les fiches
                                  (extrait le SKILL.md complet ou une
                                   section ciblée par mots-clés).
          Aucun réseau, aucune API externe. Chemins relatifs à server.py
          (Path(__file__).parent) → fonctionne depuis clé USB.
Source  : https://github.com/chaterm/terminal-skills @464c295 (HEAD cloné,
          aucun tag épinclé fourni par l'upstream — commit consigné dans
          tools.json).
Licence upstream : Apache 2.0 (voir UPSTREAM_LICENSE) — redistribuée sous
          MIT côté AfricAIsoft (compat via NOTICE + attribution).
Auteur adaptateur : AfricAIsoft — Licence adaptation : MIT
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

_HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(_HERE.parent))
from _shared.mcp_server import MCPServer  # noqa: E402

DATA_ROOT = _HERE / "data"

server = MCPServer(
    name="terminal-skills-community",
    version="1.0.0",
    description_fr=("Bibliothèque offline de fiches terminal (commandes) — "
                    "63 sujets couvrant bases de données, DevOps, réseau, "
                    "docker, kubernetes, sécurité, backup, cloud CLIs."),
    description_en=("Offline terminal cheatsheet library — 63 topics covering "
                    "databases, DevOps, networking, docker, kubernetes, "
                    "security, backup, cloud CLIs."),
)


def _load_index() -> dict[str, dict[str, Path]]:
    """Retourne {category: {topic: path/to/topic.md}}."""
    tree: dict[str, dict[str, Path]] = {}
    if not DATA_ROOT.is_dir():
        return tree
    for cat_dir in sorted(p for p in DATA_ROOT.iterdir() if p.is_dir()):
        topics: dict[str, Path] = {}
        for md in sorted(cat_dir.glob("*.md")):
            topics[md.stem] = md
        if topics:
            tree[cat_dir.name] = topics
    return tree


_INDEX_CACHE: dict[str, dict[str, Path]] | None = None


def _index() -> dict[str, dict[str, Path]]:
    global _INDEX_CACHE
    if _INDEX_CACHE is None:
        _INDEX_CACHE = _load_index()
    return _INDEX_CACHE


# ── Outil 1 : list_topics ────────────────────────────────────────────────────
@server.tool(
    name="list_topics",
    description=("Liste toutes les catégories et sujets disponibles. "
                 "Retourne {categories: [{name, topics: [...]}], total_topics}."),
    input_schema={"type": "object", "properties": {}, "additionalProperties": False},
)
def list_topics(_args: dict) -> dict:
    idx = _index()
    cats = [{"name": cat, "topics": sorted(topics.keys())}
            for cat, topics in idx.items()]
    total = sum(len(t) for _, t in idx.items())
    return {"categories": cats, "total_topics": total}


# ── Outil 2 : lookup_cheatsheet ──────────────────────────────────────────────
@server.tool(
    name="lookup_cheatsheet",
    description=("Recherche une fiche par sujet (nom exact ou fuzzy) et "
                 "renvoie son contenu markdown. Options : `category` "
                 "restreint à une catégorie, `query` filtre les sections "
                 "contenant le mot-clé, `max_bytes` tronque la réponse."),
    input_schema={
        "type": "object",
        "properties": {
            "topic": {"type": "string",
                      "description": "Sujet (ex. 'mongodb', 'rsync', 'nginx')."},
            "category": {"type": "string",
                         "description": "Optionnel : restreint à une catégorie."},
            "query": {"type": "string",
                      "description": "Optionnel : filtre les sections contenant "
                                     "ce mot-clé (case-insensitive)."},
            "max_bytes": {"type": "integer", "default": 8000,
                          "description": "Tronque la sortie à N octets."},
        },
        "required": ["topic"],
        "additionalProperties": False,
    },
)
def lookup_cheatsheet(args: dict) -> dict:
    topic = str(args.get("topic", "")).strip().lower()
    if not topic:
        return {"found": False, "error": "Paramètre 'topic' requis."}
    category = args.get("category")
    query = args.get("query")
    max_bytes = int(args.get("max_bytes") or 8000)

    idx = _index()
    # Résolution catégorie/sujet — nom exact d'abord, puis fuzzy.
    hits: list[tuple[str, str, Path]] = []
    for cat, topics in idx.items():
        if category and cat != category:
            continue
        for tname, path in topics.items():
            if tname == topic:
                hits.append((cat, tname, path))
    if not hits:
        for cat, topics in idx.items():
            if category and cat != category:
                continue
            for tname, path in topics.items():
                if topic in tname or tname in topic:
                    hits.append((cat, tname, path))
    if not hits:
        return {"found": False, "topic": topic,
                "hint": "Utilisez list_topics pour lister les sujets disponibles."}

    # Prend la première correspondance.
    cat, tname, path = hits[0]
    content = path.read_text(encoding="utf-8", errors="replace")

    if query:
        # Extrait les sections markdown (## …) contenant le mot-clé.
        q = query.lower()
        sections = re.split(r"(?m)^(?=##\s)", content)
        matched = [s for s in sections if q in s.lower()]
        if matched:
            content = "\n\n".join(matched)

    truncated = False
    encoded = content.encode("utf-8")
    if len(encoded) > max_bytes:
        content = encoded[:max_bytes].decode("utf-8", errors="ignore") + "\n\n[…tronqué…]"
        truncated = True

    return {
        "found": True,
        "topic": tname,
        "category": cat,
        "source_path": str(path.relative_to(_HERE)),
        "content": content,
        "truncated": truncated,
        "other_matches": [{"category": c, "topic": t}
                          for c, t, _ in hits[1:6]],
    }


if __name__ == "__main__":
    server.run()
