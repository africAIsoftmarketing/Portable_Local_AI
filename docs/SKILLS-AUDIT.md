# Audit des 7 dépôts skills tiers — Phase Skills tiers

**Date** : 2026-09-22 · **Environnement** : `/app/.cache/skills-src/` (gitignored)
**Critères d'EXCLUSION** appliqués strictement : appel réseau externe non
désactivable · API propriétaire (OpenAI/Anthropic/Google/…) · licence
non-compatible MIT · dépendances non embarquables offline · doublon pur
d'un skill existant (sinon suffixe `-community`).

## Tableau d'analyse

| # | Dépôt (commit cloné)                                       | Format effectif                                       | Fonctionnalité                                                                 | Licence          | Dépendances                                             | **Verdict**                                                                                                                            |
|---|------------------------------------------------------------|-------------------------------------------------------|--------------------------------------------------------------------------------|------------------|---------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------|
| 1 | `mukul975/Anthropic-Cybersecurity-Skills` @`54a7988`        | Anthropic Claude **Skills** (SKILL.md = prompts guides) | Instructions pour Claude sur des tâches cybersécurité (aucun code exécutable). | Apache-2.0       | Aucune (prompts).                                       | **EXCLU** — format non-adaptable en MCP tool (le contenu est de la méthodologie destinée à Claude Code), **et** doublon fonctionnel du skill `cybersec` déjà présent. |
| 2 | `obra/superpowers` @`5bf4e78`                              | Anthropic Claude **Skills** (SKILL.md workflow guides) | Process guides Claude Code (TDD, debugging, refactoring, …).                   | MIT              | Aucune (prompts).                                       | **EXCLU** — format prompt library pur, aucun outil exécutable ; ne fournit pas de MCP tools invocables (guides pour l'IA hôte).       |
| 3 | `aloth/PowerSkills` @`a6fd0c7`                             | **PowerShell** `bootstrap.ps1` + modules              | Utilitaires PowerShell Windows (audit AD, gestion services).                   | MIT              | Runtime PowerShell / pwsh (non embarquable offline sur macOS/Linux). | **EXCLU** — dépendance runtime PowerShell incompatible avec la promesse offline multi-OS ; PowerShell Core pèse >200 Mo à embarquer par plateforme. |
| 4 | `conorluddy/AgentLoadout` @`7bc8069`                        | Node/TypeScript (`package.json`, `src/skills`)        | Config avatar/loadout pour agents IA (métadonnées TS).                         | **AUCUNE** LICENSE visible à la racine ni sous `src/`. | node.js runtime.                                        | **EXCLU** — **licence non identifiable** (critère bloquant explicite du brief) ; par ailleurs runtime Node.js non embarqué dans la master copy. |
| 5 | `EliasOenal/term-cli` @`f0d890f`                            | Python (pyproject.toml), `skills/term-cli/SKILL.md`   | Interpréteur de commandes shell orienté LLM.                                   | **"All rights reserved"** (LICENSE ligne 1) | Python 3.11+ (compatible), pyproject.                   | **EXCLU** — licence propriétaire (« All rights reserved »), **incompatible MIT** et redistribution non autorisée.                    |
| 6 | `xawt/cobold-cli` @`2618cc3` (HEAD)                        | **COBOL** (`src/*.cob`)                               | Interpréteur CLI en COBOL.                                                     | MIT              | Compilateur/runtime **GnuCOBOL** (non embarquable, deps natives libcob). | **EXCLU** — dépendance runtime COBOL non embarquable offline ; aucun runtime COBOL fourni dans les master copies.                    |
| 7 | `chaterm/terminal-skills` @`464c295` (HEAD)                | **63× SKILL.md** organisées `<catégorie>/<sujet>/SKILL.md` | Fiches offline de commandes shell par service (mongodb, docker, k8s, nginx, rsync, aws, …). | Apache-2.0       | Aucune (pure documentation markdown).                    | **✅ INTÉGRÉ** — le seul dépôt exploitable offline : contenu 100 % statique, structure exploitable en 2 MCP tools (`list_topics`, `lookup_cheatsheet`). Adapté sous **`mcp-servers/terminal-skills-community/`** avec attribution Apache-2.0 dans `UPSTREAM_LICENSE` + `tools.json`. |

## Bilan

- **6 dépôts sur 7 EXCLUS** avec motif explicite (aucun fabriqué fictivement).
- **1 dépôt RETENU** (`terminal-skills`) → skill intégré :
  `mcp-servers/terminal-skills-community/` avec 63 fiches × 2 outils MCP.
  Aucun recouvrement fonctionnel avec les 4 skills existants (`cybersec`,
  `accounting`, `rag`, `general`) : c'est un catalogue documentaire brut,
  non un moteur d'analyse.
- **Notes de conformité** :
  - `terminal-skills` distribué sous Apache-2.0, redistribué sous MIT côté
    AfricAIsoft avec préservation de l'attribution originale (fichier
    `UPSTREAM_LICENSE` copié tel quel dans le dossier du skill + mentions
    dans `tools.json` : `source.commit`, `source.original_license`,
    `source.original_author`).
  - **Aucun SHA n'était épinglé** upstream pour cobold-cli et
    terminal-skills : le commit HEAD réellement cloné est consigné
    (`464c295` pour terminal-skills, `2618cc3` pour cobold-cli exclu).
