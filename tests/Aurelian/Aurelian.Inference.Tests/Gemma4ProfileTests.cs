using Aurelian.Inference.Gemma4;
using Xunit;

namespace Aurelian.Inference.Tests;

public sealed class Gemma4ProfileTests
{
    [Fact]
    public void GlobalAndSlidingLayersHaveExplicitDifferentHeadContracts()
    {
        var contracts = Gemma4TextModel.Requirements().ToDictionary(item => item.Name);
        Assert.Equal(new[] { 262144, 3840 }, contracts[Gemma4TextModel.Embeddings].Shape);
        Assert.Equal(new[] { 4096, 3840 }, contracts[Gemma4TextModel.Prefix + "layers.0.self_attn.q_proj.weight"].Shape);
        Assert.Equal(new[] { 8192, 3840 }, contracts[Gemma4TextModel.Prefix + "layers.5.self_attn.q_proj.weight"].Shape);
        Assert.Equal(new[] { 512, 3840 }, contracts[Gemma4TextModel.Prefix + "layers.5.self_attn.k_proj.weight"].Shape);
        Assert.DoesNotContain(Gemma4TextModel.Prefix + "layers.5.self_attn.v_proj.weight", contracts.Keys);
    }

    [Fact]
    public void ChatParsingRetainsThinkingSeparatelyAndRecognizesTruncation()
    {
        Assert.Equal("<bos><|turn>user\nHello<turn|>\n<|turn>model\n", Gemma4Chat.FormatUser(" Hello "));
        Assert.Equal(new Gemma4ChatResponse("Hello!", "", false),
            Gemma4Chat.ParseResponse("<|channel>thought\n<channel|>Hello!<turn|>"));
        Assert.Equal(new Gemma4ChatResponse("4", "Calculate", false),
            Gemma4Chat.ParseResponse("<|channel>thought\nCalculate<channel|>4<eos>"));
        Assert.Equal(new Gemma4ChatResponse("", "Calculate", true),
            Gemma4Chat.ParseResponse("<|channel>thought\nCalculate"));
        Assert.Equal(new Gemma4ChatResponse("", "", true), Gemma4Chat.ParseResponse("<|channel>"));
    }
}
