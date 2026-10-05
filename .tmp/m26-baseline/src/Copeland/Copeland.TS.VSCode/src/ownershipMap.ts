export type CopelandOwner = "tsc" | "tscl";

export interface OwnershipFileEntry {
    path: string;
    owner: CopelandOwner;
    project: string;
    matchedRule: string;
}

export interface EditorOwnershipMetadata {
    schemaVersion: number;
    workspaceRoot: string;
    files: OwnershipFileEntry[];
}

export class OwnershipMap {
    private readonly entries: Map<string, OwnershipFileEntry>;

    public constructor(metadata: EditorOwnershipMetadata) {
        if (metadata.schemaVersion !== 1 || !Array.isArray(metadata.files)) {
            throw new Error("Unsupported Copeland editor ownership metadata. Run tscl workspace sync.");
        }

        this.entries = new Map<string, OwnershipFileEntry>();
        for (const entry of metadata.files) {
            if (entry.owner !== "tsc" && entry.owner !== "tscl" || !entry.path || !entry.project) {
                throw new Error("Invalid Copeland editor ownership metadata. Run tscl workspace sync.");
            }

            this.entries.set(OwnershipMap.normalizePath(entry.path), entry);
        }
    }

    public get(relativePath: string): OwnershipFileEntry | undefined {
        return this.entries.get(OwnershipMap.normalizePath(relativePath));
    }

    public entriesFor(owner: CopelandOwner): OwnershipFileEntry[] {
        return [...this.entries.values()].filter((entry) => entry.owner === owner);
    }

    public static normalizePath(value: string): string {
        return value.replace(/\\/g, "/").replace(/^\.\//, "").toLocaleLowerCase();
    }
}
