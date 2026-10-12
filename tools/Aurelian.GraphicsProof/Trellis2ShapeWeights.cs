internal static class Trellis2ShapeWeights
{
    public const string FlowName = "slat_flow_img2shape_dit_1_3B_512_bf16.safetensors";
    public const string FlowHash = "ec5e0917ef9b7e25ad51dffc7d19687a42019871f94239f2fa7f86264c55b70f";
    public const long FlowBytes = 2584574424;
    public const string DecoderName = "shape_dec_next_dc_f16c32_fp16.safetensors";
    public const string DecoderHash = "e3b718d3e43e4f8780e9a24ac6fff231811a67e3b058e336e10fe654c911d581";
    public const long DecoderBytes = 948490494;
    public const string PipelineHash = "222c359ab1ed9bc6735a640a34f95d47f8681b9bc4aaa101bfb80274676253c6";

    public static async Task Fetch(string directory)
    {
        Directory.CreateDirectory(directory);
        const string root = "https://huggingface.co/microsoft/TRELLIS.2-4B/resolve/af44b45f2e35a493886929c6d786e563ec68364d/ckpts/";
        await Trellis2OccupancyWeights.Download(directory, FlowName, FlowBytes, FlowHash, root + FlowName);
        await Trellis2OccupancyWeights.Download(directory, DecoderName, DecoderBytes, DecoderHash, root + DecoderName);
        await Trellis2OccupancyWeights.Download(directory, "pipeline.json", 4186, PipelineHash,
            "https://huggingface.co/microsoft/TRELLIS.2-4B/resolve/af44b45f2e35a493886929c6d786e563ec68364d/pipeline.json");
    }
}
