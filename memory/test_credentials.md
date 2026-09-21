# Test Credentials — AfricAIsoft Portable Studio (Phase 2)

## Runtime dev (environnement Emergent)

- **Base URL** : `http://127.0.0.1:8001` (loopback interne, supervisor)
- **Base URL externe (ingress)** : préfixe `/api` obligatoire, via l'URL de preview du pod.
- **API prefix effectif** : `/api` (positionné par `backend/server.py`).
- **Auth** : **désactivée** par défaut (`security.require_api_key = false` dans `config/settings.json`).
  - Aucun header `Authorization` requis pour la Phase 2.
  - La clé API est **quand même générée** au 1er lancement dans `config/api_key.txt`
    (32 octets urandom base64url) — présente mais non exigée.

## Activation de l'auth (optionnel, pour tester)

1. Éditer `config/settings.json` → `"security.require_api_key": true`
2. Redémarrer : `sudo supervisorctl restart backend`
3. Récupérer la clé : `cat config/api_key.txt`
4. Utiliser : `curl -H "Authorization: Bearer <clé>" http://127.0.0.1:8001/api/health`

## Endpoints principaux

| Méthode | Chemin (dev)                            | Rôle |
|---------|------------------------------------------|------|
| GET     | `/api/health`                            | État global |
| GET     | `/api/openapi.json`                      | Schéma OpenAPI |
| GET     | `/api/docs`                              | Swagger UI |
| GET     | `/api/v1/models`                         | Liste des GGUF |
| POST    | `/api/v1/chat/completions`               | Chat OpenAI-compat (stream + non-stream) |
| GET/PUT | `/api/system-prompt`                     | Lecture / écriture (403 si locked) |
| POST    | `/api/system-prompt/reset`               | Restauration défaut |
| GET     | `/api/system-prompt/presets`             | Liste des presets |
| POST    | `/api/system-prompt/activate/{id}`       | Active un preset |
| GET/PUT | `/api/config`                            | Configuration effective |

## Modèle GGUF

- **Actuel** : `models/qwen2.5-0.5b-instruct-q4_k_m.gguf` (~490 Mo)
- Téléchargé depuis HuggingFace `Qwen/Qwen2.5-0.5B-Instruct-GGUF`.

## Binaire llama-server

- **Actuel** : `bin/linux-aarch64/cpu/llama-server` (compilé depuis `b11071` en local car les binaires officiels arm64 requièrent glibc 2.38, non disponible dans ce conteneur Debian 12 / glibc 2.36).
