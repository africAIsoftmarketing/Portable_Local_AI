# Tests AfricAIsoft Portable Studio

## Prérequis

- Le studio doit être en cours d'exécution (via `start-linux.sh` en portable,
  ou `supervisorctl start backend` en dev Emergent).
- `curl` et `python3` doivent être disponibles.

## Variables d'environnement

| Variable | Défaut | Rôle |
|---|---|---|
| `BASE_URL` | `http://127.0.0.1:8001` | URL de base de l'orchestrateur. En dev Emergent c'est le port supervisor (8001). En portable, utiliser `http://127.0.0.1:8080`. |
| `API_PREFIX` | `/api` | Préfixe des routes. `/api` en dev Emergent, vide (`""`) en portable production. |
| `STUDIO_API_KEY` | *(vide)* | Si l'auth est activée (settings.security.require_api_key=true), fournir la clé. |
| `STUDIO_ROOT` | `/app` | Racine du dépôt (utilisée par test-system-prompt.sh pour manipuler settings.json). |

## Scripts disponibles

### `test-api.sh`

Vérifie :
- `GET /health` = 200 + structure JSON (platform, backend, components).
- `GET /openapi.json` = 200.
- `GET /v1/models` liste au moins un modèle.
- `POST /v1/chat/completions` non-stream retourne une complétion.
- `POST /v1/chat/completions` stream renvoie du SSE bien formé avec `[DONE]`.
- CORS preflight OPTIONS répond correctement.

Exécution :

```bash
BASE_URL=http://127.0.0.1:8001 API_PREFIX=/api tests/test-api.sh
```

### `test-system-prompt.sh`

Vérifie :
- Lecture initiale du system prompt (`locked=false`).
- Écriture (PUT) → relecture confirme la persistance.
- Le system prompt custom **influence bien la réponse** du modèle (marqueur d'instruction obligatoire).
- Override par champ `system` dans le payload de `/v1/chat/completions`.
- Reset au défaut fabricant.
- `locked=true` :
  - `GET` → 403,
  - `PUT` → 403,
  - override silencieusement ignoré (le marqueur n'apparaît pas dans la réponse).

Exécution :

```bash
BASE_URL=http://127.0.0.1:8001 API_PREFIX=/api STUDIO_ROOT=/app tests/test-system-prompt.sh
```

## Limites (validation humaine requise)

Ces éléments ne sont **pas** couverts par les scripts automatiques :

- Démarrage réel depuis une clé USB physique (FAT32/exFAT/NTFS).
- Détection GPU réelle (CUDA/ROCm/Vulkan/Metal) sur du hardware avec GPU.
- Boot sur Windows 10/11 et macOS Intel/ARM.
- Zero-trace forensique (dump du FS après extinction).
- Ergonomie UI et accessibilité.

Une checklist manuelle sera fournie en Phase 3 dans `tests/manual-checklist.md`.
