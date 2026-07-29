enum WebsiteEvent {
    CopyPrimary,
    CopySecondary,
}

const HeroPanel = {
    surface: { fill: "#080a16", radius: 20px },
    border: { width: 1px, color: "#4d7cfe", style: "solid" }
};

const HeroTitle = {
    text: { color: "#f7f7ff", size: 62px, weight: 800, lineHeight: 0.99 }
};

const HeroAccent = {
    text: { color: "#e982ff", size: 44px, weight: 800, lineHeight: 1.05 }
};

const HeroCopy = {
    text: { color: "#aab0c8", size: 18px, weight: 500, lineHeight: 1.55 }
};

const CommandBase = {
    surface: { fill: "#05060e", radius: 12px },
    text: { color: "#f2efff", size: 17px, weight: 650 },
    border: { width: 1px, color: "#8996d3", style: "solid" }
};

const PrimaryCommand = CommandBase with {
    border: { color: "#f04dd8" }
};

function HeroLayout(): View {
    return Root([
        VStack(
            [
                Text("AI-native TypeScript for the next ChatGPT.", {
                    main: Fixed(125px),
                    cross: Fill(),
                    style: HeroTitle,
                    wrap: TextWrap.Word
                }),
                Text("the next ChatGPT is still ChatGPT.", {
                    main: Fixed(56px),
                    cross: Fill(),
                    style: HeroAccent,
                    wrap: TextWrap.Word
                }),
                Text("Copeland TS unifies React, .NET, npm, templates, and typed browser-to-CLR workflows—so AI writes less glue code and more product.", {
                    main: Fixed(90px),
                    cross: Fill(),
                    style: HeroCopy,
                    wrap: TextWrap.Word
                }),
                HStack(
                    [
                        Button("dotnet new copeland-react", WebsiteEvent.CopyPrimary, {
                            main: Fill(),
                            cross: Fill(),
                            style: PrimaryCommand
                        }),
                        Button("tscl build • tspack run", WebsiteEvent.CopySecondary, {
                            main: Fill(),
                            cross: Fill(),
                            style: CommandBase
                        })
                    ],
                    {
                        main: Fixed(58px),
                        cross: Fill(),
                        gap: 12px
                    }
                )
            ],
            {
                frame: Anchor({ left: 52px, right: 52px, top: 88px, bottom: 68px }),
                gap: 10px,
                style: HeroPanel
            }
        )
    ]);
}
