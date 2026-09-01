const $schema: string = "copeland://oblivion/m20e/validation-evidence";

enum Risk {
    Semantic,
    Ui,
    Integration,
}

record Evidence {
    owner: string;
    expected: string;
}

record table ValidationEvidence {
    order: number = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
    lane: string = ["obj-ts-load", "canonical-load", "root-contract", "projection", "formatter", "empty-shape", "cli", "reload", "single", "vertical-split", "horizontal-split", "dark", "light", "playback", "boundaries", "diff-check"];
    subsystem: string = ["App", "App", "App", "UI", "UI", "UI", "CLI", "App", "Standalone", "Standalone", "Standalone", "Standalone", "Standalone", "Integration", "Architecture", "Repository"];
    required: boolean = [true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true];
    risk: Risk = [Risk.Semantic, Risk.Semantic, Risk.Semantic, Risk.Semantic, Risk.Semantic, Risk.Semantic, Risk.Integration, Risk.Integration, Risk.Ui, Risk.Ui, Risk.Ui, Risk.Ui, Risk.Ui, Risk.Integration, Risk.Semantic, Risk.Integration];
    proofs: string[] = [["unit"], ["unit", "equivalence"], ["diagnostic"], ["unit", "structure"], ["unit"], ["unit"], ["cli", "json"], ["transaction"], ["geometry"], ["geometry", "capture"], ["geometry"], ["capture"], ["capture"], ["playback"], ["rg"], ["git"]];
    evidence: Evidence = [
        { owner: "Oblivion.App", expected: "authoring profile loads" },
        { owner: "Oblivion.App", expected: "canonical bytes load" },
        { owner: "Oblivion.App", expected: "non-table rejected" },
        { owner: "Oblivion.UI", expected: "column order retained" },
        { owner: "Oblivion.UI", expected: "all cell kinds readable" },
        { owner: "Oblivion.UI", expected: "zero one shapes render" },
        { owner: "Oblivion.Cli", expected: "metadata only" },
        { owner: "Oblivion.App", expected: "failure atomic" },
        { owner: "Standalone", expected: "slot filled" },
        { owner: "Standalone", expected: "half height usable" },
        { owner: "Standalone", expected: "wide slot scrolls" },
        { owner: "Standalone", expected: "contrast passes" },
        { owner: "Standalone", expected: "contrast passes" },
        { owner: "Presenter", expected: "14 of 14" },
        { owner: "Architecture", expected: "no row copy" },
        { owner: "Repository", expected: "clean whitespace" },
    ];
}

const $value = ValidationEvidence;
