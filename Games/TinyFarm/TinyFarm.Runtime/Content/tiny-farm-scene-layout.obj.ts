const $schema: string = "copeland://tiny-farm/content/m7/layout";

// Integer values are tile coordinates and extents. Stable IDs, not row positions, bind layout to objects.
record table SceneLayout {
    sceneId: string = [
        "overworld", "overworld", "overworld", "overworld",
        "farm", "farm", "farm", "farm", "farm",
        "town", "town", "town", "town",
        "general-store", "general-store", "general-store",
        "riverside", "riverside", "riverside"
    ];
    objectId: string = [
        "farm-entrance", "town-entrance", "riverside-entrance", "hill",
        "farm-exit", "farmhouse", "plot-1", "plot-2", "fence",
        "town-exit", "store-entrance", "well", "market-stall",
        "store-exit", "shop-counter", "shelves",
        "riverside-exit", "river", "reeds"
    ];
    x: number = [2, 11, 19, 7, 17, 1, 7, 9, 12, 10, 17, 9, 3, 5, 4, 1, 1, 10, 8];
    y: number = [7, 5, 9, 2, 6, 1, 5, 5, 2, 13, 4, 6, 3, 7, 2, 1, 5, 0, 3];
    width: number = [1, 1, 1, 3, 1, 4, 1, 1, 1, 1, 1, 2, 3, 1, 3, 1, 1, 6, 1];
    height: number = [1, 1, 1, 2, 1, 3, 1, 1, 5, 1, 1, 2, 2, 1, 1, 4, 1, 10, 3];
    layer: number = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
}

const $value = SceneLayout;
