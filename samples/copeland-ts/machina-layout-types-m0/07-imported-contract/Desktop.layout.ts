import { DesktopShell as Shell } from "./Shell.layout-type";
export layout Desktop<0px, 0px> satisfies Shell {
    width: 1200px;
    height: 800px;
    row root {
        column sidebar { width: 256px; height: fill; }
        column main { width: fill; height: fill; }
    }
}
