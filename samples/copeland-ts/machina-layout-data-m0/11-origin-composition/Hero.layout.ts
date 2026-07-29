layout SharedHero<4ui, 2ui> {
    width: 640px;
    height: 320px;
    slot hero { frame: { x: 0px, y: 0px, width: 640px, height: 320px }; }
}

layout DesktopHero<20px, 10px> = SharedHero with {
    width: 960px;
    height: 520px;
};
