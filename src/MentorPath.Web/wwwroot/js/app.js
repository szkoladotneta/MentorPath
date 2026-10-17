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
