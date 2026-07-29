layout DesktopLayout<0px, 0px> {
    width: 1440px;
    height: 900px;
    row root {
        column sidebar { width: 256px; height: fill; }
        column main {
            width: fill;
            height: fill;
            slot hero { height: 520px; }
            grid features { columns: 4; height: fill; }
            slot footer { height: 44px; }
        }
    }
}
