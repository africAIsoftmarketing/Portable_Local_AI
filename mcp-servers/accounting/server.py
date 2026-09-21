"""
Rôle    : skill MCP accounting. 2 outils :
          - verify_accounting_entries : équilibre débit/crédit + validation comptes.
          - calculate_financial_ratios : ratios standards PCG/IFRS.
Auteur  : AfricAIsoft
Licence : MIT
Date    : 2026-08-24
"""
from __future__ import annotations

import sys
from decimal import Decimal, InvalidOperation
from pathlib import Path

_HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(_HERE.parent))
from _shared.mcp_server import MCPServer  # noqa: E402

server = MCPServer(
    name="accounting",
    version="1.0.0",
    description_fr="Vérification d'écritures comptables et calcul de ratios financiers.",
    description_en="Accounting entries verification and financial ratios computation.",
)

# Comptes PCG racines valides (2 premiers caractères)
_VALID_ROOTS = {"1", "2", "3", "4", "5", "6", "7", "8"}


def _to_dec(x) -> Decimal:
    try:
        return Decimal(str(x))
    except (InvalidOperation, TypeError):
        return Decimal(0)


@server.tool(
    name="verify_accounting_entries",
    description="Vérifie l'équilibre débit=crédit d'un ensemble d'écritures comptables (partie double) et détecte les comptes suspicieux.",
    input_schema={
        "type": "object",
        "properties": {
            "entries": {
                "type": "array",
                "description": "Liste d'écritures {account, debit, credit, journal?, label?}.",
                "items": {
                    "type": "object",
                    "properties": {
                        "account": {"type": "string"},
                        "debit": {"type": "number"},
                        "credit": {"type": "number"},
                        "journal": {"type": "string"},
                        "label": {"type": "string"},
                    },
                    "required": ["account"],
                },
            },
        },
        "required": ["entries"],
    },
)
def verify_accounting_entries(args: dict) -> dict:
    entries = args.get("entries", [])
    issues: list[dict] = []
    per_journal: dict[str, dict[str, Decimal]] = {}
    total_debit = Decimal(0)
    total_credit = Decimal(0)

    for i, e in enumerate(entries):
        if not isinstance(e, dict):
            issues.append({"index": i, "type": "invalid_entry",
                           "detail": "l'écriture doit être un objet"})
            continue
        acc = str(e.get("account", "")).strip()
        d = _to_dec(e.get("debit", 0))
        c = _to_dec(e.get("credit", 0))
        j = e.get("journal", "DEFAULT")

        if not acc:
            issues.append({"index": i, "type": "missing_account",
                           "suggestion": "Ajouter un numéro de compte."})
        elif not acc[0].isdigit() or acc[0] not in _VALID_ROOTS:
            issues.append({"index": i, "type": "invalid_account",
                           "account": acc,
                           "suggestion": "Vérifier - la racine PCG attendue est 1-8."})

        if d and c:
            issues.append({"index": i, "type": "both_debit_and_credit",
                           "account": acc,
                           "suggestion": "Une écriture ne doit avoir QUE débit ou crédit."})
        if not d and not c:
            issues.append({"index": i, "type": "empty_entry",
                           "account": acc,
                           "suggestion": "Montant nul débit ET crédit → écriture inutile."})

        total_debit += d
        total_credit += c
        pj = per_journal.setdefault(j, {"debit": Decimal(0), "credit": Decimal(0)})
        pj["debit"] += d
        pj["credit"] += c

    imbalance = total_debit - total_credit
    balanced = imbalance == 0

    journal_issues = []
    for j, tot in per_journal.items():
        diff = tot["debit"] - tot["credit"]
        if diff != 0:
            journal_issues.append({"journal": j,
                                   "debit": float(tot["debit"]),
                                   "credit": float(tot["credit"]),
                                   "imbalance": float(diff)})

    return {
        "balanced": balanced,
        "totals": {"debit": float(total_debit),
                   "credit": float(total_credit),
                   "imbalance": float(imbalance)},
        "per_journal_imbalances": journal_issues,
        "issues": issues,
        "issues_count": len(issues),
        "recommendation": ("Toutes les écritures sont équilibrées." if balanced
                           else f"Déséquilibre global de {float(imbalance):+.2f}. "
                                "Vérifier les journaux listés."),
    }


@server.tool(
    name="calculate_financial_ratios",
    description="Calcule les ratios financiers standards (liquidité, solvabilité, rentabilité) à partir d'un bilan et d'un compte de résultat.",
    input_schema={
        "type": "object",
        "properties": {
            "balance_sheet": {
                "type": "object",
                "description": "{current_assets, current_liabilities, inventory, total_assets, total_liabilities, total_equity}",
            },
            "income_statement": {
                "type": "object",
                "description": "{revenue, net_income, operating_income, cogs?}",
            },
        },
        "required": ["balance_sheet", "income_statement"],
    },
)
def calculate_financial_ratios(args: dict) -> dict:
    bs = args.get("balance_sheet", {}) or {}
    is_ = args.get("income_statement", {}) or {}

    ca = _to_dec(bs.get("current_assets", 0))
    cl = _to_dec(bs.get("current_liabilities", 0))
    inv = _to_dec(bs.get("inventory", 0))
    ta = _to_dec(bs.get("total_assets", 0))
    tl = _to_dec(bs.get("total_liabilities", 0))
    te = _to_dec(bs.get("total_equity", 0))
    rev = _to_dec(is_.get("revenue", 0))
    ni = _to_dec(is_.get("net_income", 0))
    oi = _to_dec(is_.get("operating_income", 0))

    def _ratio(num: Decimal, den: Decimal) -> str:
        if den == 0:
            return "n/a"
        return f"{float(num / den):.3f}"

    ratios = {
        "current_ratio": {
            "value": _ratio(ca, cl),
            "formula": "actif_circulant / passif_courant",
            "interpretation": "≥ 1.5 : bonne liquidité court terme.",
        },
        "quick_ratio": {
            "value": _ratio(ca - inv, cl),
            "formula": "(actif_circulant - stocks) / passif_courant",
            "interpretation": "≥ 1.0 : capacité à honorer les dettes court terme sans écouler les stocks.",
        },
        "debt_to_equity": {
            "value": _ratio(tl, te),
            "formula": "dettes_totales / capitaux_propres",
            "interpretation": "≤ 1.0 : structure financière prudente. > 2.0 : levier élevé.",
        },
        "return_on_assets": {
            "value": _ratio(ni, ta),
            "formula": "résultat_net / actif_total",
            "interpretation": "ROA. Plus élevé = actifs plus productifs.",
        },
        "return_on_equity": {
            "value": _ratio(ni, te),
            "formula": "résultat_net / capitaux_propres",
            "interpretation": "ROE. Rendement pour les actionnaires.",
        },
        "operating_margin": {
            "value": _ratio(oi, rev),
            "formula": "résultat_exploitation / chiffre_affaires",
            "interpretation": "Marge opérationnelle.",
        },
        "net_margin": {
            "value": _ratio(ni, rev),
            "formula": "résultat_net / chiffre_affaires",
            "interpretation": "Marge nette.",
        },
    }
    return {"ratios": ratios,
            "notes": ["Formules calquées sur PCG français / IFRS courantes.",
                      "Valeurs 'n/a' quand dénominateur nul (données insuffisantes)."]}


if __name__ == "__main__":
    server.run()
