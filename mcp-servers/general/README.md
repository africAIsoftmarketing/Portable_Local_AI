# Skill : general

Utilitaires transverses réutilisables par tous les autres skills et par l'agent.

## Outils

### `generate_structured_report`
`{title, author?, date?, sections: [{heading, content?, bullets?, table?}, ...]}` → Markdown.

### `extract_key_info`
`{text}` → dict d'entités : emails, urls, ipv4, phones_fr, phones_intl, ibans, sirets, amounts, dates_iso, dates_fr.

Toutes les regex sont pure-Python stdlib, déterministes, offline.
