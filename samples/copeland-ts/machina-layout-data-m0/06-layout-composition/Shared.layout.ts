layout SharedHero<4ui, 2ui> {
    width: 640px;
    height: 320px;
    column hero {
        gap: 20px;
        slot announcement { height: 32px; }
        slot title { height: 120px; }
        slot commands { height: fill; }
    }
}

layout DesktopHero<20px, 10px> = SharedHero with { width: 960px; };
