using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Diagnostics;
using Machina.Core.Flat;
using Machina.Core.Lowering;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Layout.Compilation;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class StandardFormTests
{
    [Fact]
    public void LabelLowersDeterministically()
    {
        var first = UiLowerer.Lower(StandardUI.Label("Username", id: "username-label"));
        var second = UiLowerer.Lower(StandardUI.Label("Username", id: "username-label"));
        var firstSnapshot = UiLoweringSnapshotWriter.Write(first);
        var secondSnapshot = UiLoweringSnapshotWriter.Write(second);
        var labelId = new NodeId("username-label");

        Assert.Equal(firstSnapshot, secondSnapshot);
        Assert.Contains("username-label", firstSnapshot);
        Assert.Contains("Username", firstSnapshot);
        Assert.Equal(UiRole.Label, first.Semantics[labelId].Role);
        Assert.Equal("Username", first.Semantics[labelId].Label);
    }

    [Fact]
    public void InputShellDisplaysValueAndCompilesThroughLayout()
    {
        var ui = StandardUI.Input(
            id: "username",
            value: "ada",
            placeholder: "Enter username");

        var lowered = UiLowerer.Lower(ui);
        var snapshot = UiLoweringSnapshotWriter.Write(lowered);
        var inputId = new NodeId("username");
        var document = LayoutCompiler.CompileLayoutRows(lowered.Rows);

        Assert.NotNull(document);
        Assert.Contains("username", snapshot);
        Assert.Contains("ada", snapshot);
        Assert.DoesNotContain("Enter username", snapshot);
        Assert.Equal(UiRole.Input, lowered.Semantics[inputId].Role);
        Assert.True(lowered.Semantics[inputId].Focusable);
    }

    [Fact]
    public void InputShellDisplaysPlaceholderWithMutedStyleWhenValueEmpty()
    {
        var ui = StandardUI.Input(
            id: "username",
            value: "",
            placeholder: "Enter username");

        var lowered = UiLowerer.Lower(ui);
        var snapshot = UiLoweringSnapshotWriter.Write(lowered);

        Assert.Contains("Enter username", snapshot);
        Assert.Contains("#71717AFF", snapshot);
    }

    [Fact]
    public void StandardInput_ExplicitStyleOverridesShellContentAndText()
    {
        var style = StandardTheme.Default.Input.Default with
        {
            Background = ColorToken.Hex(0x102030FF),
            Foreground = ColorToken.Hex(0xF0E0D0FF),
            BorderColor = ColorToken.Hex(0x556677FF),
            BorderThickness = 2,
            Width = 220,
            Height = 34,
            ContentInset = 12,
            TextStyle = StandardTheme.Default.Input.Default.TextStyle with
            {
                Size = TextSize.Sm,
                AlignX = TextAlignX.Left,
                AlignY = TextAlignY.Center,
            },
        };

        var lowered = UiLowerer.Lower(StandardUI.Input(id: "username", value: "Ada", style: style));
        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(
            LayoutCompiler.CompileLayoutRows(lowered.Rows),
            new Rect(0, 0, 420, 120));
        var snapshot = UiLoweringSnapshotWriter.Write(lowered);

        var shellRect = resolved.Nodes[new NodeId("username")].Rect;
        var contentRect = resolved.Nodes[new NodeId("username.content")].Rect;
        var textRect = resolved.Nodes[new NodeId("username.text")].Rect;
        var shellStyle = lowered.Styles[new NodeId("username")];
        var textStyle = lowered.TextStyles[new NodeId("username.text")];

        Assert.Equal(style.Background, shellStyle.Background);
        Assert.Equal(style.Foreground, shellStyle.Foreground);
        Assert.Equal(style.BorderColor, shellStyle.BorderColor);
        Assert.Equal(style.BorderThickness, shellStyle.BorderThickness);
        Assert.Equal(shellRect.X + style.ContentInset, contentRect.X);
        Assert.Equal(shellRect.Y + style.ContentInset, contentRect.Y);
        Assert.Equal(shellRect.Width - (style.ContentInset * 2), contentRect.Width);
        Assert.Equal(shellRect.Height - (style.ContentInset * 2), contentRect.Height);
        Assert.True(textRect.X >= contentRect.X);
        Assert.True(textRect.Y >= contentRect.Y);
        Assert.Equal(style.TextStyle.Size, textStyle.Size);
        Assert.Equal(style.TextStyle.AlignX, textStyle.AlignX);
        Assert.Equal(style.TextStyle.AlignY, textStyle.AlignY);
        Assert.Equal(0, shellStyle.Padding);
        Assert.Contains("Ada", snapshot);
    }

    [Fact]
    public void StandardInput_PlaceholderUsesPlaceholderTextStyle()
    {
        var style = StandardTheme.Default.Input.Default with
        {
            ContentInset = 10,
            PlaceholderTextStyle = StandardTheme.Default.Input.Default.PlaceholderTextStyle with
            {
                Color = ColorToken.Hex(0xA1B2C3FF),
                Size = TextSize.Sm,
            },
        };

        var lowered = UiLowerer.Lower(StandardUI.Input(id: "email", value: "", placeholder: "Email", style: style));
        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(
            LayoutCompiler.CompileLayoutRows(lowered.Rows),
            new Rect(0, 0, 320, 120));

        var contentRect = resolved.Nodes[new NodeId("email.content")].Rect;
        var textRect = resolved.Nodes[new NodeId("email.text")].Rect;
        var textStyle = lowered.TextStyles[new NodeId("email.text")];

        Assert.Contains("Email", UiLoweringSnapshotWriter.Write(lowered));
        Assert.Equal(style.PlaceholderTextStyle.Color, textStyle.Color);
        Assert.Equal(style.PlaceholderTextStyle.Size, textStyle.Size);
        Assert.True(textRect.X >= contentRect.X);
        Assert.True(textRect.Y >= contentRect.Y);
        Assert.Equal(resolved.Nodes[new NodeId("email")].Rect.X + style.ContentInset, contentRect.X);
    }

    [Fact]
    public void StandardInput_DefaultStyleMatchesThemeDefault()
    {
        var lowered = UiLowerer.Lower(StandardUI.Input(id: "username", value: "Ada"));
        var defaultStyle = StandardTheme.Default.Input.Default;
        var shellStyle = lowered.Styles[new NodeId("username")];
        var textStyle = lowered.TextStyles[new NodeId("username.text")];

        Assert.Equal(defaultStyle.Background, shellStyle.Background);
        Assert.Equal(defaultStyle.Foreground, shellStyle.Foreground);
        Assert.Equal(defaultStyle.BorderColor, shellStyle.BorderColor);
        Assert.Equal(defaultStyle.BorderThickness, shellStyle.BorderThickness);
        Assert.Equal(defaultStyle.TextStyle.Size, textStyle.Size);
        Assert.Equal(defaultStyle.TextStyle.AlignX, textStyle.AlignX);
        Assert.Equal(defaultStyle.TextStyle.AlignY, textStyle.AlignY);
    }


    [Fact]
    public void DisabledInputOmitsAction()
    {
        var ui = StandardUI.Input(
            id: "username",
            value: "ada",
            disabled: true,
            changed: UiAction.Named("username.changed"));

        var lowered = UiLowerer.Lower(ui);
        var inputId = new NodeId("username");

        Assert.Equal(UiRole.Input, lowered.Semantics[inputId].Role);
        Assert.True(lowered.Semantics[inputId].Disabled);
        Assert.False(lowered.Semantics[inputId].Focusable);
        Assert.Empty(lowered.Actions);
    }

    [Fact]
    public void FieldComposesLabelControlDescriptionAndErrorDeterministically()
    {
        var ui = StandardUI.Field(
            id: "username-field",
            label: "Username",
            control: StandardUI.Input(id: "username", placeholder: "Enter username"),
            description: "Used for login.",
            error: "Required");

        var first = UiLowerer.Lower(ui);
        var second = UiLowerer.Lower(ui);
        var firstSnapshot = UiLoweringSnapshotWriter.Write(first);
        var secondSnapshot = UiLoweringSnapshotWriter.Write(second);
        var document = LayoutCompiler.CompileLayoutRows(first.Rows);

        Assert.Equal(firstSnapshot, secondSnapshot);
        Assert.NotNull(document);
        Assert.Contains("username-field", firstSnapshot);
        Assert.Contains("username-field.label", firstSnapshot);
        Assert.Contains("Username", firstSnapshot);
        Assert.Contains("username", firstSnapshot);
        Assert.Contains("Used for login.", firstSnapshot);
        Assert.Contains("Required", firstSnapshot);
    }

    [Fact]
    public void CheckboxCheckedWithLabelEmitsMarkerSemanticsAndAction()
    {
        var ui = StandardUI.Checkbox(
            id: "accept",
            label: "Accept terms",
            isChecked: true,
            changed: UiAction.Named("accept.changed"));

        var lowered = UiLowerer.Lower(ui);
        var snapshot = UiLoweringSnapshotWriter.Write(lowered);
        var checkboxId = new NodeId("accept");

        Assert.Contains("accept", snapshot);
        Assert.Contains("Accept terms", snapshot);
        Assert.Contains("accept.mark", snapshot, StringComparison.Ordinal);
        Assert.Equal(UiRole.Checkbox, lowered.Semantics[checkboxId].Role);
        Assert.Equal("Accept terms", lowered.Semantics[checkboxId].Label);
        Assert.Equal("accept.changed", lowered.Actions[checkboxId].Name);
    }

    [Fact]
    public void DisabledCheckboxOmitsAction()
    {
        var ui = StandardUI.Checkbox(
            id: "accept",
            label: "Accept terms",
            disabled: true,
            changed: UiAction.Named("accept.changed"));

        var lowered = UiLowerer.Lower(ui);
        var checkboxId = new NodeId("accept");

        Assert.True(lowered.Semantics[checkboxId].Disabled);
        Assert.False(lowered.Semantics[checkboxId].Focusable);
        Assert.Empty(lowered.Actions);
    }

    [Fact]
    public void SwitchOnAndOffSnapshotsDifferDeterministically()
    {
        var onSwitch = StandardUI.Switch(
            id: "notifications",
            label: "Notifications",
            isOn: true,
            changed: UiAction.Named("notifications.changed"));

        var offSwitch = StandardUI.Switch(
            id: "notifications",
            label: "Notifications",
            isOn: false,
            changed: UiAction.Named("notifications.changed"));

        var firstOnSnapshot = UiLoweringSnapshotWriter.Write(UiLowerer.Lower(onSwitch));
        var secondOnSnapshot = UiLoweringSnapshotWriter.Write(UiLowerer.Lower(onSwitch));
        var firstOffSnapshot = UiLoweringSnapshotWriter.Write(UiLowerer.Lower(offSwitch));
        var secondOffSnapshot = UiLoweringSnapshotWriter.Write(UiLowerer.Lower(offSwitch));

        Assert.Equal(firstOnSnapshot, secondOnSnapshot);
        Assert.Equal(firstOffSnapshot, secondOffSnapshot);
        Assert.NotEqual(firstOnSnapshot, firstOffSnapshot);
        Assert.Contains("#18181BFF", firstOnSnapshot);
        Assert.Contains("#F4F4F5FF", firstOffSnapshot);
        Assert.Contains("notifications.changed", firstOnSnapshot);
    }

    [Fact]
    public void DisabledSwitchOmitsAction()
    {
        var ui = StandardUI.Switch(
            id: "notifications",
            label: "Notifications",
            disabled: true,
            changed: UiAction.Named("notifications.changed"));

        var lowered = UiLowerer.Lower(ui);
        var switchId = new NodeId("notifications");

        Assert.Equal(UiRole.Switch, lowered.Semantics[switchId].Role);
        Assert.True(lowered.Semantics[switchId].Disabled);
        Assert.False(lowered.Semantics[switchId].Focusable);
        Assert.Empty(lowered.Actions);
    }

    [Fact]
    public void StandardFormSampleLowersDeterministicallyAndCompilesThroughLayout()
    {
        var first = UiLowerer.Lower(CreateStandardFormSample());
        var second = UiLowerer.Lower(CreateStandardFormSample());
        var firstSnapshot = UiLoweringSnapshotWriter.Write(first);
        var secondSnapshot = UiLoweringSnapshotWriter.Write(second);
        var document = LayoutCompiler.CompileLayoutRows(first.Rows);

        Assert.Equal(firstSnapshot, secondSnapshot);
        Assert.NotNull(document);
        Assert.Contains("settings-card", firstSnapshot);
        Assert.Contains("settings-content", firstSnapshot);
        Assert.Contains("Username", firstSnapshot);
        Assert.Contains("ada", firstSnapshot);
        Assert.Contains("Email updates", firstSnapshot);
        Assert.Contains("Notifications", firstSnapshot);
        Assert.Contains("email-updates.changed", firstSnapshot);
        Assert.Contains("notifications.changed", firstSnapshot);
        Assert.Contains("save => save", firstSnapshot);
    }

    private static Machina.Core.Nodes.UiNode CreateStandardFormSample()
    {
        return StandardUI.Card(
            id: "settings-card",
            child: UI.Column(
                id: "settings-content",
                gap: 12,
                children:
                [
                    UI.Text("Settings", id: "title", size: TextSize.H1),

                    StandardUI.Field(
                        id: "username-field",
                        label: "Username",
                        control: StandardUI.Input(
                            id: "username",
                            value: "ada",
                            placeholder: "Enter username"),
                        description: "This appears in your profile."),

                    StandardUI.Checkbox(
                        id: "email-updates",
                        label: "Email updates",
                        isChecked: true,
                        changed: UiAction.Named("email-updates.changed")),

                    StandardUI.Switch(
                        id: "notifications",
                        label: "Notifications",
                        isOn: false,
                        changed: UiAction.Named("notifications.changed")),

                    StandardUI.Separator(id: "rule"),

                    StandardUI.Button(
                        "Save",
                        id: "save",
                        action: UiAction.Named("save")),
                ]));
    }
}
