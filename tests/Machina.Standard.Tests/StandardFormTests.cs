using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Diagnostics;
using Machina.Core.Lowering;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Compilation;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
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
