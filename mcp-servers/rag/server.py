"""
Rôle    : skill MCP rag. 2 outils :
          - search_knowledge_base : recherche BM25 dans knowledge/documents/.
          - reindex_knowledge_base : réindexation manuelle.
          Indexation auto au démarrage si absente ou plus vieille que les documents.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import json
import pickle
import sys
from pathlib import Path

_HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(_HERE.parent))
from _shared.mcp_server import MCPServer  # noqa: E402
from _shared.bm25 import BM25, tokenize  # noqa: E402
from _shared.text_extract import extract_text, split_paragraphs  # noqa: E402

server = MCPServer(
    name="rag",
    version="1.0.0",
    description_fr="Recherche BM25 locale dans une base de connaissances txt/md/pdf.",
    description_en="Local BM25 search over a txt/md/pdf knowledge base.",
)

DOCS_DIR = _HERE / "knowledge" / "documents"
INDEX_DIR = _HERE / "knowledge" / "index"
INDEX_FILE = INDEX_DIR / "bm25.pkl"


def _needs_reindex() -> bool:
    """Vrai si l'index est absent ou plus vieux qu'au moins un document."""
    if not INDEX_FILE.exists():
        return True
    idx_mtime = INDEX_FILE.stat().st_mtime
    for f in DOCS_DIR.rglob("*"):
        if f.is_file() and f.suffix.lower() in (".txt", ".md", ".pdf"):
            if f.stat().st_mtime > idx_mtime:
                return True
    return False


def _build_index() -> dict:
    """Extrait le texte, découpe en paragraphes, tokenise, construit BM25."""
    passages: list[dict] = []
    tokens_corpus: list[list[str]] = []
    if not DOCS_DIR.exists():
        DOCS_DIR.mkdir(parents=True, exist_ok=True)
    for f in sorted(DOCS_DIR.rglob("*")):
        if not f.is_file() or f.suffix.lower() not in (".txt", ".md", ".pdf"):
            continue
        text = extract_text(f)
        for i, para in enumerate(split_paragraphs(text)):
            toks = tokenize(para)
            if len(toks) < 3:
                continue
            passages.append({
                "source": str(f.relative_to(DOCS_DIR)),
                "paragraph_index": i,
                "content": para,
            })
            tokens_corpus.append(toks)

    if not tokens_corpus:
        return {"passages": [], "tokens": [], "N": 0}

    bm = BM25(tokens_corpus)
    INDEX_DIR.mkdir(parents=True, exist_ok=True)
    with INDEX_FILE.open("wb") as fh:
        pickle.dump({"passages": passages, "corpus": tokens_corpus,
                     "avgdl": bm.avgdl, "idf": bm.idf,
                     "doc_freqs": bm.doc_freqs}, fh)
    return {"passages": passages, "N": len(passages)}


def _load_index():
    """Recharge l'index depuis le pickle. Retourne (bm25, passages)."""
    if not INDEX_FILE.exists():
        return None, []
    with INDEX_FILE.open("rb") as fh:
        d = pickle.load(fh)
    if not d.get("corpus"):
        return None, []
    bm = BM25(d["corpus"])
    return bm, d["passages"]


# Indexation au premier chargement du skill (avant que MCP boot ne démarre).
if _needs_reindex():
    _build_index()
_BM25, _PASSAGES = _load_index()


@server.tool(
    name="search_knowledge_base",
    description="Recherche BM25 dans la base locale (txt/md/pdf) et retourne les passages les plus pertinents.",
    input_schema={
        "type": "object",
        "properties": {
            "query": {"type": "string"},
            "top_k": {"type": "integer", "default": 5, "minimum": 1, "maximum": 20},
        },
        "required": ["query"],
    },
)
def search_knowledge_base(args: dict) -> dict:
    global _BM25, _PASSAGES
    if _needs_reindex():
        _build_index()
        _BM25, _PASSAGES = _load_index()
    if _BM25 is None or not _PASSAGES:
        return {"results": [], "note": "Base de connaissances vide - ajouter des documents dans knowledge/documents/"}

    query = args["query"]
    top_k = int(args.get("top_k", 5))
    ranked = _BM25.top_k(tokenize(query), k=top_k)
    results = []
    for idx, score in ranked:
        p = _PASSAGES[idx]
        results.append({
            "source": p["source"],
            "paragraph_index": p["paragraph_index"],
            "content": p["content"][:600],
            "score": round(score, 4),
        })
    return {"query": query, "results": results, "index_size": len(_PASSAGES)}


@server.tool(
    name="reindex_knowledge_base",
    description="Force la reconstruction de l'index à partir de knowledge/documents/.",
    input_schema={"type": "object", "properties": {}},
)
def reindex_knowledge_base(args: dict) -> dict:
    global _BM25, _PASSAGES
    info = _build_index()
    _BM25, _PASSAGES = _load_index()
    return {"status": "ok", "passages_indexed": info.get("N", 0),
            "documents_dir": str(DOCS_DIR)}


if __name__ == "__main__":
    server.run()
