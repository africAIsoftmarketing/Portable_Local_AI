# Exemples de scénarios agentiques — AfricAIsoft Portable Studio

> Trois scénarios réels illustrant la boucle agentique multi-outils MCP.
> Chaque exemple montre : le prompt utilisateur, la requête HTTP à
> `/v1/chat/completions`, la trace SSE reçue, la réponse finale du modèle.
>
> Les payloads sont ceux réellement acceptés par l'orchestrateur (schémas
> validés Pydantic + JSON Schema des outils). Les sorties `tool_result`
> sont issues des invocations directes des skills (voir §Reproduire).

---

## 1. Scénario cybersécurité — analyse de logs SSH

**Cas d'usage** : un analyste SOC copie/colle un extrait de `/var/log/auth.log`
et demande une synthèse.

### 1.1 Prompt

> « Analyse ces logs et donne-moi la liste des menaces avec recommandations. »

Avec le preset `cybersec` actif (via `POST /api/system-prompt/activate/cybersec`),
le system prompt oriente le modèle vers un rapport structuré.

### 1.2 Requête HTTP

```bash
curl -sX POST http://127.0.0.1:8080/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "messages": [
      {"role":"user","content":"Analyse ces logs et donne les menaces + recommandations :\n\nAug 21 10:15:03 srv sshd[1101]: Failed password for root from 185.220.101.5 port 44022 ssh2\nAug 21 10:15:05 srv sshd[1101]: Failed password for root from 185.220.101.5 port 44022 ssh2\nAug 21 10:15:07 srv sshd[1102]: Failed password for admin from 185.220.101.5 port 44024 ssh2\nAug 21 10:15:09 srv sshd[1103]: Failed password for oracle from 185.220.101.5 port 44026 ssh2\nAug 21 10:15:11 srv sshd[1104]: Failed password for postgres from 185.220.101.5 port 44028 ssh2\nAug 21 10:15:13 srv sshd[1105]: Failed password for git from 185.220.101.5 port 44030 ssh2\nAug 21 10:17:22 srv sshd[1150]: Accepted password for root from 10.0.0.42 port 55001 ssh2"}
    ],
    "tool_choice": "auto",
    "max_tokens": 800,
    "temperature": 0.4
  }'
```

Aucun `tools:[...]` fourni → l'orchestrateur injecte automatiquement les
8 outils MCP disponibles.

### 1.3 Invocation directe (résultat déterministe du skill)

Pour rendre l'exemple reproductible même sans un modèle 7B+, voici le
résultat exact de l'outil `cybersec__analyze_security_logs` appelé
directement (POST `/api/skills/cybersec/invoke`) sur ces mêmes logs :

```json
{
  "skill": "cybersec",
  "tool": "analyze_security_logs",
  "result": {
    "summary": {
      "lines_analyzed": 7,
      "unique_source_ips_failing": 1,
      "total_ssh_failures": 5,
      "sudo_commands_count": 0,
      "anomalies_count": 2
    },
    "anomalies": [
      {
        "type": "ssh_brute_force",
        "source_ip": "185.220.101.5",
        "failed_attempts": 5,
        "targeted_users": ["admin", "git", "oracle", "postgres", "root"],
        "severity": "medium",
        "recommendation": "Bloquer 185.220.101.5 au pare-feu (fail2ban, iptables) et vérifier les comptes ciblés."
      },
      {
        "type": "root_login_accepted",
        "source_ip": "10.0.0.42",
        "user": "root",
        "severity": "high",
        "recommendation": "Interdire la connexion root directe (PermitRootLogin no dans sshd_config)."
      }
    ]
  }
}
```

### 1.4 Trace SSE agentique (extrait de `metadata.trace`)

```json
[
  {"ts":"2026-08-24T10:20:12Z","type":"start","data":{"max_rounds":5}},
  {"ts":"2026-08-24T10:20:12Z","type":"thought","data":{"round":0,"tools_offered":8}},
  {"ts":"2026-08-24T10:20:14Z","type":"tool_calls","data":{"round":0,"calls":[
     {"id":"call_0","name":"cybersec__analyze_security_logs"}
  ]}},
  {"ts":"2026-08-24T10:20:14Z","type":"tool_results","data":{"round":0,"results":[
     {"ok":true,"error":null}
  ]}},
  {"ts":"2026-08-24T10:20:16Z","type":"final","data":{"round":1,
     "content_preview":"Deux menaces identifiées : (1) brute-force SSH depuis 185.220.101.5..."}}
]
```

---

## 2. Scénario comptable — vérification + ratios chaînés

**Cas d'usage** : un expert-comptable soumet des écritures et demande une
analyse chiffrée.

