export template<static gap: Length = 12px, static padding: Length = 0px>
layout Reading<0px, 0px> {
    width: fill;
    height: fit;

    column root {
        gap: gap;
        padding: padding;
        slot heading { height: fit; }
        slot body { height: fit; }
        slot notes { height: fit; }
    }
}

export enum Arrangement {
    Horizontal,
    Vertical,
}

export template<static arrangement: Arrangement = Arrangement.Horizontal, static sidebarWidth: Length = 300px>
layout TwoPane<0px, 0px> {
    width: fill;
    height: fit;

    static if (match arrangement {
        Horizontal => false,
        Vertical => true,
    }) {
        column root {
            column listSurface { height: fit; slot list { height: fit; } }
            column detailSurface { height: fit; slot detail { height: fit; } }
        }
    } else {
        row root {
            column listSurface { width: sidebarWidth; height: fit; slot list { height: fit; } }
            column detailSurface { width: fill; height: fit; slot detail { height: fit; } }
        }
    }
}
