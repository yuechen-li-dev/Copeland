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
            slot list { height: fit; }
            slot detail { height: fit; }
        }
    } else {
        row root {
            slot list { width: sidebarWidth; height: fit; }
            slot detail { width: fill; height: fit; }
        }
    }
}
