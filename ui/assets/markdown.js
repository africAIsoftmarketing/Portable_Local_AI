/* AfricAIsoft Portable Studio — parseur Markdown minimaliste vendorié.
 * Auteur  : AfricAIsoft
 * Licence : MIT
 * Version : 1.1.0 (2026-09-25)
 * Rôle    : rend un sous-ensemble courant de Markdown sans dépendance externe
 *           (offline strict). Ne remplace pas marked/showdown mais suffit
 *           pour les réponses LLM : titres 1-6, gras, italique, code inline,
 *           blocs de code (``` et ~~~), JSON nu, tableaux, citations,
 *           listes ul/ol, liens, règles horizontales, paragraphes.
 *           Tout texte est échappé HTML avant insertion (défense XSS).
 *
 * 1.1.0 — CORRECTIF rendu du code :
 *   - Les blocs de code sont extraits AVANT le traitement ligne à ligne et
 *     remplacés par des jetons, puis réinjectés à la fin. Avant, seules la
 *     1re et la dernière ligne d'un bloc restaient dans <pre> ; les lignes
 *     du milieu étaient fusionnées en paragraphe (code « aplati »).
 *   - Bloc non encore fermé (streaming) : rendu comme code jusqu'à la fin.
 *   - Fins de ligne CRLF, clôtures indentées, langages « c++ », « c# ».
 *   - JSON nu (sans ```) sur ses propres lignes : détecté, validé par
 *     JSON.parse et rendu indenté en bloc json.
 *   - Code inline protégé : `a_b_c` n'est plus mis en italique.
 *   - Bouton « Copier » sur chaque bloc (délégation gérée par app.js).
 */
