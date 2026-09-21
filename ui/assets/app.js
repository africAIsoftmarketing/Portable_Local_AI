/* AfricAIsoft Portable Studio — logique UI Phase 4 (Vanilla JS, offline strict).
 * Auteur  : AfricAIsoft
 * Licence : MIT
 * Rôle    : orchestration complète de l'UI (conversations, chat streaming + agent,
 *           skills, modèles, système prompt, config, thème, i18n).
 *           Aucune dépendance externe : fetch API, SSE, localStorage.
 */
(function () {
    "use strict";

    // ── Constantes API ───────────────────────────────────────────────────────
    const path = window.location.pathname.replace(/\/$/, "");
    const API_PREFIX = path.startsWith("/api") ? "/api" : "";
    const API_KEY = localStorage.getItem("studio_api_key") || null;

    // ── État global ──────────────────────────────────────────────────────────
    // Validation stricte : seules "fr" et "en" sont acceptées, sinon détection
    // du navigateur. Idem thème (déjà robuste).
    function validLang(v)  { return (v === "fr" || v === "en") ? v : null; }
    function validTheme(v) { return (v === "dark" || v === "light") ? v : null; }

    const state = {
        lang:   validLang(localStorage.getItem("studio_lang"))   || detectLang(),
        theme:  validTheme(localStorage.getItem("studio_theme")) || detectTheme(),
        translations: {},
        currentConversationId: null,
        conversations: [],
        health: null,
        skills: null,
        activeModel: null,
        activePreset: null,
        systemPromptLocked: false,
    };

    // Bloc utilitaires --------------------------------------------------------
    function readStored(key, fallback) {
        const v = localStorage.getItem(key);
        return v === null ? fallback : v;
    }
    function detectLang() {
        return (navigator.language || "fr").toLowerCase().startsWith("en") ? "en" : "fr";
    }
    function detectTheme() {
        return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches
            ? "dark" : "light";
    }
    function $(sel) { return document.querySelector(sel); }
    function $$(sel) { return document.querySelectorAll(sel); }
    function fmtBytes(n) {
        if (!n) return "0 B";
        const u = ["B","KB","MB","GB","TB"];
        let i = 0; let v = n;
        while (v >= 1024 && i < u.length - 1) { v /= 1024; i++; }
        return v.toFixed(v < 10 && i > 0 ? 1 : 0) + " " + u[i];
    }
    function fmtDate(iso) {
        if (!iso) return "–";
        try { return new Date(iso).toLocaleString(state.lang); }
        catch (_) { return iso; }
    }
    async function api(p, opts) {
        opts = opts || {};
        const headers = Object.assign({}, opts.headers || {},
            {"Content-Type": "application/json"});
        if (API_KEY) headers["Authorization"] = "Bearer " + API_KEY;
        return fetch(`${API_PREFIX}${p}`, Object.assign({}, opts, {headers}));
    }

    // ── I18n ────────────────────────────────────────────────────────────────
    // Chargement STRICTEMENT séquentiel : on ne rend AUCUN texte avant que
    // le dictionnaire de la langue active ne soit résolu. Un try/finally
    // dans le bootstrap retire la classe `booting` du <body> pour éviter
    // que l'UI reste invisible si le fetch échoue.
    async function loadI18n(lang) {
        const url = `${API_PREFIX}/assets/i18n/${lang}.json?_=${Date.now()}`;
        try {
            const r = await fetch(url, { cache: "no-store" });
            if (!r.ok) throw new Error("HTTP " + r.status);
            state.translations = await r.json();
        } catch (e) {
            // En cas d'échec réseau, on n'écrase pas les traductions
            // précédentes : cela permet de conserver l'affichage courant
            // au lieu de retomber sur les data-i18n keys.
            console.warn("i18n load failed for", lang, e);
            if (!state.translations || !Object.keys(state.translations).length) {
                state.translations = {};
            }
        }
        document.documentElement.lang = lang;
        applyI18n();
    }
    function t(key, params) {
        const val = key.split(".").reduce((o, k) => (o || {})[k], state.translations);
        if (typeof val !== "string") return key;
        if (!params) return val;
        return val.replace(/\{(\w+)\}/g, (_, k) => (params[k] !== undefined ? params[k] : ""));
    }
    function applyI18n() {
        $$("[data-i18n]").forEach(el => { el.textContent = t(el.getAttribute("data-i18n")); });
        $$("[data-i18n-placeholder]").forEach(el => {
            el.placeholder = t(el.getAttribute("data-i18n-placeholder"));
        });
        $$("[data-i18n-title]").forEach(el => {
            el.title = t(el.getAttribute("data-i18n-title"));
        });
        if (state.translations.app && state.translations.app.title) {
            document.title = state.translations.app.title;
        }
    }

    // ── Thème ───────────────────────────────────────────────────────────────
    function applyTheme(theme) {
        const t = validTheme(theme) || "light";
        state.theme = t;
        document.documentElement.setAttribute("data-theme", t);
        localStorage.setItem("studio_theme", t);
        const icon = $(".theme-icon");
        if (icon) icon.textContent = t === "dark" ? "☀" : "◐";
    }

    // ── Langue (même pattern strict que le thème) ───────────────────────────
    // Écriture localStorage UNIQUEMENT ici (au clic ou lors d'un changement
    // explicite). Aucun `setItem` à l'init : la valeur en localStorage
    // n'est jamais réécrite tant que l'utilisateur n'agit pas.
    function applyLang(lang) {
        const l = validLang(lang) || "fr";
        state.lang = l;
        document.documentElement.lang = l;
        localStorage.setItem("studio_lang", l);
        const sel = $("#lang-select");
        if (sel && sel.value !== l) sel.value = l;
    }

    // ── Health polling ──────────────────────────────────────────────────────
    async function refreshHealth() {
        const badge = $("#health-badge");
        try {
            const h = await (await api("/health")).json();
            state.health = h;
            const llama = (h.components && h.components.llama && h.components.llama.status) || "?";
            const isOk = h.status === "ok" && llama === "ok";
            const isPartial = h.status === "ok" && llama !== "ok";
            badge.className = "badge " + (isOk ? "badge-ok" : (isPartial ? "badge-warn" : "badge-error"));
            badge.textContent = isOk ? t("badge.ok") : (isPartial ? t("badge.partial") : t("badge.error"));
            $("#model-name").textContent = (h.components.llama && h.components.llama.model) || "–";
            state.activeModel = h.components.llama ? h.components.llama.model : null;
            renderPlatformInfo();
            renderPerfInfo();
            // Rafraîchit le highlight du modèle actif dans la liste des modèles.
            $$("#models-list .model-card").forEach(c => {
                c.classList.toggle("active", c.dataset.modelId === state.activeModel);
            });
        } catch (e) {
            badge.className = "badge badge-error";
            badge.textContent = t("badge.error");
        }
    }
    function renderPlatformInfo() {
        const info = $("#platform-info");
        if (!info || !state.health) return;
        info.innerHTML = "";
        const h = state.health;
        const rc = h.backend.reason_code || "";
        const rp = h.backend.reason_params || {};
        const reasonKey = "backend_reason." + rc;
        let reason = t(reasonKey, rp);
        if (reason === reasonKey) reason = rc || h.backend.reason || "";
        const rows = [
            [t("platform.os"),      h.platform.os + " " + h.platform.arch],
            [t("platform.backend"), h.backend.backend + " (" + reason + ")"],
            [t("platform.version"), h.version],
            [t("platform.auth"),    h.components.api.auth_enabled ? t("platform.authOn") : t("platform.authOff")],
            [t("platform.mcp"),     h.components.mcp.status || "–"],
        ];
        for (const [k, v] of rows) {
            const dt = document.createElement("dt"); dt.textContent = k;
            const dd = document.createElement("dd"); dd.textContent = v;
            info.appendChild(dt); info.appendChild(dd);
        }
    }
    function renderPerfInfo() {
        const info = $("#perf-info");
        if (!info || !state.health) return;
        info.innerHTML = "";
        const h = state.health;
        const rows = [
            [t("config.perf.llamaStatus"), h.components.llama.status || "–"],
            [t("config.perf.mcpStatus"),   (h.components.mcp.running || 0) + "/" + (h.components.mcp.total || 0)],
            [t("config.perf.authStatus"),  h.components.api.auth_enabled ? "on" : "off"],
            [t("config.perf.modelActive"), h.components.llama.model || "–"],
            [t("config.perf.contextSize"), h.components.llama.context_size || "–"],
            [t("config.perf.backend"),     h.backend.backend || "–"],
        ];
        for (const [k, v] of rows) {
            const dt = document.createElement("dt"); dt.textContent = k;
            const dd = document.createElement("dd"); dd.textContent = v;
            info.appendChild(dt); info.appendChild(dd);
        }
    }

    // ── Conversations ───────────────────────────────────────────────────────
    async function refreshConversations() {
        try {
            const d = await (await api("/conversations")).json();
            state.conversations = d.conversations || [];
            renderConversationList();
            $("#conversation-count").textContent = state.conversations.length;
        } catch (e) {
            console.error("conv list", e);
        }
    }
    function renderConversationList() {
        const list = $("#conversation-list");
        list.innerHTML = "";
        if (!state.conversations.length) {
            const empty = document.createElement("div");
            empty.className = "muted small"; empty.style.padding = "10px";
            empty.textContent = t("conversations.empty");
            list.appendChild(empty);
            return;
        }
        for (const c of state.conversations) {
            const el = document.createElement("div");
            el.className = "conv-item";
            el.setAttribute("data-testid", "conv-" + c.id);
            if (c.id === state.currentConversationId) el.classList.add("active");
            el.innerHTML = `
                <div class="conv-title">${Markdown.escape(c.title || "—")}</div>
                <div class="conv-meta">${c.message_count} msg · ${fmtDate(c.updated_at)}</div>`;
            el.addEventListener("click", () => loadConversation(c.id));
            list.appendChild(el);
        }
    }
    async function createConversation() {
        const r = await api("/conversations", {method: "POST",
            body: JSON.stringify({title: t("chat.untitled")})});
        const c = await r.json();
        await refreshConversations();
        loadConversation(c.id);
    }
    async function loadConversation(id) {
        state.currentConversationId = id;
        renderConversationList();
        try {
            const r = await api("/conversations/" + id);
            if (!r.ok) throw new Error("HTTP " + r.status);
            const conv = await r.json();
            $("#chat-title-input").value = conv.title || "";
            renderMessagesFromHistory(conv.messages || []);
        } catch (e) {
            renderMessagesFromHistory([]);
        }
    }
    async function renameConversation() {
        if (!state.currentConversationId) return;
        const current = $("#chat-title-input").value;
        const nt = prompt(t("conversations.renamePrompt"), current);
        if (!nt || nt === current) return;
        await api("/conversations/" + state.currentConversationId, {
            method: "PUT", body: JSON.stringify({title: nt})
        });
        $("#chat-title-input").value = nt;
        await refreshConversations();
    }
    async function deleteConversation() {
        if (!state.currentConversationId) return;
        if (!confirm(t("conversations.confirmDelete"))) return;
        await api("/conversations/" + state.currentConversationId, {method: "DELETE"});
        state.currentConversationId = null;
        $("#chat-title-input").value = "";
        renderMessagesFromHistory([]);
        await refreshConversations();
    }
    async function appendMessagesToStore(msgs) {
        if (!state.currentConversationId) {
            const r = await api("/conversations", {method: "POST",
                body: JSON.stringify({title: t("chat.untitled")})});
            const c = await r.json();
            state.currentConversationId = c.id;
        }
        await api("/conversations/" + state.currentConversationId + "/messages", {
            method: "POST", body: JSON.stringify({messages: msgs})
        });
    }

    // ── Rendu messages ──────────────────────────────────────────────────────
    function renderMessagesFromHistory(messages) {
        const box = $("#messages");
        box.innerHTML = "";
        if (!messages.length) {
            const w = document.createElement("div");
            w.className = "msg-hint"; w.textContent = t("chat.welcome");
            box.appendChild(w);
            return;
        }
        for (const m of messages) {
            if (m.role === "user") addUserBubble(m.content);
            else if (m.role === "assistant") {
                const d = addAssistantBubble();
                d.querySelector(".md").innerHTML = Markdown.render(m.content || "");
                if (m.trace && m.trace.length) attachTrace(d, m.trace);
            }
        }
    }
    function addUserBubble(text) {
        const box = $("#messages");
        const div = document.createElement("div");
        div.className = "msg msg-user"; div.textContent = text;
        box.appendChild(div); box.scrollTop = box.scrollHeight;
        return div;
    }
    function addAssistantBubble() {
        const box = $("#messages");
        const div = document.createElement("div");
        div.className = "msg msg-assistant";
        div.innerHTML = '<div class="md"></div>';
        box.appendChild(div); box.scrollTop = box.scrollHeight;
        return div;
    }
    function addErrorBubble(msg) {
        const box = $("#messages");
        const div = document.createElement("div");
        div.className = "msg msg-error"; div.textContent = "⚠ " + msg;
        box.appendChild(div); box.scrollTop = box.scrollHeight;
    }
    function attachTrace(assistantDiv, trace) {
        if (!trace || !trace.length) return;
        const toolCallEvents = trace.filter(e => e.type === "tool_calls");
        const toolCount = toolCallEvents.reduce((a, e) =>
            a + ((e.data && e.data.calls && e.data.calls.length) || 0), 0);
        const wrap = document.createElement("div");
        wrap.className = "trace-block";
        wrap.setAttribute("data-testid", "trace-block");
        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = "trace-toggle";
        btn.setAttribute("data-testid", "trace-toggle");
        btn.innerHTML = `<span class="chev">▸</span> ${t("chat.traceToggle")}
            <span class="trace-badge">${toolCount} ${t("chat.toolsCount")}</span>`;
        btn.addEventListener("click", () => wrap.classList.toggle("open"));
        const evs = document.createElement("div"); evs.className = "trace-events";
        for (const e of trace) {
            const line = document.createElement("div"); line.className = "trace-event";
            const time = (e.ts || "").split("T")[1] || (e.ts || "");
            const label = t("chat.trace." + e.type) === "chat.trace." + e.type
                          ? e.type : t("chat.trace." + e.type);
            line.innerHTML = `<div class="t-time">${time}</div>
                <div class="t-type">${label}</div>`;
            if (e.data && Object.keys(e.data).length) {
                const pre = document.createElement("pre");
                pre.textContent = JSON.stringify(e.data, null, 2);
                line.appendChild(pre);
            }
            evs.appendChild(line);
        }
        wrap.appendChild(btn); wrap.appendChild(evs);
        assistantDiv.appendChild(wrap);
    }

    // ── Envoi de messages ───────────────────────────────────────────────────
    async function sendMessage(text) {
        if (!text.trim()) return;
        addUserBubble(text);
        const assistantDiv = addAssistantBubble();
        const md = assistantDiv.querySelector(".md");
        md.innerHTML = '<span class="typing"></span>';
        const status = $("#stream-status");
        status.textContent = t("chat.streaming");
        $("#chat-send-btn").disabled = true;

        // Récupération de l'historique persisté pour envoyer à l'API.
        const hist = await getCurrentHistory();
        hist.push({role: "user", content: text});

        try {
            const r = await api("/v1/chat/completions", {
                method: "POST",
                body: JSON.stringify({
                    messages: hist, stream: true,
                    temperature: 0.6, max_tokens: 512,
                }),
            });
            if (!r.ok) throw new Error("HTTP " + r.status + ": " + await r.text());
            const contentType = r.headers.get("content-type") || "";
            let fullText = "";
            if (contentType.includes("application/json")) {
                // Non-stream (boucle agentique retourne JSON complet avec trace)
                const data = await r.json();
                const msg = data.choices && data.choices[0] && data.choices[0].message;
                fullText = (msg && msg.content) || "";
                md.innerHTML = Markdown.render(fullText);
                if (data.metadata && data.metadata.trace) {
                    attachTrace(assistantDiv, data.metadata.trace);
                }
            } else {
                // SSE stream classique
                fullText = await consumeSSE(r, md);
            }
            const assistantMsg = {role: "assistant", content: fullText};
            const traceEl = assistantDiv.querySelector(".trace-events");
            if (traceEl) assistantMsg.trace = collectTraceFromDom(assistantDiv);
            await appendMessagesToStore([{role: "user", content: text}, assistantMsg]);
            await refreshConversations();
            status.textContent = t("chat.idle");
        } catch (e) {
            assistantDiv.remove();
            addErrorBubble(e.message);
            status.textContent = t("chat.idle");
        } finally {
            $("#chat-send-btn").disabled = false;
        }
    }
    async function getCurrentHistory() {
        if (!state.currentConversationId) return [];
        try {
            const r = await api("/conversations/" + state.currentConversationId);
            if (!r.ok) return [];
            const conv = await r.json();
            return (conv.messages || []).map(m => ({role: m.role, content: m.content || ""}));
        } catch (_) { return []; }
    }
    async function consumeSSE(r, mdEl) {
        const reader = r.body.getReader();
        const decoder = new TextDecoder();
        let buffer = "", full = "";
        while (true) {
            const {value, done} = await reader.read();
            if (done) break;
            buffer += decoder.decode(value, {stream: true});
            const lines = buffer.split("\n"); buffer = lines.pop() || "";
            for (const line of lines) {
                if (!line.startsWith("data:")) continue;
                const data = line.slice(5).trim();
                if (data === "[DONE]") break;
                try {
                    const j = JSON.parse(data);
                    const delta = (j.choices && j.choices[0] && j.choices[0].delta
                                   && j.choices[0].delta.content) || "";
                    if (delta) { full += delta; mdEl.innerHTML = Markdown.render(full); }
                } catch (_) {}
            }
        }
        return full;
    }
    function collectTraceFromDom() { return []; /* déjà attaché via attachTrace */ }

    // ── Skills / RAG ────────────────────────────────────────────────────────
    async function refreshSkills() {
        try {
            const d = await (await api("/skills")).json();
            state.skills = d;
            renderSkills();
        } catch (e) {
            $("#skills-list").innerHTML = `<div class="muted small">${t("skills.loadError")}</div>`;
        }
    }
    function renderSkills() {
        const list = $("#skills-list"); list.innerHTML = "";
        if (!state.skills || !state.skills.skills) return;
        for (const sk of state.skills.skills) {
            const card = document.createElement("div");
            card.className = "skill-card";
            card.setAttribute("data-testid", "skill-" + sk.name);
            const stKey = "skills.status." + sk.status;
            const stLabel = t(stKey) !== stKey ? t(stKey) : sk.status;
            const badgeCls = sk.status === "running" ? "badge-ok"
                           : (sk.status === "unavailable" || sk.status === "crashed")
                              ? "badge-error" : "badge-warn";
            const toolsHtml = (sk.tools || []).map(tl =>
                `<span class="skill-tool" title="${Markdown.escape(tl.description || "")}">${Markdown.escape(tl.name)}</span>`
            ).join("") || `<span class="muted">${t("skills.noTools")}</span>`;
            card.innerHTML = `
                <div class="skill-head">
                    <span class="skill-name">${Markdown.escape(sk.name)}</span>
                    <span class="badge ${badgeCls}">${stLabel}</span>
                </div>
                <div class="skill-tools">${toolsHtml}</div>`;
            list.appendChild(card);
        }
    }
    async function refreshRagDocuments() {
        const listEl = $("#rag-documents");
        const status = $("#rag-status");
        try {
            const d = await (await api("/skills/rag/documents")).json();
            listEl.innerHTML = "";
            if (!d.documents || !d.documents.length) {
                listEl.innerHTML = `<li class="muted small">${t("rag.noDocs")}</li>`;
                status.textContent = t("rag.dropHere");
                return;
            }
            for (const doc of d.documents) {
                const li = document.createElement("li");
                li.setAttribute("data-testid", "rag-doc-" + doc.path);
                li.innerHTML = `<span class="doc-name">${Markdown.escape(doc.path)}</span>
                    <span class="doc-meta">${fmtBytes(doc.size_bytes)} · ${fmtDate(doc.modified_at).split(",")[0]}</span>`;
                listEl.appendChild(li);
            }
            status.textContent = d.count + " " + t("rag.docsCount") +
                (d.index ? " · " + t("rag.indexAge") + " " + fmtDate(d.index.modified_at) : "");
        } catch (e) {
            status.textContent = t("errors.generic");
        }
    }
    async function reindexRag() {
        const btn = $("#rag-reindex-btn");
        btn.disabled = true;
        const original = btn.textContent;
        btn.textContent = t("rag.reindexing");
        try {
            await api("/skills/rag/reindex", {method: "POST"});
            btn.textContent = t("rag.reindexed");
            await refreshRagDocuments();
        } catch (e) {
            btn.textContent = t("errors.generic");
        } finally {
            setTimeout(() => { btn.textContent = original; btn.disabled = false; }, 1200);
        }
    }

    // ── Modèles ─────────────────────────────────────────────────────────────
    async function refreshModels() {
        const list = $("#models-list"); list.innerHTML = "";
        try {
            const d = await (await api("/models/available")).json();
            for (const m of d.models) {
                const card = document.createElement("div");
                card.className = "model-card";
                card.dataset.modelId = m.id;
                card.setAttribute("data-testid", "model-" + m.id);
                if (m.id === state.activeModel) card.classList.add("active");
                card.innerHTML = `
                    <div class="model-title">${Markdown.escape(m.id)}</div>
                    <div class="model-meta">
                        <span>${t("models.size")}: ${fmtBytes(m.size_bytes)}</span>
                        ${m.parameters_hint ? `<span>${t("models.params")}: ${m.parameters_hint}</span>` : ""}
                        ${m.quantization ? `<span>${t("models.quant")}: ${m.quantization}</span>` : ""}
                        <span>${fmtDate(m.modified_at).split(",")[0]}</span>
                    </div>`;
                card.addEventListener("click", () => switchModel(m.id));
                list.appendChild(card);
            }
            if (!d.models.length) {
                list.innerHTML = `<div class="muted small">${t("skills.noTools")}</div>`;
            }
        } catch (e) {
            list.innerHTML = `<div class="muted small">${t("errors.network")}</div>`;
        }
    }
    async function switchModel(modelId) {
        if (modelId === state.activeModel) return;
        if (!confirm(t("models.confirmSwitch"))) return;
        const cards = $$("#models-list .model-card");
        cards.forEach(c => c.style.opacity = ".5");
        try {
            const r = await api("/models/switch", {
                method: "POST", body: JSON.stringify({model_id: modelId})
            });
            if (!r.ok) throw new Error("HTTP " + r.status);
            await refreshHealth();
            await refreshModels();
        } catch (e) {
            alert(t("models.switchFailed") + " : " + e.message);
        } finally {
            cards.forEach(c => c.style.opacity = "");
        }
    }

    // ── System prompt ───────────────────────────────────────────────────────
    async function refreshSystemPrompt() {
        const ta = $("#sp-textarea");
        const banner = $("#sp-locked-banner");
        try {
            const r = await api("/system-prompt");
            if (r.status === 403) {
                banner.classList.remove("hidden");
                ta.disabled = true;
                $("#sp-save").disabled = true; $("#sp-reset").disabled = true;
                ta.value = "";
                $("#sp-source").textContent = t("sysprompt.sourceLocked");
                state.systemPromptLocked = true;
                return;
            }
            state.systemPromptLocked = false;
            banner.classList.add("hidden");
            ta.disabled = false;
            $("#sp-save").disabled = false; $("#sp-reset").disabled = false;
            const d = await r.json();
            ta.value = d.content || "";
            $("#sp-source").textContent = d.source;
            $("#sp-token-count").textContent = d.token_count_approx;
            state.activePreset = d.active_preset;
            await refreshPresets();
        } catch (e) { console.error(e); }
    }
    async function refreshPresets() {
        if (state.systemPromptLocked) return;
        try {
            const d = await (await api("/system-prompt/presets")).json();
            const wrap = $("#sp-presets-buttons");
            wrap.innerHTML = "";
            for (const p of (d.presets || [])) {
                const b = document.createElement("button");
                b.type = "button"; b.className = "preset-btn";
                b.setAttribute("data-testid", "preset-" + p.id);
                if (p.id === state.activePreset) b.classList.add("active");
                b.textContent = p.id;
                b.title = p.preview || "";
                b.addEventListener("click", async () => {
                    await api("/system-prompt/activate/" + encodeURIComponent(p.id),
                              {method: "POST"});
                    await refreshSystemPrompt();
                });
                wrap.appendChild(b);
            }
        } catch (_) {}
    }
    async function saveSystemPrompt() {
        const btn = $("#sp-save");
        btn.disabled = true;
        const original = t("sysprompt.save");
        try {
            const r = await api("/system-prompt", {
                method: "PUT",
                body: JSON.stringify({content: $("#sp-textarea").value}),
            });
            if (!r.ok) throw new Error("HTTP " + r.status);
            btn.textContent = t("sysprompt.saved");
            setTimeout(() => { btn.textContent = original; }, 900);
            await refreshSystemPrompt();
        } catch (e) {
            alert(t("errors.saveFailed") + " : " + e.message);
        } finally { btn.disabled = false; }
    }
    async function resetSystemPrompt() {
        if (!confirm(t("sysprompt.confirmReset"))) return;
        await api("/system-prompt/reset", {method: "POST"});
        await refreshSystemPrompt();
    }
    function updateSPTokenCount() {
        const v = $("#sp-textarea").value || "";
        $("#sp-token-count").textContent = Math.max(1, Math.round(v.length / 3.8));
    }

    // ── Config ──────────────────────────────────────────────────────────────
    const CONFIG_FIELDS = [
        {key: "server.port",                   type: "number"},
        {key: "server.bind_host",              type: "text"},
        {key: "model.context_size",            type: "number"},
        {key: "model.gpu_layers",              type: "number", nullable: true},
        {key: "model.threads",                 type: "number", nullable: true},
        {key: "agentic.max_tool_rounds",       type: "number"},
        {key: "agentic.total_timeout",         type: "number"},
        {key: "agentic.allow_parallel_tools",  type: "checkbox"},
        {key: "server.cors.allow_credentials", type: "checkbox"},
        {key: "ui.default_language",           type: "select", options: ["fr", "en"]},
        {key: "ui.theme",                      type: "select", options: ["auto", "light", "dark"]},
    ];
    async function refreshConfig() {
        try {
            const cfg = await (await api("/config")).json();
            const form = $("#config-form"); form.innerHTML = "";
            for (const f of CONFIG_FIELDS) {
                const row = document.createElement("div");
                row.className = "config-row";
                const label = document.createElement("label");
                label.textContent = t("config.fields." + f.key);
                label.htmlFor = "cfg-" + f.key;
                let input;
                const val = f.key.split(".").reduce((o, k) => (o||{})[k], cfg);
                if (f.type === "checkbox") {
                    input = document.createElement("input");
                    input.type = "checkbox"; input.checked = !!val;
                    input.style.width = "auto";
                } else if (f.type === "select") {
                    input = document.createElement("select");
                    for (const o of f.options) {
                        const op = document.createElement("option");
                        op.value = o; op.textContent = o;
                        if (o === val) op.selected = true;
                        input.appendChild(op);
                    }
                } else {
                    input = document.createElement("input");
                    input.type = f.type;
                    input.value = val === null || val === undefined ? "" : val;
                    if (f.nullable) input.placeholder = "auto";
                }
                input.id = "cfg-" + f.key;
                input.dataset.field = f.key; input.dataset.ftype = f.type;
                if (f.nullable) input.dataset.nullable = "1";
                input.setAttribute("data-testid", "cfg-" + f.key);
                row.appendChild(label); row.appendChild(input);
                form.appendChild(row);
            }
        } catch (e) { console.error(e); }
    }
    async function saveConfig() {
        const btn = $("#config-save");
        const original = t("config.save");
        btn.disabled = true;
        try {
            const cfg = await (await api("/config")).json();
            for (const f of CONFIG_FIELDS) {
                const inp = $("#cfg-" + f.key);
                let v;
                if (f.type === "checkbox") v = inp.checked;
                else if (f.type === "number") {
                    if (inp.value === "" && f.nullable) v = null;
                    else v = Number(inp.value);
                } else v = inp.value;
                const parts = f.key.split(".");
                let cur = cfg;
                for (let i = 0; i < parts.length - 1; i++) {
                    cur[parts[i]] = cur[parts[i]] || {};
                    cur = cur[parts[i]];
                }
                cur[parts[parts.length - 1]] = v;
            }
            const r = await api("/config", {method: "PUT", body: JSON.stringify(cfg)});
            if (!r.ok) {
                const err = await r.json();
                throw new Error((err.detail && JSON.stringify(err.detail)) || ("HTTP " + r.status));
            }
            const body = await r.json();
            btn.textContent = t("config.saved");
            if (body.requires_restart && body.requires_restart.length) {
                $("#config-warn").classList.remove("hidden");
            }
            setTimeout(() => { btn.textContent = original; }, 900);
            await refreshHealth();
        } catch (e) {
            alert(t("errors.saveFailed") + " : " + e.message);
        } finally { btn.disabled = false; }
    }

    // ── Tabs ────────────────────────────────────────────────────────────────
    function setupTabs() {
        $$(".tab").forEach(tabBtn => {
            tabBtn.addEventListener("click", () => {
                const name = tabBtn.dataset.tab;
                $$(".tab").forEach(t => t.classList.toggle("active", t === tabBtn));
                $$(".tab-panel").forEach(p =>
                    p.classList.toggle("active", p.id === "panel-" + name));
                // Lazy refresh à l'ouverture
                if (name === "models") refreshModels();
                if (name === "config") refreshConfig();
                if (name === "systemprompt") refreshSystemPrompt();
            });
        });
    }

    // ── Bootstrap ───────────────────────────────────────────────────────────
    document.addEventListener("DOMContentLoaded", async () => {
        // Thème persistant (validation stricte + application immédiate).
        applyTheme(state.theme);
        $("#theme-toggle").addEventListener("click", () => {
            applyTheme(state.theme === "dark" ? "light" : "dark");
        });
        // Langue persistante (MÊME pattern que le thème : validation stricte
        // au boot, écriture localStorage uniquement au change réel).
        const langSelect = $("#lang-select");
        langSelect.value = state.lang;
        langSelect.addEventListener("change", async e => {
            const chosen = validLang(e.target.value) || "fr";
            applyLang(chosen);
            await loadI18n(chosen);
            renderConversationList();
            renderPlatformInfo(); renderPerfInfo();
            if (state.skills) renderSkills();
            await refreshRagDocuments();
            await refreshModels();
        });
        // Alignement du <html lang="…"> dès le boot sans réécrire localStorage.
        document.documentElement.lang = state.lang;
        // Chargement STRICTEMENT bloquant du dictionnaire i18n de la langue
        // active AVANT tout premier rendu de texte. On retire la classe
        // `booting` (qui masque l'UI) seulement une fois applyI18n effectué.
        // try/finally garantit que l'UI apparaît même en cas d'échec réseau.
        try {
            await loadI18n(state.lang);
        } finally {
            document.body.classList.remove("booting");
        }

        // Conversations
        $("#conversation-new-btn").addEventListener("click", createConversation);
        $("#chat-rename-btn").addEventListener("click", renameConversation);
        $("#chat-delete-btn").addEventListener("click", deleteConversation);

        // Chat
        $("#chat-form").addEventListener("submit", e => {
            e.preventDefault();
            const inp = $("#chat-input");
            const text = inp.value.trim();
            if (!text) return;
            inp.value = "";
            sendMessage(text);
        });
        $("#chat-input").addEventListener("keydown", e => {
            if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                $("#chat-form").dispatchEvent(new Event("submit", {cancelable: true}));
            }
        });

        // Right panel
        setupTabs();
        $("#rag-reindex-btn").addEventListener("click", reindexRag);
        $("#models-refresh-btn").addEventListener("click", refreshModels);
        $("#sp-textarea").addEventListener("input", updateSPTokenCount);
        $("#sp-save").addEventListener("click", saveSystemPrompt);
        $("#sp-reset").addEventListener("click", resetSystemPrompt);
        $("#config-save").addEventListener("click", saveConfig);
        $("#config-reload").addEventListener("click", refreshConfig);

        // Chargements initiaux (parallèles)
        await Promise.all([
            refreshHealth(),
            refreshConversations(),
            refreshSkills(),
            refreshRagDocuments(),
            refreshSystemPrompt(),
        ]);
        renderMessagesFromHistory([]);
        setInterval(refreshHealth, 5000);
    });
})();
