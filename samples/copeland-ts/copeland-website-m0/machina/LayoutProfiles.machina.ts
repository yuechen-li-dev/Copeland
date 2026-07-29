// Three intentionally separate page compositions. React supplies the semantic
// elements and shared content; this source supplies their resolved geometry.

const Canvas = {
    surface: { fill: "#05060d" },
    text: { color: "#f7f7ff" }
};

const SidebarSurface = Canvas with {
    surface: { fill: "#080914" },
    border: { width: 1px, color: "#273152", style: "solid" }
};

const HeaderSurface = Canvas with {
    surface: { fill: "#070913" },
    border: { width: 1px, color: "#273152", style: "solid" }
};

const HeroSurface = Canvas with {
    surface: { fill: "#080a16", radius: 24px },
    border: { width: 1px, color: "#4d7cfe", style: "solid" }
};

const StripSurface = Canvas with {
    surface: { fill: "#080814", radius: 999px },
    border: { width: 1px, color: "#9b5cff", style: "solid" }
};

const CardSurface = Canvas with {
    surface: { fill: "#101631", radius: 16px },
    border: { width: 1px, color: "#273152", style: "solid" }
};

function DesktopLayout(): View {
    return Root([
        HStack(
            [
                VStack([], { main: Fixed(256px), cross: Fill(), gap: 0px, style: SidebarSurface }),
                VStack(
                    [
                        VStack([], { main: Fixed(520px), cross: Fill(), gap: 0px, style: HeroSurface }),
                        VStack([], { main: Fixed(56px), cross: Fill(), gap: 0px, style: StripSurface }),
                        HStack(
                            [
                                VStack([], { main: Fill(), cross: Fill(), gap: 0px, style: CardSurface }),
                                VStack([], { main: Fill(), cross: Fill(), gap: 0px, style: CardSurface }),
                                VStack([], { main: Fill(), cross: Fill(), gap: 0px, style: CardSurface }),
                                VStack([], { main: Fill(), cross: Fill(), gap: 0px, style: CardSurface })
                            ],
                            { main: Fill(), cross: Fill(), gap: 16px }
                        ),
                        VStack([], { main: Fixed(44px), cross: Fill(), gap: 0px, style: Canvas })
                    ],
                    { main: Fill(), cross: Fill(), gap: 18px }
                )
            ],
            {
                frame: Absolute({ x: 0px, y: 0px, width: 1440px, height: 900px }),
                gap: 18px,
                style: Canvas
            }
        )
    ]);
}

function TabletLayout(): View {
    return Root([
        VStack(
            [
                VStack([], { main: Fixed(72px), cross: Fill(), gap: 0px, style: HeaderSurface }),
                VStack([], { main: Fixed(470px), cross: Fill(), gap: 0px, style: HeroSurface }),
                VStack([], { main: Fixed(60px), cross: Fill(), gap: 0px, style: StripSurface }),
                VStack(
                    [
                        HStack(
                            [
                                VStack([], { main: Fill(), cross: Fill(), gap: 0px, style: CardSurface }),
                                VStack([], { main: Fill(), cross: Fill(), gap: 0px, style: CardSurface })
                            ],
                            { main: Fill(), cross: Fill(), gap: 16px }
                        ),
                        HStack(
                            [
                                VStack([], { main: Fill(), cross: Fill(), gap: 0px, style: CardSurface }),
                                VStack([], { main: Fill(), cross: Fill(), gap: 0px, style: CardSurface })
                            ],
                            { main: Fill(), cross: Fill(), gap: 16px }
                        )
                    ],
                    { main: Fill(), cross: Fill(), gap: 16px }
                ),
                VStack([], { main: Fixed(44px), cross: Fill(), gap: 0px, style: Canvas })
            ],
            {
                frame: Absolute({ x: 0px, y: 0px, width: 768px, height: 1024px }),
                gap: 18px,
                style: Canvas
            }
        )
    ]);
}

function MobileLayout(): View {
    return Root([
        VStack(
            [
                VStack([], { main: Fixed(72px), cross: Fill(), gap: 0px, style: HeaderSurface }),
                VStack([], { main: Fixed(410px), cross: Fill(), gap: 0px, style: HeroSurface }),
                VStack([], { main: Fixed(140px), cross: Fill(), gap: 0px, style: Canvas }),
                VStack([], { main: Fixed(150px), cross: Fill(), gap: 0px, style: Canvas }),
                VStack([], { main: Fixed(54px), cross: Fill(), gap: 0px, style: StripSurface }),
                VStack(
                    [
                        VStack([], { main: Fill(), cross: Fill(), gap: 0px, style: CardSurface }),
                        VStack([], { main: Fill(), cross: Fill(), gap: 0px, style: CardSurface }),
                        VStack([], { main: Fill(), cross: Fill(), gap: 0px, style: CardSurface }),
                        VStack([], { main: Fill(), cross: Fill(), gap: 0px, style: CardSurface })
                    ],
                    { main: Fill(), cross: Fill(), gap: 14px }
                ),
                VStack([], { main: Fixed(44px), cross: Fill(), gap: 0px, style: Canvas })
            ],
            {
                frame: Absolute({ x: 0px, y: 0px, width: 390px, height: 1604px }),
                gap: 14px,
                style: Canvas
            }
        )
    ]);
}
