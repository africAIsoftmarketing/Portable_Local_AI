# terminal-skills-community — MCP skill (offline)

**Origine** : bibliothèque `chaterm/terminal-skills` (Apache-2.0, commit `464c295`).
**Adaptation** : AfricAIsoft (MIT). Aucun réseau, aucun appel API externe.

## Contenu

63 fiches SKILL.md organisées en 12 catégories × service (mongodb, mysql,
postgresql, docker, kubernetes, nginx, ssl-tls, hardening, rsync, tar,
rsyslog, aws-cli, gcloud, azure-cli, …). Toutes les fiches sont copiées
telles quelles dans `data/<catégorie>/<sujet>.md`.

## Outils MCP exposés

### `list_topics`
Ne prend aucun argument. Retourne l'arbre `{categories: [{name, topics}], total_topics}`.

### `lookup_cheatsheet`
Renvoie le contenu markdown d'une fiche.
| Param       | Type    | Obligatoire | Description                                          |
|-------------|---------|-------------|------------------------------------------------------|
| `topic`     | string  | ✅          | Nom du sujet (ex. `mongodb`, `rsync`, `nginx`).      |
| `category`  | string  |             | Restreint la recherche à une catégorie donnée.       |
| `query`     | string  |             | Filtre les sections `##` contenant le mot-clé.       |
| `max_bytes` | integer |             | Tronque la sortie (défaut 8000 octets).              |

## Exemple d'invocation

```bash
API=$(grep REACT_APP_BACKEND_URL frontend/.env | cut -d= -f2)
curl -s -X POST "$API/api/skills/terminal-skills-community/invoke" \
  -H 'Content-Type: application/json' \
  -d '{"tool":"lookup_cheatsheet","arguments":{"topic":"rsync","max_bytes":600}}'
```

## Limites documentées

- Les fiches upstream contiennent parfois du chinois et de l'anglais mêlés
  (commentaires bilingues) — les commandes shell sont universelles.
- Le contenu est statique (copié au moment du build de la clé). Une mise à
  jour nécessite de re-fabriquer la clé USB depuis une master copy récente.
- Aucun exécutable : le skill retourne uniquement de la documentation. Pour
  exécuter réellement les commandes suggérées, l'opérateur reste maître à
  bord dans son propre shell.