### 2.1 Prompt

> « Vérifie l'équilibre de ces écritures, puis calcule les ratios de liquidité
> et de solvabilité, et fais-moi un rapport structuré. »

### 2.2 Résultat outil `accounting__verify_accounting_entries`

Appel direct (`POST /api/skills/accounting/invoke`) avec un journal
déséquilibré :

```json
{
  "tool": "verify_accounting_entries",
  "arguments": {
    "entries": [
      {"account":"411000","debit":1200,"credit":0,"journal":"VE"},
      {"account":"701000","debit":0,"credit":1000,"journal":"VE"},
      {"account":"445710","debit":0,"credit":200,"journal":"VE"},
      {"account":"512000","debit":1200,"credit":0,"journal":"BQ"},
      {"account":"411000","debit":0,"credit":1158,"journal":"BQ"}
    ]
  }
}
```

Résultat renvoyé :

```json
{
  "balanced": false,
  "totals": {"debit": 2400.0, "credit": 2358.0, "imbalance": 42.0},
  "per_journal_imbalances": [
    {"journal":"BQ","debit":1200.0,"credit":1158.0,"imbalance":42.0}
  ],
  "issues": [],
  "issues_count": 0,
  "recommendation": "Déséquilibre global de +42.00. Vérifier les journaux listés."
}
```

### 2.3 Résultat outil `accounting__calculate_financial_ratios`

```json
{
  "arguments": {
    "balance_sheet": {"current_assets":150000,"current_liabilities":80000,
                      "inventory":30000,"total_assets":500000,
                      "total_liabilities":200000,"total_equity":300000},
    "income_statement": {"revenue":400000,"net_income":45000,
                         "operating_income":60000}
  }
}
```

Résultat :

```json
{
  "ratios": {
    "current_ratio":   {"value":"1.875","formula":"actif_circulant / passif_courant",
                        "interpretation":"≥ 1.5 : bonne liquidité court terme."},
    "quick_ratio":     {"value":"1.500","formula":"(actif_circulant - stocks) / passif_courant",
                        "interpretation":"≥ 1.0 : capacité à honorer les dettes court terme sans écouler les stocks."},
    "debt_to_equity":  {"value":"0.667","formula":"dettes_totales / capitaux_propres",
                        "interpretation":"≤ 1.0 : structure financière prudente."},
    "return_on_assets":{"value":"0.090","formula":"résultat_net / actif_total",
                        "interpretation":"ROA."},
    "return_on_equity":{"value":"0.150","formula":"résultat_net / capitaux_propres",
                        "interpretation":"ROE."},
    "operating_margin":{"value":"0.150","formula":"résultat_exploitation / chiffre_affaires",
                        "interpretation":"Marge opérationnelle."},
    "net_margin":      {"value":"0.113","formula":"résultat_net / chiffre_affaires",
                        "interpretation":"Marge nette."}
  }
}
```

### 2.4 Trace SSE (chaînage 2 outils sur 2 rounds)

```json
[
  {"type":"start","data":{"max_rounds":5}},
  {"type":"thought","data":{"round":0,"tools_offered":8}},
  {"type":"tool_calls","data":{"round":0,"calls":[
     {"name":"accounting__verify_accounting_entries"}]}},
  {"type":"tool_results","data":{"round":0,"results":[{"ok":true}]}},
  {"type":"thought","data":{"round":1,"tools_offered":8}},
  {"type":"tool_calls","data":{"round":1,"calls":[
     {"name":"accounting__calculate_financial_ratios"}]}},
  {"type":"tool_results","data":{"round":1,"results":[{"ok":true}]}},
  {"type":"final","data":{"round":2,
     "content_preview":"Journal BQ déséquilibré de +42.00€. Ratios : liquidité correcte (1.87), levier prudent (0.67)..."}}
]
```

---

## 3. Scénario multi-outils — RAG + extraction + rapport

**Cas d'usage** : un utilisateur pose une question qui nécessite (1) chercher
dans la base documentaire, (2) extraire des entités du résultat, (3) produire
un rapport Markdown final.

### 3.1 Prompt

> « Cherche dans la KB tout ce qui concerne les politiques de sécurité IP,
> extrais les adresses IP mentionnées, puis produis un rapport structuré. »

### 3.2 Outil `rag__search_knowledge_base` (invocation directe)

```json
{
  "tool":"search_knowledge_base",
  "arguments":{"query":"politique securite adresses IP","top_k":3}
}
```

Résultat (base vide au premier lancement — nominal) :

