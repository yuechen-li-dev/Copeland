namespace Aurelian.Core.Engine.Graphics;

public static class AurelianPreparedGraphicsSubsystemValidation
{
    public static AurelianPreparedGraphicsSubsystemResult Validate(
        AurelianPreparedGraphicsSubsystem? subsystem)
    {
        if (subsystem is null)
        {
            return Rejected([
                Error(
                    AurelianPreparedGraphicsSubsystemDiagnosticCodes.OptionsMissing,
                    "Aurelian prepared graphics subsystem options are required.")]);
        }

        List<AurelianPreparedGraphicsSubsystemDiagnostic> diagnostics = [];

        if (subsystem.Options is null)
        {
            diagnostics.Add(Error(
                AurelianPreparedGraphicsSubsystemDiagnosticCodes.OptionsMissing,
                "Aurelian prepared graphics subsystem options are required."));
            return Rejected(diagnostics);
        }

        if (subsystem.Options.Ownership != AurelianEngineGraphicsOwnership.External)
        {
            diagnostics.Add(Error(
                AurelianPreparedGraphicsSubsystemDiagnosticCodes.UnsupportedOwnership,
                $"Aurelian prepared graphics subsystem ownership '{subsystem.Options.Ownership}' is not supported by M0."));
        }

        switch (subsystem.Options.Mode)
        {
            case AurelianEngineGraphicsMode.Headless:
                if (subsystem.PresentationMechanism is not null)
                {
                    diagnostics.Add(new AurelianPreparedGraphicsSubsystemDiagnostic(
                        AurelianPreparedGraphicsSubsystemDiagnosticCodes.HeadlessIgnoresPresentationMechanism,
                        AurelianPreparedGraphicsSubsystemDiagnosticSeverity.Warning,
                        "Aurelian headless graphics mode ignores the supplied presentation mechanism."));
                }
                break;

            case AurelianEngineGraphicsMode.PreparedVisible:
                if (subsystem.CompositorMechanism is null)
                {
                    diagnostics.Add(Error(
                        AurelianPreparedGraphicsSubsystemDiagnosticCodes.PreparedVisibleRequiresCompositorMechanism,
                        "Aurelian prepared visible graphics mode requires a prepared compositor mechanism."));
                }

                if (subsystem.PresentationMechanism is null)
                {
                    diagnostics.Add(Error(
                        AurelianPreparedGraphicsSubsystemDiagnosticCodes.PreparedVisibleRequiresPresentationMechanism,
                        "Aurelian prepared visible graphics mode requires a prepared presentation mechanism."));
                }
                break;

            default:
                diagnostics.Add(Error(
                    AurelianPreparedGraphicsSubsystemDiagnosticCodes.OptionsMissing,
                    $"Aurelian prepared graphics subsystem mode '{subsystem.Options.Mode}' is not supported by M0."));
                break;
        }

        return diagnostics.Any(static diagnostic => diagnostic.Severity == AurelianPreparedGraphicsSubsystemDiagnosticSeverity.Error)
            ? Rejected(diagnostics)
            : new AurelianPreparedGraphicsSubsystemResult(AurelianPreparedGraphicsSubsystemStatus.Valid, diagnostics);
    }

    private static AurelianPreparedGraphicsSubsystemResult Rejected(
        IReadOnlyList<AurelianPreparedGraphicsSubsystemDiagnostic> diagnostics) =>
        new(AurelianPreparedGraphicsSubsystemStatus.Rejected, diagnostics);

    private static AurelianPreparedGraphicsSubsystemDiagnostic Error(string code, string message) =>
        new(code, AurelianPreparedGraphicsSubsystemDiagnosticSeverity.Error, message);
}
