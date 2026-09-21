# Template de skill MCP

Copier ce dossier :
```bash
cp -r mcp-servers/_template mcp-servers/mon-skill
```

Puis :
1. Éditer `server.py` (renommer `template` → `mon-skill`, ajouter vos outils).
2. Éditer `tools.json` (liste des outils exposés).
3. Ajouter au registre : `skills/registry.json` (ou laisser l'auto-discover).
4. Relancer l'orchestrateur.

Voir `docs/ADD-SKILL.md` pour le guide complet.
