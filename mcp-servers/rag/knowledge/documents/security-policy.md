# Politique de sécurité SSH — extrait

## Rotation des clés

Les clés SSH utilisateurs doivent être renouvelées tous les 12 mois.
Les clés de service (déploiement, sauvegardes) tous les 6 mois.
La révocation d'une clé compromise doit être appliquée sous 4 heures.

## Détection de brute-force

Toute IP source produisant plus de 5 échecs d'authentification en 10 minutes
est automatiquement bloquée par fail2ban pour 24 heures. Les IP figurant sur
la blocklist Tor sont bloquées à titre préventif.

## Connexions root

La connexion SSH directe en root est strictement interdite. Toute occurrence
de `Accepted password for root` dans auth.log constitue un incident P0
et déclenche une investigation immédiate.

## Sudo et escalade

L'usage de sudo est journalisé. Les commandes contenant `rm -rf`, `wget`,
`curl`, `nc`, `bash -i` doivent être auditées manuellement dans les 24h.
