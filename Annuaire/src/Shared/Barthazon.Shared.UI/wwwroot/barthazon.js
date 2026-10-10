// Fonctions navigateur communes à toutes les applications Barthazon (aucune dépendance externe).
window.barthazon = (function () {
    let shortcutsRegistered = false;

    function isTyping(target) {
        if (!target) return false;
        const tag = (target.tagName || "").toLowerCase();
        return tag === "input" || tag === "textarea" || tag === "select" || target.isContentEditable;
    }

    return {
        setTheme: function (mode) {
            document.cookie = "bz-theme=" + mode + "; path=/; max-age=31536000; samesite=lax";
        },
        prefersDark: function () {
            return !!(window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches);
        },
        registerShortcuts: function () {
            if (shortcutsRegistered) return;
            shortcutsRegistered = true;
            document.addEventListener("keydown", function (e) {
                const ctrlK = (e.ctrlKey || e.metaKey) && (e.key === "k" || e.key === "K");
                const slash = e.key === "/" && !isTyping(e.target) && !e.ctrlKey && !e.metaKey && !e.altKey;
                if (ctrlK || slash) {
                    const input = document.getElementById("bz-global-search");
                    if (input) {
                        e.preventDefault();
                        input.focus();
                        if (input.select) input.select();
                    }
                }
            });
        },
        downloadFromStream: async function (fileName, streamReference) {
            const buffer = await streamReference.arrayBuffer();
            const url = URL.createObjectURL(new Blob([buffer]));
            const link = document.createElement("a");
            link.href = url;
            link.download = fileName || "";
            document.body.appendChild(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(url);
        },
        print: function () {
            window.print();
        },
        focus: function (id) {
            const el = document.getElementById(id);
            if (el) el.focus();
        },
        storageGet: function (key) {
            try { return window.localStorage.getItem(key); } catch { return null; }
        },
        storageSet: function (key, value) {
            try {
                if (value === null || value === undefined) window.localStorage.removeItem(key);
                else window.localStorage.setItem(key, value);
            } catch { /* stockage indisponible */ }
        }
    };
})();
