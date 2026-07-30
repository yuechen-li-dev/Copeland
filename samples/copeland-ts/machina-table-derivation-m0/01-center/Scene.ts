layout DialogScene<0px, 0px> {
    width: 1280px;
    height: 720px;
    overlay root {
        width: 1280px;
        height: 720px;
        slot page { x: 0px; y: 0px; width: 1280px; height: 720px; }
        slot dialog { width: 480px; height: 320px; } with centerIn(root);
        slot tooltip { width: 180px; height: 48px; }
            with placeAbove(dialog, 8px)
            with alignRight(dialog);
        slot halo { } with expandFrom(dialog, 16px);
    }
}
