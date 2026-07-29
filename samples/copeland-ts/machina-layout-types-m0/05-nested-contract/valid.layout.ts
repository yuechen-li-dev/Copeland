layout type PageShell {
    row root {
        column main {
            slot hero;
            grid features;
            slot footer;
        }
    }
}
layout Page<0px, 0px> satisfies PageShell {
    width: 1200px;
    height: 800px;
    row root {
        column main { width: fill; height: fill;
            slot hero { height: 500px; }
            grid features { columns: 4; height: fill; }
            slot footer { height: 40px; }
        }
    }
}
