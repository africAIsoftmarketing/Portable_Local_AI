# Skill : rag

Recherche full-text pure Python (BM25 vendored, < 50 Ko) sur un corpus local.
**Aucun modèle d'embedding, aucune GPU, aucune dépendance native.**

## Formats supportés

- `.txt`, `.md` : lecture directe.
- `.pdf` : extraction via `pypdf` si installé, sinon fallback très basique.

## Fonctionnement

1. Au démarrage du skill, l'index (`knowledge/index/bm25.pkl`) est reconstruit si :
   - il n'existe pas ;
   - il est plus ancien que le plus récent document.
2. Sinon il est rechargé tel quel.
3. Chaque document est découpé en **paragraphes** (double newline), tokenisé (accents supprimés, minuscules, stopwords FR+EN filtrés).
4. Recherche BM25 Okapi (k1=1.5, b=0.75).

## Outils

### `search_knowledge_base`
`{query, top_k?}` → `{results: [{source, paragraph_index, content, score}]}`

### `reindex_knowledge_base`
`{}` → `{status, passages_indexed}` — force la reconstruction (à appeler après ajout de documents).

## Ajout de documents

Déposer les fichiers dans `mcp-servers/rag/knowledge/documents/` (sous-dossiers OK).
Le skill détecte la fraîcheur automatiquement au prochain appel.