(function (root) {
    "use strict";

    const TOKEN = "\u0000MDBLK";
    const labels = { copy: "Copier" };

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, function (c) {
            return {"&":"&amp;","<":"&lt;",">":"&gt;","\"":"&quot;","'":"&#39;"}[c];
        });
    }

    function codeBlockHtml(code, lang) {
        const safeLang = (lang || "").replace(/[^A-Za-z0-9_+#.-]/g, "").slice(0, 24);
        const cls = safeLang ? ' class="lang-' + escapeHtml(safeLang) + '"' : "";
        return '<div class="code-block">' +
               '<div class="code-head"><span class="code-lang">' +
               escapeHtml(safeLang || "code") + '</span>' +
               '<button type="button" class="code-copy" data-md-copy>' +
               escapeHtml(labels.copy) + '</button></div>' +
               "<pre><code" + cls + ">" + escapeHtml(code) + "</code></pre></div>";
    }

    // ── Passe 1 : extraction des blocs de code (sur le texte BRUT) ──────────
    function extractBlocks(lines, store) {
        const out = [];
        const budget = { parses: 300 };   // borne le coût du JSON nu (rendu à chaque token)
        const openRe = /^ {0,3}(`{3,}|~{3,})\s*([^\s`~]*)[^`]*$/;
        let i = 0;
        while (i < lines.length) {
            const m = lines[i].match(openRe);
            if (m) {
                const fence = m[1], ch = fence[0], lang = m[2];
                const closeRe = new RegExp("^ {0,3}\\" + ch + "{" + fence.length + ",}\\s*$");
                const body = [];
                let j = i + 1;
                while (j < lines.length && !closeRe.test(lines[j])) { body.push(lines[j]); j++; }
                // j === lines.length → bloc non fermé (streaming) : tout est du code.
                store.push(codeBlockHtml(body.join("\n"), lang));
                out.push(TOKEN + (store.length - 1) + "\u0000");
                i = j + 1;
                continue;
            }
            // JSON nu : une ligne qui commence par { ou [ et un bloc qui se parse.
            const t = lines[i].trim();
            if (t[0] === "{" || t[0] === "[") {
                const got = budget.parses > 0 ? tryJsonRun(lines, i, budget) : null;
                if (got) {
                    store.push(codeBlockHtml(got.pretty, "json"));
                    out.push(TOKEN + (store.length - 1) + "\u0000");
                    i = got.next;
                    continue;
                }
            }
            out.push(lines[i]);
            i++;
        }
        return out;
    }

    function tryJsonRun(lines, start, budget) {
        let acc = "";
        const max = Math.min(lines.length, start + 200);
        for (let j = start; j < max; j++) {
            if (j > start && !lines[j].trim()) break;     // ligne vide = fin
            acc += (acc ? "\n" : "") + lines[j];
            const last = lines[j].trim();
            if (last.endsWith("}") || last.endsWith("]")) {
                if (--budget.parses < 0) return null;
                try {
                    const v = JSON.parse(acc);
                    if (v !== null && typeof v === "object") {
                        return { pretty: JSON.stringify(v, null, 2), next: j + 1 };
                    }
                } catch (_) { /* pas encore complet */ }
            }
        }
        return null;
    }

    // ── Passe 2 : blocs Markdown ligne à ligne ──────────────────────────────
    function render(src) {
        if (!src) return "";
        const store = [];
        const lines = extractBlocks(String(src).replace(/\r\n?/g, "\n").split("\n"), store);

        const out = [];
        let inUl = false, inOl = false, buffer = [], quote = [];

        function flushBuffer() {
            if (buffer.length) {
                out.push("<p>" + buffer.map(inline).join("<br>") + "</p>");
                buffer = [];
            }
        }
        function flushQuote() {
            if (quote.length) {
                out.push("<blockquote>" + quote.map(inline).join("<br>") + "</blockquote>");
                quote = [];
            }
        }
        function closeLists() {
            if (inUl) { out.push("</ul>"); inUl = false; }
            if (inOl) { out.push("</ol>"); inOl = false; }
        }
        function closeAll() { flushBuffer(); flushQuote(); closeLists(); }

        for (let i = 0; i < lines.length; i++) {
            const line = lines[i].replace(/\s+$/, "");

            // Jeton de bloc de code : réinjecté tel quel à la fin.
            if (line.indexOf(TOKEN) === 0) { closeAll(); out.push(line); continue; }

            if (!line.trim()) { closeAll(); continue; }

            // Tableau GFM : ligne d'en-tête | ... | suivie de |---|---|
            if (line.indexOf("|") !== -1 && i + 1 < lines.length &&
                /^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$/.test(lines[i + 1])) {
                closeAll();
                const head = splitRow(line);
                const rows = [];
                let j = i + 2;
                while (j < lines.length && lines[j].indexOf("|") !== -1 && lines[j].trim()
                       && lines[j].indexOf(TOKEN) !== 0) {
                    rows.push(splitRow(lines[j])); j++;
                }
                let h = '<div class="table-wrap"><table><thead><tr>' +
                        head.map(c => "<th>" + inline(c) + "</th>").join("") +
                        "</tr></thead><tbody>";
                for (const r of rows) {
                    h += "<tr>" + head.map((_, k) => "<td>" + inline(r[k] || "") + "</td>").join("") + "</tr>";
                }
                out.push(h + "</tbody></table></div>");
                i = j - 1;
                continue;
            }

            let m = line.match(/^ {0,3}(#{1,6})\s+(.*?)\s*#*\s*$/);
            if (m) {
                closeAll();
                const n = m[1].length;
                out.push("<h" + n + ">" + inline(m[2]) + "</h" + n + ">");
                continue;
            }
            if (/^ {0,3}([-*_])(\s*\1){2,}\s*$/.test(line)) { closeAll(); out.push("<hr>"); continue; }

            m = line.match(/^ {0,3}>\s?(.*)$/);
            if (m) { flushBuffer(); closeLists(); quote.push(m[1]); continue; }
            flushQuote();

            m = line.match(/^\s*(\d+)[.)]\s+(.*)$/);
            if (m) {
                flushBuffer();
                if (inUl) { out.push("</ul>"); inUl = false; }
                if (!inOl) { out.push("<ol>"); inOl = true; }
                out.push("<li>" + inline(m[2]) + "</li>");
                continue;
            }
            m = line.match(/^\s*[-*+]\s+(.*)$/);
            if (m) {
                flushBuffer();
                if (inOl) { out.push("</ol>"); inOl = false; }
                if (!inUl) { out.push("<ul>"); inUl = true; }
                out.push("<li>" + inline(m[1]) + "</li>");
                continue;
            }
            closeLists();
            buffer.push(line);
        }
        closeAll();

        const tokRe = new RegExp(TOKEN + "(\\d+)\u0000", "g");
        return out.join("\n").replace(tokRe, function (_, n) { return store[+n] || ""; });
    }

    function splitRow(line) {
        let s = line.trim();
        if (s[0] === "|") s = s.slice(1);
        if (s[s.length - 1] === "|") s = s.slice(0, -1);
        return s.split("|").map(c => c.trim());
    }

    // ── Inline : le code inline est protégé des autres règles ───────────────
    function inline(raw) {
        const codes = [];
        let s = String(raw).replace(/(`+)([^`]|[^`][\s\S]*?[^`])\1(?!`)/g, function (_, t, c) {
            codes.push("<code>" + escapeHtml(c.trim()) + "</code>");
            return "\u0001" + (codes.length - 1) + "\u0001";
        });
        s = escapeHtml(s);
        s = s.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
        s = s.replace(/__([^_]+)__/g, "<strong>$1</strong>");
        s = s.replace(/(^|[^*\w])\*([^*\n]+)\*(?!\w)/g, "$1<em>$2</em>");
        s = s.replace(/(^|[^_\w])_([^_\n]+)_(?!\w)/g, "$1<em>$2</em>");
        s = s.replace(/~~([^~]+)~~/g, "<del>$1</del>");
        s = s.replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, function (_, txt, url) {
            if (!/^(https?:|mailto:|#|\/|\.\/)/i.test(url)) return txt;   // liste blanche
            return '<a href="' + url + '" target="_blank" rel="noopener">' + txt + '</a>';
        });
        return s.replace(/\u0001(\d+)\u0001/g, function (_, n) { return codes[+n]; });
    }

    function setLabels(l) { Object.assign(labels, l || {}); }

    root.Markdown = { render: render, escape: escapeHtml, setLabels: setLabels };
}(window));
