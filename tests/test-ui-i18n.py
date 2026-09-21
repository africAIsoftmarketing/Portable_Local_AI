"""
Rôle    : test Playwright de la persistance i18n dans le navigateur RÉEL.
          Vérifie deux cycles : FR→EN→reload→EN et EN→FR→reload→FR.
          Assert sur localStorage, valeur du <select>, document.documentElement.lang
          ET texte réel affiché (labels d'onglets, titres de sections).
Auteur  : AfricAIsoft
Licence : MIT
Usage   : /opt/plugins-venv/bin/python tests/test-ui-i18n.py
          (retour 0 si tout passe, 1 sinon)
"""
from __future__ import annotations

import asyncio
import os
import sys

# Chemin vers un chromium préinstallé (headless_shell) — évite un download réseau.
os.environ.setdefault(
    "PLAYWRIGHT_BROWSERS_PATH",
    "/root/.cache/ms-playwright",
)

from playwright.async_api import async_playwright  # noqa: E402

BASE_URL = os.environ.get("BASE_URL", "http://127.0.0.1:8001")
API_PREFIX = os.environ.get("API_PREFIX", "/api")
URL = f"{BASE_URL}{API_PREFIX}/"


class Report:
    def __init__(self):
        self.pass_ = 0
        self.fail = 0

    def ok(self, msg: str):
        self.pass_ += 1
        print(f"  \033[0;32m✓\033[0m {msg}")

    def ko(self, msg: str):
        self.fail += 1
        print(f"  \033[0;31m✗\033[0m {msg}")


async def dump_state(page):
    return await page.evaluate(
        """() => ({
            ls: localStorage.getItem('studio_lang'),
            sel: document.getElementById('lang-select').value,
            htmlLang: document.documentElement.lang,
            tabSkills: (document.querySelector('[data-testid=\"tab-skills\"]')||{}).textContent||'',
            sidebarTitle: (document.querySelector('.sidebar-header h2')||{}).textContent||'',
            spSaveLabel: (document.querySelector('[data-testid=\"sp-save\"]')||{}).textContent||'',
            welcomeText: (document.querySelector('.msg-hint')||{}).textContent||'',
        })"""
    )


async def cycle(page, target: str, expected_label: dict, r: Report):
    """Bascule sur `target`, reload, vérifie tous les invariants."""
    print(f"\n  ▸ Cycle → {target.upper()}")
    # Bascule
    await page.select_option('[data-testid="lang-select"]', target)
    # Laisse le change handler faire son travail (setItem + loadI18n async).
    await page.wait_for_function(
        f"() => localStorage.getItem('studio_lang') === '{target}' "
        f"&& document.getElementById('lang-select').value === '{target}'",
        timeout=3000,
    )
    st_before = await dump_state(page)
    print(f"    avant reload : {st_before}")
    # Reload (hard : bypass cache) + attend rehydratation
    await page.reload(wait_until="networkidle")
    await page.wait_for_function(
        f"() => document.getElementById('lang-select').value === '{target}'",
        timeout=3000,
    )
    st_after = await dump_state(page)
    print(f"    après reload : {st_after}")

    if st_after["ls"] == target:
        r.ok(f"localStorage.studio_lang == '{target}' après reload")
    else:
        r.ko(f"localStorage.studio_lang = '{st_after['ls']}' (attendu '{target}')")

    if st_after["sel"] == target:
        r.ok(f"select#lang-select.value == '{target}'")
    else:
        r.ko(f"select value = '{st_after['sel']}'")

    if st_after["htmlLang"] == target:
        r.ok(f"<html lang='{target}'>")
    else:
        r.ko(f"htmlLang = '{st_after['htmlLang']}'")

    # Labels traduits (dont un DISCRIMINANT FR/EN : le bouton Save/Enregistrer).
    if st_after["tabSkills"].strip() == expected_label["tabSkills"]:
        r.ok(f"tab Skills label = '{expected_label['tabSkills']}'")
    else:
        r.ko(f"tab Skills = '{st_after['tabSkills']}' (attendu '{expected_label['tabSkills']}')")

    if st_after["sidebarTitle"].strip() == expected_label["sidebarTitle"]:
        r.ok(f"sidebar h2 = '{expected_label['sidebarTitle']}'")
    else:
        r.ko(f"sidebar h2 = '{st_after['sidebarTitle']}' (attendu '{expected_label['sidebarTitle']}')")

    # DISCRIMINANT : sysprompt.save = "Enregistrer" (FR) vs "Save" (EN).
    exp_save = expected_label["spSave"]
    if st_after["spSaveLabel"].strip() == exp_save:
        r.ok(f"bouton Save discriminant = '{exp_save}'")
    else:
        r.ko(f"bouton Save = '{st_after['spSaveLabel']}' (attendu '{exp_save}')")

    # DISCRIMINANT : welcome hint (chat.welcome) diffère FR/EN.
    exp_welcome_prefix = expected_label["welcomePrefix"]
    if st_after["welcomeText"].startswith(exp_welcome_prefix):
        r.ok(f"message d'accueil commence par '{exp_welcome_prefix}'")
    else:
        r.ko(f"accueil = '{st_after['welcomeText'][:50]}' (attendu préfixe '{exp_welcome_prefix}')")


