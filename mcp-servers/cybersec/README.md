# Skill : cybersec

Analyse de logs de sécurité + vérification de réputation IP, 100% offline.

## Outils exposés

### `analyze_security_logs`

Détecte automatiquement dans un log auth.log/syslog :
- **Brute-force SSH** : ≥ N `Failed password` depuis la même IP source (seuil configurable).
- **Connexions root acceptées** : `Accepted password for root/admin`.
- **Sudo suspicieux** : commandes potentiellement dangereuses (`rm -rf`, `wget`, `nc -`, etc.).

**Arguments** : `{logs: "contenu brut OU chemin fichier", brute_force_threshold?: 5, max_lines?: 5000}`

**Sortie** : `{summary, anomalies: [{type, source_ip, severity, recommendation, ...}]}`

### `check_ip_reputation`

Match une IP (v4 ou v6) contre les blocklists locales stockées sous `lists/*.txt` (une IP ou un CIDR par ligne). Support IP exacte + plages CIDR via `ipaddress` stdlib.

**Arguments** : `{ip: "1.2.3.4"}`
**Sortie** : `{verdict: clean|suspicious|malicious, sources, matches}`

## Enrichissement des listes

Ajouter un fichier `lists/<source>.txt` avec une IP/CIDR par ligne (les `#` sont ignorés). Le skill recharge les listes au prochain lancement de l'orchestrateur.
