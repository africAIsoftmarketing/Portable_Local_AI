# Skill : accounting

Outils comptables et financiers, 100% déterministes, aucune IA.

## Outils

### `verify_accounting_entries`
Vérifie qu'un ensemble d'écritures respecte la partie double (débit = crédit),
au niveau global ET par journal. Détecte : comptes manquants, comptes invalides
(racine PCG hors 1-8), écritures mixtes (débit ET crédit), écritures nulles.

**Input** : `{entries: [{account, debit, credit, journal?, label?}, ...]}`

### `calculate_financial_ratios`
Calcule les 7 ratios standards : current_ratio, quick_ratio, debt_to_equity,
ROA, ROE, operating_margin, net_margin. Formules documentées, `n/a` si
dénominateur nul.

**Input** : `{balance_sheet: {...}, income_statement: {...}}`
