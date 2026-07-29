layout type TwoColumnShell {
    row root { column left; column right; }
}
layout WrongKind<0px, 0px> satisfies TwoColumnShell {
    width: 1200px;
    height: 800px;
    row root {
        column left { width: 256px; height: fill; }
        row right { height: fill; }
    }
}
