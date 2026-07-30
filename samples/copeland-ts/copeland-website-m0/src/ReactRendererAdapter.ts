import { createRoot } from "react-dom/client";

// This application-level adapter owns React root identity and lifecycle.
// Copeland callers retain component identity and compiler-owned host geometry.
export function MountReactRenderer(host: ReactMountElement): ReactRoot {
    return createRoot(host);
}

export function UpdateReactRenderer(root: ReactRoot, content: ReactNode): void {
    root.render(content);
}

export function UnmountReactRenderer(root: ReactRoot): void {
    root.unmount();
}