async def main() -> int:
    r = Report()
    print("=" * 58)
    print(f"  test-ui-i18n.py : {URL}")
    print("=" * 58)

    async with async_playwright() as pw:
        # Utilise le chromium préinstallé (headless_shell suffit pour ce test).
        browser = await pw.chromium.launch(headless=True)
        context = await browser.new_context()
        page = await context.new_page()

        # Page vierge : purge tout état, puis reload pour partir clean.
        await page.goto(URL, wait_until="networkidle")
        await page.evaluate("localStorage.clear()")
        await page.reload(wait_until="networkidle")
        await page.wait_for_selector('[data-testid="lang-select"]')
        await page.wait_for_function(
            "() => document.querySelector('[data-testid=\"lang-select\"]').value === 'fr' "
            "|| document.querySelector('[data-testid=\"lang-select\"]').value === 'en'",
            timeout=3000,
        )
        initial = await dump_state(page)
        print(f"  état initial (localStorage vide) : {initial}")

        # Cycle 1 : FR → EN → reload → EN
        await page.select_option('[data-testid="lang-select"]', 'fr')
        await page.wait_for_function(
            "() => localStorage.getItem('studio_lang') === 'fr'", timeout=3000,
        )
        # Après avoir mis FR, il faut ouvrir l'onglet System Prompt pour rendre
        # le bouton sp-save visible et donc textContent lisible par le test.
        await page.click('[data-testid="tab-systemprompt"]')
        await page.wait_for_selector('[data-testid="sp-save"]', state='visible')
        await cycle(page, 'en', {
            "tabSkills": "Skills", "sidebarTitle": "Conversations",
            "spSave": "Save", "welcomePrefix": "Welcome",
        }, r)

        # Cycle 2 : EN → FR → reload → FR
        await page.click('[data-testid="tab-systemprompt"]')
        await page.wait_for_selector('[data-testid="sp-save"]', state='visible')
        await cycle(page, 'fr', {
            "tabSkills": "Skills", "sidebarTitle": "Conversations",
            "spSave": "Enregistrer", "welcomePrefix": "Bienvenue",
        }, r)

        # Cycle 3 : bonus — deuxième bascule EN pour confirmer robustesse.
        await page.click('[data-testid="tab-systemprompt"]')
        await page.wait_for_selector('[data-testid="sp-save"]', state='visible')
        await cycle(page, 'en', {
            "tabSkills": "Skills", "sidebarTitle": "Conversations",
            "spSave": "Save", "welcomePrefix": "Welcome",
        }, r)

        await browser.close()

    print("-" * 58)
    print(f"  PASS={r.pass_}  FAIL={r.fail}")
    print("=" * 58)
    return 0 if r.fail == 0 else 1


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
