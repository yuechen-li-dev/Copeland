internal static class Trellis2MaterialWeights
{
    public const string FlowName = "slat_flow_imgshape2tex_dit_1_3B_512_bf16.safetensors";
    public const string FlowHash = "8371aa1c5d13be79dcd5ddfd2cf3835e902e204dc34427169a1c702828e1a94d";
    public const long FlowBytes = 2584672728;
    public const string DecoderName = "tex_dec_next_dc_f16c32_fp16.safetensors";
    public const string DecoderHash = "97ea69addea2ecd9312910f5f548234665eef51c088386180b7cd5b258645e3c";
    public const long DecoderBytes = 948458812;

    public static async Task Fetch(string directory)
    {
        Directory.CreateDirectory(directory);
        const string root = "https://huggingface.co/microsoft/TRELLIS.2-4B/resolve/af44b45f2e35a493886929c6d786e563ec68364d/ckpts/";
        await Trellis2OccupancyWeights.Download(directory, FlowName, FlowBytes, FlowHash, root + FlowName);
        await Trellis2OccupancyWeights.Download(directory, DecoderName, DecoderBytes, DecoderHash, root + DecoderName);
    }
}
