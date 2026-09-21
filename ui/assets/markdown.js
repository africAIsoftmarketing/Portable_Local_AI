/* AfricAIsoft Portable Studio — parseur Markdown minimaliste vendorié.
 * Auteur  : AfricAIsoft
 * Licence : MIT
 * Rôle    : rend un sous-ensemble courant de Markdown sans dépendance externe
 *           (offline strict). Ne remplace pas marked/showdown mais suffit
 *           pour les réponses LLM : titres 1-3, gras, italique, code inline,
 *           blocs de code, listes ul/ol, liens, paragraphes.
 *           Toutes les entrées sont d'abord échappées HTML (défense XSS).
 */
(function (root) {
    "use strict";

    function escapeHtml(s) {
        return s.replace(/[&<>"']/g, function (c) {
            return {"&":"&amp;","<":"&lt;",">":"&gt;","\"":"&quot;","'":"&#39;"}[c];
        });
    }

    function render(src) {
        if (!src) return "";
        let text = escapeHtml(src);

        // Blocs de code fenced ```lang\n...\n```
        text = text.replace(/```([a-zA-Z0-9_-]*)\n([\s\S]*?)```/g,
            function (_, lang, code) {
                const cls = lang ? ' class="lang-' + lang + '"' : "";
                return "<pre><code" + cls + ">" + code.replace(/\n$/,'') + "</code></pre>";
            });

        // Traite ligne par ligne pour titres et listes.
        const lines = text.split("\n");
        const out = [];
        let inUl = false, inOl = false, buffer = [];

        function flushBuffer() {
            if (buffer.length) {
                out.push("<p>" + inline(buffer.join(" ")) + "</p>");
                buffer = [];
            }
        }
        function closeLists() {
            if (inUl) { out.push("</ul>"); inUl = false; }
            if (inOl) { out.push("</ol>"); inOl = false; }
        }

        for (let i = 0; i < lines.length; i++) {
            const raw = lines[i];
            // Passer les blocs de code déjà transformés en tags HTML
            if (raw.indexOf("<pre>") !== -1 || raw.indexOf("</pre>") !== -1
                || raw.indexOf("<code") !== -1 || raw.indexOf("</code>") !== -1) {
                flushBuffer(); closeLists();
                out.push(raw); continue;
            }
            const line = raw.trimRight();
            if (!line.trim()) { flushBuffer(); closeLists(); continue; }
            // Titres
            let m = line.match(/^(#{1,3})\s+(.*)$/);
            if (m) {
                flushBuffer(); closeLists();
                out.push("<h" + m[1].length + ">" + inline(m[2]) + "</h" + m[1].length + ">");
                continue;
            }
            // Liste ordonnée
            m = line.match(/^(\d+)\.\s+(.*)$/);
            if (m) {
                flushBuffer();
                if (inUl) { out.push("</ul>"); inUl = false; }
                if (!inOl) { out.push("<ol>"); inOl = true; }
                out.push("<li>" + inline(m[2]) + "</li>");
                continue;
            }
            // Liste non ordonnée
            m = line.match(/^[-*+]\s+(.*)$/);
            if (m) {
                flushBuffer();
                if (inOl) { out.push("</ol>"); inOl = false; }
                if (!inUl) { out.push("<ul>"); inUl = true; }
                out.push("<li>" + inline(m[1]) + "</li>");
                continue;
            }
            // Paragraphe (accumule)
            closeLists();
            buffer.push(line);
        }
        flushBuffer(); closeLists();
        return out.join("\n");
    }

    function inline(s) {
        // Gras: **texte**
        s = s.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
        // Italique: *texte* ou _texte_
        s = s.replace(/(^|[^*])\*([^*\n]+)\*/g, "$1<em>$2</em>");
        s = s.replace(/(^|[^_])_([^_\n]+)_/g, "$1<em>$2</em>");
        // Code inline: `texte`
        s = s.replace(/`([^`]+)`/g, "<code>$1</code>");
        // Liens: [texte](url)
        s = s.replace(/\[([^\]]+)\]\(([^)\s]+)\)/g,
            function (_, txt, url) {
                if (/^javascript:/i.test(url)) return txt;
                return '<a href="' + url + '" target="_blank" rel="noopener">' + txt + '</a>';
            });
        return s;
    }

    root.Markdown = { render: render, escape: escapeHtml };
}(window));