```json
{
  "query":"politique securite adresses IP",
  "results":[],
  "index_size":0,
  "note":"Base de connaissances vide - ajouter des documents dans knowledge/documents/"
}
```

Avec un document `mcp-servers/rag/knowledge/documents/politique.md` contenant
« Les IPs 10.0.0.42 et 185.220.101.5 doivent être surveillées », le résultat
devient :

```json
{
  "query":"politique securite adresses IP",
  "results":[
    {"source":"politique.md","paragraph_index":0,"score":2.1743,
     "content":"Les IPs 10.0.0.42 et 185.220.101.5 doivent être surveillées."}
  ],
  "index_size":1
}
```

### 3.3 Outil `general__extract_key_info`

Appel avec le passage retourné par le RAG :

```json
{
  "tool":"extract_key_info",
  "arguments":{"text":"Les IPs 10.0.0.42 et 185.220.101.5 doivent être surveillées."}
}
```

Résultat :

```json
{
  "entities": {
    "emails": [],
    "urls": [],
    "ipv4": ["10.0.0.42", "185.220.101.5"],
    "phones_fr": [],
    "phones_intl": [],
    "ibans": [],
    "sirets": [],
    "amounts": [],
    "dates_iso": [],
    "dates_fr": []
  },
  "counts": {"ipv4": 2, "emails": 0, "urls": 0, "phones_fr": 0,
             "phones_intl": 0, "ibans": 0, "sirets": 0, "amounts": 0,
             "dates_iso": 0, "dates_fr": 0},
  "total_entities": 2
}
```

### 3.4 Outil `general__generate_structured_report`

```json
{
  "tool":"generate_structured_report",
  "arguments":{
    "title":"Rapport IP surveillance",
    "author":"AfricAIsoft Studio",
    "date":"2026-08-24",
    "sections":[
      {"heading":"Contexte",
       "content":"Extrait du document politique.md indexé localement."},
      {"heading":"IPs identifiées",
       "bullets":["10.0.0.42","185.220.101.5"]},
      {"heading":"Recommandations",
       "bullets":["Corrélation avec les logs SSH récents",
                  "Ajout au blocklist local (mcp-servers/cybersec/lists/)"]}
    ]
  }
}
```

Résultat (extrait) :

```json
{
  "markdown": "# Rapport IP surveillance\n\n**Auteur** : AfricAIsoft Studio — **Date** : 2026-08-24\n\n## Contexte\n\nExtrait du document politique.md indexé localement.\n\n## IPs identifiées\n\n- 10.0.0.42\n- 185.220.101.5\n\n## Recommandations\n\n- Corrélation avec les logs SSH récents\n- Ajout au blocklist local (mcp-servers/cybersec/lists/)\n",
  "line_count": 15,
  "sections_count": 3
}
```

### 3.5 Trace SSE (3 rounds, 3 outils différents)

```json
[
  {"type":"start","data":{"max_rounds":5}},
  {"type":"tool_calls","data":{"round":0,"calls":[{"name":"rag__search_knowledge_base"}]}},
  {"type":"tool_results","data":{"round":0,"results":[{"ok":true}]}},
  {"type":"tool_calls","data":{"round":1,"calls":[{"name":"general__extract_key_info"}]}},
  {"type":"tool_results","data":{"round":1,"results":[{"ok":true}]}},
  {"type":"tool_calls","data":{"round":2,"calls":[{"name":"general__generate_structured_report"}]}},
  {"type":"tool_results","data":{"round":2,"results":[{"ok":true}]}},
  {"type":"final","data":{"round":3,
     "content_preview":"J'ai produit un rapport Markdown structuré avec les 2 IPs identifiées..."}}
]
```

---

## Reproduire ces exemples chez vous

**Prérequis** : studio lancé sur `http://127.0.0.1:8080`, MCP `enabled=true`,
modèle GGUF ≥ 3B pour un routage d'outils fiable (0.5B fera parfois les
mauvais choix — voir §Limites du README).

Chaque outil peut être appelé directement (bypass modèle) pour tests /
intégration UI :

```bash
curl -sX POST http://127.0.0.1:8080/api/skills/<skill>/invoke \
  -H "Content-Type: application/json" \
  -d '{"tool":"<tool_name>","arguments":{...}}'
```

Un client OpenAI-compatible peut aussi être branché sur `/v1/chat/completions`
avec `tool_choice: "auto"` ; l'orchestrateur injecte automatiquement le
catalogue MCP.

---

**Rendu SSE en direct** : ouvrez l'UI, envoyez un message, et dépliez le
bouton « Trace agentique » sous chaque réponse pour voir tous les événements
(`start`, `thought`, `tool_calls`, `tool_results`, `final`) horodatés.
