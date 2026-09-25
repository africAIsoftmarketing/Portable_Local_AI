"""
Rôle    : recherche BM25 DANS l'orchestrateur sur la base de connaissances
          (mcp-servers/rag/knowledge/documents/) et construction du bloc de
          contexte injecté dans le system prompt quand une conversation a des
          fichiers joints.
          Réutilise EXACTEMENT le code du skill RAG (_shared/bm25.py,
          _shared/text_extract.py, stdlib pure) : mêmes résultats, mais aucune
          dépendance au subprocess MCP. Les fichiers joints restent donc
          exploitables même si les skills MCP sont indisponibles, et même avec
          un petit modèle incapable d'appeler des outils.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-09-25
Version : 1.0.6
"""
from __future__ import annotations

import logging
import re
import sys
import threading
from pathlib import Path
from typing import Optional

from app.config.loader import STUDIO_ROOT

logger = logging.getLogger("studio.rag")

MCP_DIR = STUDIO_ROOT / "mcp-servers"
DOCS_DIR = MCP_DIR / "rag" / "knowledge" / "documents"
INDEXABLE = (".txt", ".md", ".pdf")

# Extensions texte acceptées à l'upload, enregistrées en « <nom>.<ext>.txt »
# pour être indexées par le skill RAG (qui ne lit que txt/md/pdf).
TEXT_LIKE = {
    ".csv", ".tsv", ".json", ".jsonl", ".log", ".xml", ".html", ".htm",
    ".yml", ".yaml", ".toml", ".ini", ".cfg", ".conf",
    ".py", ".js", ".ts", ".tsx", ".jsx", ".java", ".kt", ".c", ".h", ".cpp",
    ".hpp", ".cs", ".go", ".rs", ".php", ".rb", ".swift", ".sql", ".sh",
    ".bat", ".ps1", ".cbl", ".cob", ".cpy", ".r", ".m", ".pl", ".lua",
    ".css", ".scss", ".rst", ".tex", ".srt", ".vtt",
}
MAX_UPLOAD_BYTES = 20 * 1024 * 1024

_lock = threading.Lock()
_cache: dict = {"sig": None, "bm25": None, "passages": []}


def _shared():
    """Importe les modules _shared du skill RAG (ajout de mcp-servers au path)."""
    p = str(MCP_DIR)
    if p not in sys.path:
        sys.path.insert(0, p)
    from _shared.bm25 import BM25, tokenize  # noqa: WPS433
    from _shared.text_extract import extract_text, split_paragraphs  # noqa: WPS433
    return BM25, tokenize, extract_text, split_paragraphs


def _files() -> list[Path]:
    if not DOCS_DIR.exists():
        return []
    return sorted(f for f in DOCS_DIR.rglob("*")
                  if f.is_file() and f.suffix.lower() in INDEXABLE)


def _signature(files: list[Path]) -> tuple:
    out = []
    for f in files:
        try:
            st = f.stat()
            out.append((str(f), st.st_size, st.st_mtime))
        except OSError:
            continue
    return tuple(out)


def _ensure_index():
    """Construit (ou réutilise) l'index BM25 en mémoire. Thread-safe."""
    BM25, tokenize, extract_text, split_paragraphs = _shared()
    files = _files()
    sig = _signature(files)
    with _lock:
        if _cache["sig"] == sig:
            return _cache["bm25"], _cache["passages"], tokenize
        passages, corpus = [], []
        for f in files:
            try:
                text = extract_text(f)
            except Exception as e:  # noqa: BLE001
                logger.warning("RAG : extraction impossible %s (%s)", f.name, e)
                continue
            paras = split_paragraphs(text)
            if not paras and text.strip():
                paras = [text.strip()]  # petit document sans double saut de ligne
            for i, para in enumerate(paras):
                toks = tokenize(para)
                if len(toks) < 2:
                    continue
                passages.append({"source": str(f.relative_to(DOCS_DIR)).replace("\\", "/"),
                                 "paragraph_index": i, "content": para})
                corpus.append(toks)
        bm = BM25(corpus) if corpus else None
        _cache.update(sig=sig, bm25=bm, passages=passages)
        logger.info("RAG local : %d passage(s) indexé(s) depuis %d fichier(s).",
                    len(passages), len(files))
        return bm, passages, tokenize


def search(query: str, sources: Optional[list[str]] = None,
           top_k: int = 5) -> list[dict]:
    """Passages les plus pertinents. `sources` restreint aux fichiers joints."""
    bm, passages, tokenize = _ensure_index()
    if not passages:
        return []
    wanted = {s.replace("\\", "/") for s in sources} if sources else None
    allowed = [i for i, p in enumerate(passages)
               if wanted is None or p["source"] in wanted]
    if not allowed:
        return []
    hits: list[dict] = []
    if bm is not None:
        scores = bm.get_scores(tokenize(query))
        ranked = sorted(allowed, key=lambda i: scores[i], reverse=True)
        hits = [dict(passages[i], score=round(scores[i], 4))
                for i in ranked[:top_k] if scores[i] > 0]
    if not hits and wanted:
        # Question générique (« résume le fichier ») : début des documents joints.
        hits = [dict(passages[i], score=0.0) for i in allowed[:top_k]]
    return hits


def build_context(query: str, sources: Optional[list[str]] = None,
                  top_k: int = 5, max_chars: int = 5000) -> Optional[str]:
    """Bloc texte à ajouter au system prompt, ou None si rien de pertinent."""
    try:
        hits = search(query, sources, top_k)
    except Exception as e:  # noqa: BLE001 — le chat ne doit jamais casser
        logger.warning("RAG local indisponible : %s", e)
        return None
    if not hits:
        return None
    parts, used = [], 0
    for n, h in enumerate(hits, 1):
        chunk = h["content"].strip()
        if len(chunk) > 1200:
            chunk = chunk[:1200] + " […]"
        if used + len(chunk) > max_chars and parts:
            break
        parts.append(f"[{n}] (source : {h['source']})\n{chunk}")
        used += len(chunk)
    return ("Extraits des documents joints par l'utilisateur (base de "
            "connaissances locale) :\n\n" + "\n\n".join(parts) +
            "\n\nAppuyez-vous sur ces extraits pour répondre et citez la source "
            "entre crochets, par exemple [1]. Si l'information n'y figure pas, "
            "dites-le clairement au lieu d'inventer.")


_SAFE = re.compile(r"[^\w.\- ()\[\]]+", re.UNICODE)
_RESERVED = {"con", "prn", "aux", "nul", *(f"com{i}" for i in range(1, 10)),
             *(f"lpt{i}" for i in range(1, 10))}


def safe_filename(name: str) -> Optional[str]:
    """Nom de fichier sûr (sans chemin, caractères Windows interdits retirés)."""
    base = Path(name.replace("\\", "/")).name.strip().strip(".")
    base = _SAFE.sub("_", base)[:120].strip()
    if not base or base.split(".")[0].lower() in _RESERVED:
        return None
    return base


def storage_name(filename: str) -> tuple[Optional[str], str]:
    """(nom de stockage, type) ou (None, raison) si l'extension est refusée."""
    safe = safe_filename(filename)
    if not safe:
        return None, "nom de fichier invalide"
    ext = Path(safe).suffix.lower()
    if ext in INDEXABLE:
        return safe, ext.lstrip(".")
    if ext in TEXT_LIKE or not ext:
        return safe + ".txt", "text"
    return None, (f"type {ext} non pris en charge (formats acceptés : .txt, .md, "
                  f".pdf et fichiers texte/code : .csv, .json, .py, .cbl…)")
