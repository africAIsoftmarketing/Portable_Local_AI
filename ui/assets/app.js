/* AfricAIsoft Portable Studio - logique UI minimaliste
 * Auteur  : AfricAIsoft
 * Licence : MIT
 * Date    : 2026-08-24
 * Rôle    : chat streaming SSE, panneau system prompt, health polling, i18n.
 */

(function () {
    "use strict";

    // Détecte automatiquement le préfixe API (utile en dev où tout est sous /api).
    // Si la page est chargée via /api/, on prend /api ; sinon vide.
    const path = window.location.pathname.replace(/\/$/, "");
    const API_PREFIX = path.startsWith("/api") ? "/api" : "";
    const API_KEY = localStorage.getItem("studio_api_key") || null;

    // ── I18n ──────────────────────────────────────────────────────────────────
    let currentLang = localStorage.getItem("studio_lang") || "fr";
    let translations = {};

    async function loadI18n(lang) {
        try {
            const r = await fetch(`${API_PREFIX}/assets/i18n/${lang}.json`);
            translations = await r.json();
            applyI18n();
        } catch (e) {
            console.warn("i18n load failed", e);
        }
    }

    function t(key) {
        return key.split(".").reduce((o, k) => (o || {})[k], translations) || key;
    }

    function applyI18n() {
        document.querySelectorAll("[data-i18n]").forEach(el => {
            el.textContent = t(el.getAttribute("data-i18n"));
        });
        document.querySelectorAll("[data-i18n-placeholder]").forEach(el => {
            el.placeholder = t(el.getAttribute("data-i18n-placeholder"));
        });
    }

    // ── Helpers réseau ────────────────────────────────────────────────────────
    async function api(path, opts = {}) {
        const headers = { "Content-Type": "application/json", ...(opts.headers || {}) };
        if (API_KEY) headers["Authorization"] = `Bearer ${API_KEY}`;
        const r = await fetch(`${API_PREFIX}${path}`, { ...opts, headers });
        return r;
    }

    // ── Health polling ────────────────────────────────────────────────────────
    async function refreshHealth() {
        const badge = document.getElementById("health-badge");
        const info = document.getElementById("platform-info");
        const modelNameEl = document.getElementById("model-name");
        try {
            const r = await api("/health");
            const h = await r.json();
            const llamaStatus = (h.components && h.components.llama && h.components.llama.status) || "?";
            const isOk = h.status === "ok" && llamaStatus === "ok";
            const isPartial = h.status === "ok" && llamaStatus !== "ok";
            badge.className = "badge " + (isOk ? "badge-ok" : (isPartial ? "badge-warn" : "badge-error"));
            badge.textContent = isOk ? "OK" : (isPartial ? "PARTIEL" : "KO");
            badge.title = `llama=${llamaStatus} · mcp=${h.components.mcp.status} · backend=${h.backend.backend}`;

            info.innerHTML = "";
            const rows = [
                ["OS", `${h.platform.os} ${h.platform.arch}`],
                ["Backend", `${h.backend.backend} (${h.backend.reason})`],
                ["Version", h.version],
                ["Auth", h.components.api.auth_enabled ? "activée" : "désactivée"],
                ["MCP", h.components.mcp.status],
            ];
            for (const [k, v] of rows) {
                const dt = document.createElement("dt"); dt.textContent = k;
                const dd = document.createElement("dd"); dd.textContent = v;
                info.appendChild(dt); info.appendChild(dd);
            }
            if (h.components.llama.model) {
                modelNameEl.textContent = h.components.llama.model;
            } else {
                modelNameEl.textContent = "–";
            }
            if (h.warnings && h.warnings.length) {
                console.warn("[health warnings]", h.warnings);
            }
        } catch (e) {
            badge.className = "badge badge-error";
            badge.textContent = "KO";
            badge.title = String(e);
        }
    }

    // ── System prompt ─────────────────────────────────────────────────────────
    async function loadSystemPrompt() {
        const banner = document.getElementById("sp-locked-banner");
        const ta = document.getElementById("sp-textarea");
        const sourceEl = document.getElementById("sp-source");
        const tokenEl = document.getElementById("sp-token-count");
        const saveBtn = document.getElementById("sp-save");
        const resetBtn = document.getElementById("sp-reset");

        try {
            const r = await api("/system-prompt");
            if (r.status === 403) {
                banner.classList.remove("hidden");
                ta.disabled = true; saveBtn.disabled = true; resetBtn.disabled = true;
                ta.value = "";
                sourceEl.textContent = "locked";
                return;
            }
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
        btn.disabled = true;
        try {
            const r = await api("/system-prompt", {
                method: "PUT",
                body: JSON.stringify({ content: ta.value }),
            });
            if (!r.ok) throw new Error(`HTTP ${r.status}`);
            btn.textContent = "✓";
            setTimeout(() => btn.textContent = t("sysprompt.save"), 900);
            await loadSystemPrompt();
        } catch (e) {
            alert("Erreur : " + e.message);
        } finally { btn.disabled = false; }
    }

    async function resetSystemPrompt() {
        if (!confirm(t("sysprompt.confirmReset"))) return;
        await api("/system-prompt/reset", { method: "POST" });
        await loadSystemPrompt();
    }

    // ── Chat streaming ────────────────────────────────────────────────────────
    const conversation = [];

    function addMessage(role, text) {
        const box = document.getElementById("messages");
        const div = document.createElement("div");
        div.className = `msg msg-${role}`;
        div.textContent = text;
        box.appendChild(div);
        box.scrollTop = box.scrollHeight;
        return div;
    }

    async function sendMessage(text) {
        addMessage("user", text);
        conversation.push({ role: "user", content: text });
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
            if (!r.ok) throw new Error(`HTTP ${r.status}: ${await r.text()}`);

            const reader = r.body.getReader();
            const decoder = new TextDecoder();
            let buffer = "";
            let full = "";
            while (true) {
                const { value, done } = await reader.read();
                if (done) break;
                buffer += decoder.decode(value, { stream: true });
                const lines = buffer.split("\n");
                buffer = lines.pop() || "";
                for (const line of lines) {
                    if (!line.startsWith("data:")) continue;
                    const data = line.slice(5).trim();
                    if (data === "[DONE]") break;
                    try {
                        const json = JSON.parse(data);
                        const delta = json.choices?.[0]?.delta?.content
                                   || json.choices?.[0]?.message?.content
                                   || "";
                        if (delta) {
                            full += delta;
                            assistantDiv.textContent = full;
                            document.getElementById("messages").scrollTop = 1e9;
                        }
                    } catch { /* ignore keepalive */ }
                }
            }
            conversation.push({ role: "assistant", content: full });
            status.textContent = t("chat.idle");
        } catch (e) {
            assistantDiv.remove();
            addMessage("error", `⚠ ${e.message}`);
            status.textContent = t("chat.idle");
        }
    }

    // ── Bootstrap ─────────────────────────────────────────────────────────────
    document.addEventListener("DOMContentLoaded", async () => {
        document.getElementById("lang-select").value = currentLang;
        document.getElementById("lang-select").addEventListener("change", async (e) => {
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

        await loadI18n(currentLang);
        await refreshHealth();
        await loadSystemPrompt();
        setInterval(refreshHealth, 5000);
    });
})();
