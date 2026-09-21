# Ajouter un skill MCP

Un skill = un serveur MCP stdio (JSON-RPC 2.0) exposant un ou plusieurs outils.

## Recette rapide

1. **Copier le template** :
   ```bash
   cp -r mcp-servers/_template mcp-servers/mon-skill
   ```

2. **Éditer `mcp-servers/mon-skill/server.py`** :
   - Renommer `name="template"` → `name="mon-skill"`.
   - Ajouter des outils via le décorateur `@server.tool(...)`.
   ```python
   @server.tool(
       name="mon_outil",
       description="Fait quelque chose d'utile.",
       input_schema={
           "type": "object",
           "properties": {"param": {"type": "string"}},
           "required": ["param"],
       },
   )
   def mon_outil(args: dict) -> dict:
       # code métier ici (pur Python, offline)
       return {"resultat": args["param"].upper()}
   ```

3. **Éditer `mcp-servers/mon-skill/tools.json`** : décrire les outils (facultatif si `auto_discover=true`).

4. **Enregistrer dans `skills/registry.json`** (facultatif si `auto_discover=true`) :
   ```json
   {"name": "mon-skill", "enabled": true, "path": "mcp-servers/mon-skill"}
   ```

5. **Redémarrer** : `sudo supervisorctl restart backend` (dev) ou relancer `start-linux.sh` (portable).

6. **Vérifier** : `curl http://127.0.0.1:8080/skills` doit lister `mon-skill` avec ses outils.

7. **Tester** :
   ```bash
   curl -X POST http://127.0.0.1:8080/skills/mon-skill/invoke \
     -H "Content-Type: application/json" \
     -d '{"tool":"mon_outil","arguments":{"param":"hello"}}'
   ```

## Règles impératives

- **stdout est réservé au JSON-RPC**. Utilisez `sys.stderr` pour les logs (le framework le fait déjà).
- **Zéro dépendance native**, zéro compilation à l'installation. Restez en pure Python + stdlib.
- **Timeout par appel** = 30 s (`config/mcp.json → skill_timeout_sec`).
- **Respawn automatique** en cas de crash (3 tentatives, puis skill marqué `unavailable`).
- **JSON Schema strict** pour `input_schema` : cela permet la validation côté orchestrateur ET la génération du format OpenAI tool automatique.

## Format des noms d'outils

Les outils exposés à l'API OpenAI (`/v1/chat/completions` avec `tools:[...]`) sont automatiquement préfixés par le nom du skill pour éviter les collisions :

```
mon-skill  → mon-skill__mon_outil     (côté client OpenAI-compat)
```

L'appel `POST /skills/mon-skill/invoke` reçoit lui le nom **local** (`mon_outil`, sans préfixe).

## Utilitaires partagés

`mcp-servers/_shared/` propose :
- `mcp_server.py` : le framework MCPServer utilisé par tous les skills.
- `bm25.py` : recherche BM25 + tokenizer FR/EN.
- `text_extract.py` : extraction txt/md/pdf.

Importez-les dans votre `server.py` :
```python
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from _shared.mcp_server import MCPServer
```
