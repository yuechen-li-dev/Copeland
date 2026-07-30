stream DialogScene<0px, 0px> {
    width: 1280px;
    height: 720px;
    csv overlay root {
        name, content, width, height, derivations;
        page, Page(), 1280px, 720px, [];
        dialog, Dialog(), 480px, 320px, [centerIn(root)];
        tooltip, Tooltip(), 180px, 48px, [placeAbove(dialog, 8px), alignRight(dialog)];
        halo, Halo(), derived, derived, [expandFrom(dialog, 16px)];
    }
}
