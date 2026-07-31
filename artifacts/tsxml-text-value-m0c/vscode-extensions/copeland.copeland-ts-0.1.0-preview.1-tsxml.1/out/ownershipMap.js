"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.OwnershipMap = void 0;
class OwnershipMap {
    entries;
    constructor(metadata) {
        if (metadata.schemaVersion !== 1 || !Array.isArray(metadata.files)) {
            throw new Error("Unsupported Copeland editor ownership metadata. Run tscl workspace sync.");
        }
        this.entries = new Map();
        for (const entry of metadata.files) {
            if (entry.owner !== "tsc" && entry.owner !== "tscl" || !entry.path || !entry.project) {
                throw new Error("Invalid Copeland editor ownership metadata. Run tscl workspace sync.");
            }
            this.entries.set(OwnershipMap.normalizePath(entry.path), entry);
        }
    }
    get(relativePath) {
        return this.entries.get(OwnershipMap.normalizePath(relativePath));
    }
    entriesFor(owner) {
        return [...this.entries.values()].filter((entry) => entry.owner === owner);
    }
    static normalizePath(value) {
        return value.replace(/\\/g, "/").replace(/^\.\//, "").toLocaleLowerCase();
    }
}
exports.OwnershipMap = OwnershipMap;
//# sourceMappingURL=ownershipMap.js.map