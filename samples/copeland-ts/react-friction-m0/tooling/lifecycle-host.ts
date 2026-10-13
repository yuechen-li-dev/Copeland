interface HostObservation {
    kind: "search" | "save" | "focus";
    value: string;
    id?: string;
}
const observations: HostObservation[] = [];
Object.defineProperty(globalThis, Symbol.for("copeland.lifecycle-demo.host"), { value: observations });

async function request(path: string, parameters: Record<string, string>): Promise<string> {
    const response = await fetch(`/lifecycle-api/${path}?${new URLSearchParams(parameters)}`);
    const body = await response.json() as { ok: boolean; value?: string; error?: string };
    if (!response.ok || !body.ok || typeof body.value !== "string") {
        throw body.error ?? "Malformed service response";
    }
    return body.value;
}

export function search(query: string): Promise<string> {
    observations.push({ kind: "search", value: query });
    return request("search", { query });
}

export function saveDraft(id: string, draft: string): Promise<string> {
    observations.push({ kind: "save", id, value: draft });
    return request("save", { id, draft });
}

export function focus(id: string): void {
    observations.push({ kind: "focus", value: id });
    const control = document.getElementById(id);
    if (control instanceof HTMLElement) control.focus();
}
