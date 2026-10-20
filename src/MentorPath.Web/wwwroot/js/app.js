window.mentorPath = {
    copyText: async function (text) {
        try { await navigator.clipboard.writeText(text); return true; } catch { return false; }
    },
    // Posts the practical submission form (multipart) to the dedicated endpoint with the antiforgery token (ARCHITECTURE §6).
    submitPractical: async function (assignmentId, fileInputId, payload) {
        const form = new FormData();
        form.append("clientRequestId", payload.clientRequestId);
        form.append("rationaleMarkdown", payload.rationaleMarkdown ?? "");
        form.append("confidenceBefore", String(payload.confidenceBefore ?? 0));
        form.append("externalUrl", payload.externalUrl ?? "");
        const input = document.getElementById(fileInputId);
        if (input && input.files && input.files.length > 0) {
            form.append("file", input.files[0]);
        }
        const tokenResp = await fetch("/api/antiforgery-token", { credentials: "same-origin" });
        if (!tokenResp.ok) return { status: tokenResp.status, body: null };
        const token = await tokenResp.json();
        const resp = await fetch(`/api/assignments/${assignmentId}/practical-submissions`, {
            method: "POST",
            body: form,
            credentials: "same-origin",
            headers: { [token.headerName]: token.requestToken }
        });
        let body = null;
        try { body = await resp.text(); } catch { }
        return { status: resp.status, body: body };
    },
    clearFile: function (fileInputId) {
        const input = document.getElementById(fileInputId);
        if (input) input.value = "";
    }
};

// App shell behaviour. The layout is static SSR, so this is plain DOM code driven by data attributes (no Blazor events).
(function () {
    const root = document.documentElement;
    const mobile = window.matchMedia("(max-width: 991.98px)");
    let lastTrigger = null;

    const rail = () => document.getElementById("app-rail");
    const frameParts = () => Array.from(document.querySelectorAll(".app-topbar, .app-main"));
    const openers = () => document.querySelectorAll("[data-drawer-open]");

    function setDrawer(open) {
        const el = rail();
        if (!el) return;
        const isOpen = root.dataset.drawer === "open";
        if (open === isOpen) return;
        if (open) {
            lastTrigger = document.activeElement;
            root.dataset.drawer = "open";
            frameParts().forEach(p => p.setAttribute("inert", ""));
            openers().forEach(b => b.setAttribute("aria-expanded", "true"));
            const first = el.querySelector(".rail-close") || el.querySelector("a");
            requestAnimationFrame(() => first && first.focus());
        } else {
            delete root.dataset.drawer;
            frameParts().forEach(p => p.removeAttribute("inert"));
            openers().forEach(b => b.setAttribute("aria-expanded", "false"));
            if (lastTrigger && lastTrigger.isConnected) lastTrigger.focus();
            lastTrigger = null;
        }
    }

    function setRail(collapsed) {
        if (collapsed) root.dataset.rail = "collapsed"; else delete root.dataset.rail;
        try { localStorage.setItem("mp-rail", collapsed ? "collapsed" : "expanded"); } catch { }
        syncRailButton();
    }

    function syncRailButton() {
        const collapsed = root.dataset.rail === "collapsed";
        document.querySelectorAll("[data-rail-toggle]").forEach(b => {
            b.setAttribute("aria-label", collapsed ? "Rozwiń menu boczne" : "Zwiń menu boczne");
        });
    }

    function markCurrent() {
        document.querySelectorAll(".rail-link").forEach(a => {
            if (a.classList.contains("active")) a.setAttribute("aria-current", "page"); else a.removeAttribute("aria-current");
        });
    }

    function closeMenus(except) {
        document.querySelectorAll("details[data-menu][open]").forEach(d => { if (d !== except) d.removeAttribute("open"); });
    }

    function afterNavigation() {
        let collapsed = false;
        try { collapsed = localStorage.getItem("mp-rail") === "collapsed"; } catch { }
        if (collapsed) root.dataset.rail = "collapsed"; else delete root.dataset.rail;
        delete root.dataset.drawer;
        frameParts().forEach(p => p.removeAttribute("inert"));
        closeMenus();
        syncRailButton();
        markCurrent();
    }

    document.addEventListener("click", function (e) {
        const target = e.target instanceof Element ? e.target : null;
        if (!target) return;

        if (target.closest("[data-drawer-open]")) { setDrawer(true); return; }
        if (target.closest("[data-drawer-close]")) { setDrawer(false); return; }
        if (target.closest("[data-rail-toggle]")) { setRail(root.dataset.rail !== "collapsed"); return; }

        const link = target.closest("#app-rail a");
        if (link && root.dataset.drawer === "open") setDrawer(false);

        const menu = target.closest("details[data-menu]");
        if (menu && target.closest("a")) { menu.removeAttribute("open"); return; }
        closeMenus(menu);
    });

    document.addEventListener("keydown", function (e) {
        if (e.key !== "Escape") return;
        if (root.dataset.drawer === "open") { setDrawer(false); return; }
        const open = document.querySelector("details[data-menu][open]");
        if (open) { open.removeAttribute("open"); const s = open.querySelector("summary"); if (s) s.focus(); }
    });

    // Keep Tab inside the drawer while it is open (the rest of the page is inert).
    document.addEventListener("focusin", function (e) {
        const el = rail();
        if (root.dataset.drawer === "open" && el && !el.contains(e.target)) {
            const first = el.querySelector(".rail-close") || el.querySelector("a");
            if (first) first.focus();
        }
    });

    mobile.addEventListener("change", function () { if (!mobile.matches) setDrawer(false); });

    if (window.Blazor && typeof Blazor.addEventListener === "function") {
        Blazor.addEventListener("enhancednavigationend", afterNavigation);
    } else {
        document.addEventListener("DOMContentLoaded", function () {
            if (window.Blazor && typeof Blazor.addEventListener === "function") Blazor.addEventListener("enhancednavigationend", afterNavigation);
        });
    }
    document.addEventListener("DOMContentLoaded", function () { syncRailButton(); markCurrent(); });
})();
