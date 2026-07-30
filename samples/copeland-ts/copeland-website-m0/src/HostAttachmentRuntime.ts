import { MountReactRenderer, UnmountReactRenderer, UpdateReactRenderer } from "./ReactRendererAdapter";

// This is the browser realization of the neutral attachment boundary. The
// plan identity is Copeland-owned. The React root is an opaque adapter-private
// value, so it deliberately does not appear in a Copeland record.
export function MountHostAttachment(
    attachmentId: string,
    componentInstanceId: string,
    host: ReactMountElement,
    payload: ReactNode): ReactRoot {
    const root: ReactRoot = MountReactRenderer(host);
    UpdateReactRenderer(root, payload);
    return root;
}

export function UpdateHostAttachment(attachmentId: string, root: ReactRoot, payload: ReactNode): void {
    UpdateReactRenderer(root, payload);
}

export function UnmountHostAttachment(attachmentId: string, root: ReactRoot): void {
    UnmountReactRenderer(root);
}
