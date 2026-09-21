"""
Rôle    : extraction texte depuis txt/md/pdf. Aucun embedding, aucun ML.
          PDF via pypdf si disponible, sinon message d'erreur clair.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

from pathlib import Path


def extract_text(path: Path) -> str:
    """Retourne le texte brut d'un fichier .txt/.md/.pdf."""
    suffix = path.suffix.lower()
    if suffix in (".txt", ".md"):
        return path.read_text(encoding="utf-8", errors="ignore")
    if suffix == ".pdf":
        return _extract_pdf(path)
    return ""


def _extract_pdf(path: Path) -> str:
    try:
        import pypdf  # optionnel
    except ImportError:
        # Fallback pdf-minimal : extraction naive via texte imprimable.
        try:
            raw = path.read_bytes()
            # heuristique très basique : cherche les BT..ET blocks
            import re
            texts = re.findall(rb"\(([^)]+)\)\s*Tj", raw)
            return "\n".join(t.decode("latin-1", errors="ignore") for t in texts)
        except Exception:  # noqa: BLE001
            return f"[PDF non extractible sans pypdf : {path.name}]"

    try:
        reader = pypdf.PdfReader(str(path))
        chunks = []
        for page in reader.pages:
            chunks.append(page.extract_text() or "")
        return "\n".join(chunks)
    except Exception as e:  # noqa: BLE001
        return f"[erreur lecture PDF {path.name}: {e}]"


def split_paragraphs(text: str, min_len: int = 40) -> list[str]:
    """Découpe en paragraphes (double newline), filtre les trop courts."""
    paragraphs = [p.strip() for p in text.split("\n\n")]
    return [p for p in paragraphs if len(p) >= min_len]
