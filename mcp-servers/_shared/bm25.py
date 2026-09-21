"""
Rôle    : BM25 pur Python vendored (≤ 50 Ko). Aucun modèle, aucune dépendance
          binaire. Tokenisation FR+EN simple avec normalisation des accents.
Auteur  : AfricAIsoft
Licence : MIT (implémentation originale, algorithme BM25 = Robertson et al.)
Date    : 2026-08-24
"""
from __future__ import annotations

import math
import re
import unicodedata
from collections import Counter

# Stopwords FR + EN embarqués (liste courte, suffisante pour BM25).
_STOPWORDS: set[str] = {
    # Français
    "le", "la", "les", "un", "une", "des", "de", "du", "au", "aux",
    "et", "ou", "mais", "donc", "or", "ni", "car", "que", "qui", "quoi",
    "ce", "cet", "cette", "ces", "il", "elle", "ils", "elles", "on", "nous", "vous",
    "je", "tu", "me", "te", "se", "en", "y", "à", "pour", "par", "sur",
    "dans", "avec", "sans", "sous", "chez", "vers", "entre", "avant", "après",
    "est", "sont", "sera", "était", "été", "être", "avoir", "eu", "a", "as", "ont",
    "pas", "ne", "plus", "moins", "très", "trop", "aussi", "si",
    # Anglais
    "the", "a", "an", "and", "or", "but", "so", "of", "to", "in", "on", "at",
    "with", "without", "for", "by", "from", "up", "down", "over", "under",
    "is", "are", "was", "were", "be", "been", "being", "have", "has", "had",
    "do", "does", "did", "not", "no", "yes", "this", "that", "these", "those",
    "i", "you", "he", "she", "it", "we", "they", "me", "him", "her", "us", "them",
    "as", "if", "than", "then", "very",
}


def normalize(text: str) -> str:
    """Minuscule + suppression des accents (NFD)."""
    nfkd = unicodedata.normalize("NFKD", text.lower())
    return "".join(c for c in nfkd if not unicodedata.combining(c))


_TOKEN_RE = re.compile(r"[a-z0-9]+")


def tokenize(text: str, remove_stopwords: bool = True) -> list[str]:
    """Tokenisation simple : accents supprimés, mots alphanumériques, min 2 chars."""
    tokens = _TOKEN_RE.findall(normalize(text))
    tokens = [t for t in tokens if len(t) >= 2]
    if remove_stopwords:
        tokens = [t for t in tokens if t not in _STOPWORDS]
    return tokens


class BM25:
    """
    BM25 Okapi. Reçoit une liste de documents pré-tokenisés.
    Paramètres par défaut : k1=1.5, b=0.75 (standard).
    """

    def __init__(self, corpus_tokens: list[list[str]],
                 k1: float = 1.5, b: float = 0.75):
        self.k1 = k1
        self.b = b
        self.corpus = corpus_tokens
        self.N = len(corpus_tokens)
        total_len = sum(len(d) for d in corpus_tokens)
        self.avgdl = total_len / max(1, self.N)

        self.doc_freqs: list[Counter] = []
        df: dict[str, int] = {}
        for doc in corpus_tokens:
            freqs = Counter(doc)
            self.doc_freqs.append(freqs)
            for term in freqs:
                df[term] = df.get(term, 0) + 1

        self.idf: dict[str, float] = {}
        for term, f in df.items():
            # IDF Okapi (variante avec +1 pour éviter idf négatif sur termes très fréquents)
            self.idf[term] = math.log((self.N - f + 0.5) / (f + 0.5) + 1.0)

    def get_scores(self, query_tokens: list[str]) -> list[float]:
        scores = [0.0] * self.N
        for term in query_tokens:
            idf = self.idf.get(term)
            if idf is None:
                continue
            for i, freqs in enumerate(self.doc_freqs):
                tf = freqs.get(term)
                if not tf:
                    continue
                dl = len(self.corpus[i])
                denom = tf + self.k1 * (1.0 - self.b + self.b * dl / self.avgdl)
                scores[i] += idf * (tf * (self.k1 + 1)) / denom
        return scores

    def top_k(self, query_tokens: list[str], k: int = 5) -> list[tuple[int, float]]:
        """Retourne [(doc_index, score), ...] triés par score décroissant."""
        scores = self.get_scores(query_tokens)
        ranked = sorted(enumerate(scores), key=lambda x: x[1], reverse=True)
        return [(i, s) for i, s in ranked[:k] if s > 0]
