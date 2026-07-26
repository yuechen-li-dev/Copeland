enum SettingsEvent {
    Save,
    ToggleDarkMode,
}

const ButtonBase = {
    surface: { fill: "#182238", radius: 8px },
    text: { color: "#ffffff", weight: 600 },
    border: { width: 1px, color: "#334155", style: "solid" }
};

const PrimaryButton = ButtonBase with {
    surface: { fill: "#2563eb" }
};

function SettingsPanel(): View {
    return VStack(
        [
            Text("Status: ready", {
                main: Fixed(40px),
                cross: Fill(),
                offset: { x: 0.25ui - 2px },
                wrap: TextWrap.Word
            }),
            Button("Save", SettingsEvent.Save, {
                main: Fill(),
                cross: Fill(),
                style: PrimaryButton
            }),
            Toggle(false, SettingsEvent.ToggleDarkMode, {
                main: Fixed(20px),
                cross: Fill()
            })
        ],
        {
            frame: Anchor({ left: 24px, right: 24px, top: 20px, bottom: 20px }),
            gap: 16px
        }
    );
}

function SettingsPage(): View {
    return Root([SettingsPanel()]);
}
