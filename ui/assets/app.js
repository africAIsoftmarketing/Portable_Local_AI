/* AfricAIsoft Portable Studio - logique UI minimaliste
 * Auteur  : AfricAIsoft
 * Licence : MIT
 * Date    : 2026-08-24 (correctifs Phase 2)
 * Rôle    : chat streaming SSE, panneau system prompt, health polling, i18n
 *           complète et réversible (aucune chaîne visible en dur).
 */

(function () {
    "use strict";

    const path = window.location.pathname.replace(/\/$/, "");
    const API_PREFIX = path.startsWith("/api") ? "/api" : "";
    const API_KEY = localStorage.getItem("studio_api_key") || null;

    // ── I18n ─────────────────────────────────────────────────────────────────
    let currentLang = localStorage.getItem("studio_lang") || "fr";
    let translations = {};

    async function loadI18n(lang) {
        try {
            const r = await fetch(`${API_PREFIX}/assets/i18n/${lang}.json`);
            translations = await r.json();
            document.documentElement.lang = lang;
            applyI18n();
            // Ré-applique aussi le contenu dynamique (badge, platform kv).
            await refreshHealth();
            await loadSystemPrompt();
        } catch (e) {
            console.warn("i18n load failed", e);
        }
    }

    function t(key) {
        const v = key.split(".").reduce((o, k) => (o || {})[k], translations);
        return (typeof v === "string") ? v : key;
    }

    function applyI18n() {
        document.querySelectorAll("[data-i18n]").forEach(el => {
            el.textContent = t(el.getAttribute("data-i18n"));
        });
        document.querySelectorAll("[data-i18n-placeholder]").forEach(el => {
            el.placeholder = t(el.getAttribute("data-i18n-placeholder"));
        });
        document.querySelectorAll("[data-i18n-title]").forEach(el => {
            el.title = t(el.getAttribute("data-i18n-title"));
        });
        if (translations.app && translations.app.title) {
            document.title = translations.app.title;
        }
    }

    // ── Helpers réseau ───────────────────────────────────────────────────────
    async function api(path, opts) {
        opts = opts || {};
        const headers = Object.assign({}, opts.headers || {}, {"Content-Type": "application/json"});
        if (API_KEY) headers["Authorization"] = "Bearer " + API_KEY;
        return fetch(`${API_PREFIX}${path}`, Object.assign({}, opts, {headers: headers}));
    }

    // ── Health polling ───────────────────────────────────────────────────────
    async function refreshHealth() {
        const badge = document.getElementById("health-badge");
        const info = document.getElementById("platform-info");
        const modelNameEl = document.getElementById("model-name");
        if (!badge || !info) return;
        try {
            const r = await api("/health");
            const h = await r.json();
            const llamaStatus = (h.components && h.components.llama
                                 && h.components.llama.status) || "?";
            const isOk = h.status === "ok" && llamaStatus === "ok";
            const isPartial = h.status === "ok" && llamaStatus !== "ok";
            badge.className = "badge " + (isOk ? "badge-ok"
                                         : (isPartial ? "badge-warn" : "badge-error"));
            badge.textContent = isOk ? t("badge.ok")
                                     : (isPartial ? t("badge.partial") : t("badge.error"));
            badge.title = t("health.tooltip")
                          + " · llama=" + llamaStatus
                          + " · mcp=" + h.components.mcp.status
                          + " · backend=" + h.backend.backend;

            info.innerHTML = "";
            const rows = [
                [t("platform.os"),      h.platform.os + " " + h.platform.arch],
                [t("platform.backend"), h.backend.backend + " (" + h.backend.reason + ")"],
                [t("platform.version"), h.version],
                [t("platform.auth"),    h.components.api.auth_enabled
                                        ? t("platform.authOn") : t("platform.authOff")],
                [t("platform.mcp"),     h.components.mcp.status],
            ];
            for (const [k, v] of rows) {
                const dt = document.createElement("dt"); dt.textContent = k;
                const dd = document.createElement("dd"); dd.textContent = v;
                info.appendChild(dt); info.appendChild(dd);
            }
            modelNameEl.textContent = (h.components.llama.model) || "–";
            if (h.warnings && h.warnings.length) {
                console.warn("[health warnings]", h.warnings);
            }
        } catch (e) {
            badge.className = "badge badge-error";
            badge.textContent = t("badge.error");
            badge.title = String(e);
        }
    }

    // ── System prompt ────────────────────────────────────────────────────────
    async function loadSystemPrompt() {
        const banner = document.getElementById("sp-locked-banner");
        const ta = document.getElementById("sp-textarea");
        const sourceEl = document.getElementById("sp-source");
        const tokenEl = document.getElementById("sp-token-count");
        const saveBtn = document.getElementById("sp-save");
        const resetBtn = document.getElementById("sp-reset");
        if (!ta) return;

        try {
            const r = await api("/system-prompt");
            if (r.status === 403) {
                banner.classList.remove("hidden");
                ta.disabled = true; saveBtn.disabled = true; resetBtn.disabled = true;
                ta.value = "";
                sourceEl.textContent = t("sysprompt.sourceLocked");
                return;
            }
            banner.classList.add("hidden");
            ta.disabled = false; saveBtn.disabled = false; resetBtn.disabled = false;
            const d = await r.json();
            ta.value = d.content || "";
            sourceEl.textContent = d.source;
            tokenEl.textContent = d.token_count_approx;
        } catch (e) {
            console.error("system-prompt load", e);
        }
    }

    function updateTokenCount() {
        const ta = document.getElementById("sp-textarea");
        const el = document.getElementById("sp-token-count");
        el.textContent = Math.max(1, Math.round(ta.value.length / 3.8));
    }

    async function saveSystemPrompt() {
        const ta = document.getElementById("sp-textarea");
        const btn = document.getElementById("sp-save");
        const originalLabel = t("sysprompt.save");
        btn.disabled = true;
        try {
            const r = await api("/system-prompt", {
                method: "PUT",
                body: JSON.stringify({content: ta.value}),
            });
            if (!r.ok) throw new Error("HTTP " + r.status);
            btn.textContent = t("sysprompt.saved");
            setTimeout(() => { btn.textContent = originalLabel; }, 900);
            await loadSystemPrompt();
        } catch (e) {
            alert(t("errors.saveFailed") + " : " + e.message);
        } finally { btn.disabled = false; }
    }

    async function resetSystemPrompt() {
        if (!confirm(t("sysprompt.confirmReset"))) return;
        await api("/system-prompt/reset", {method: "POST"});
        await loadSystemPrompt();
    }

    // ── Chat streaming ───────────────────────────────────────────────────────
    const conversation = [];

    function addMessage(role, text) {
        const box = document.getElementById("messages");
        const div = document.createElement("div");
        div.className = "msg msg-" + role;
        div.textContent = text;
        box.appendChild(div);
        box.scrollTop = box.scrollHeight;
        return div;
    }

    async function sendMessage(text) {
        addMessage("user", text);
        conversation.push({role: "user", content: text});
        const assistantDiv = addMessage("assistant", "");
        const status = document.getElementById("stream-status");
        status.textContent = t("chat.streaming");

        try {
            const r = await api("/v1/chat/completions", {
                method: "POST",
                body: JSON.stringify({
                    messages: conversation,
                    stream: true,
                    temperature: 0.6,
                    max_tokens: 512,
                }),
            });
            if (!r.ok) throw new Error("HTTP " + r.status + ": " + await r.text());

            const reader = r.body.getReader();
            const decoder = new TextDecoder();
            let buffer = "";
            let full = "";
            while (true) {
                const {value, done} = await reader.read();
                if (done) break;
                buffer += decoder.decode(value, {stream: true});
                const lines = buffer.split("\n");
                buffer = lines.pop() || "";
                for (const line of lines) {
                    if (!line.startsWith("data:")) continue;
                    const data = line.slice(5).trim();
                    if (data === "[DONE]") break;
                    try {
                        const json = JSON.parse(data);
                        const delta = (json.choices && json.choices[0]
                                       && json.choices[0].delta
                                       && json.choices[0].delta.content) || "";
                        if (delta) {
                            full += delta;
                            assistantDiv.textContent = full;
                            document.getElementById("messages").scrollTop = 1e9;
                        }
                    } catch (_) { /* keepalive ignoré */ }
                }
            }
            conversation.push({role: "assistant", content: full});
            status.textContent = t("chat.idle");
        } catch (e) {
            assistantDiv.remove();
            addMessage("error", "⚠ " + e.message);
            status.textContent = t("chat.idle");
        }
    }

    // ── Bootstrap ────────────────────────────────────────────────────────────
    document.addEventListener("DOMContentLoaded", async () => {
        const langSelect = document.getElementById("lang-select");
        langSelect.value = currentLang;
        langSelect.addEventListener("change", async (e) => {
            currentLang = e.target.value;
            localStorage.setItem("studio_lang", currentLang);
            await loadI18n(currentLang);
        });

        document.getElementById("sp-textarea").addEventListener("input", updateTokenCount);
        document.getElementById("sp-save").addEventListener("click", saveSystemPrompt);
        document.getElementById("sp-reset").addEventListener("click", resetSystemPrompt);

        document.getElementById("chat-form").addEventListener("submit", (e) => {
            e.preventDefault();
            const input = document.getElementById("user-input");
            const text = input.value.trim();
            if (!text) return;
            input.value = "";
            sendMessage(text);
        });

        await loadI18n(currentLang);   // charge FR/EN et déclenche 1er refresh
        setInterval(refreshHealth, 5000);
    });
})();
