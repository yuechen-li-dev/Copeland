namespace Machina.Presenter.Sample;

public sealed record PresenterProgramOptions(
    bool ExportOnly,
    string OutputPath,
    PresenterProofOptions ProofOptions)
{
    public static PresenterProgramOptions Parse(IReadOnlyList<string> args)
    {
        bool exportOnly = false;
        string outputPath = PresenterExportContract.DefaultOutputPath;
        bool includeDirectOutlineRenderBridgeProof = false;

        for (int index = 0; index < args.Count; index++)
        {
            string arg = args[index];

            if (arg == "--export-only")
            {
                exportOnly = true;
                continue;
            }

            if (arg == "--output-path" && index + 1 < args.Count)
            {
                outputPath = args[++index];
                continue;
            }

            if (arg == "--include-direct-outline-render-bridge-proof")
            {
                includeDirectOutlineRenderBridgeProof = true;
            }
        }

        return new PresenterProgramOptions(
            exportOnly,
            outputPath,
            new PresenterProofOptions(includeDirectOutlineRenderBridgeProof));
    }
}
