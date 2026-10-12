using System.Diagnostics;
using Aurelian.Graphics.Vulkan.ComputeMath;

namespace Aurelian.Inference;

public interface ICausalTextModel
{
    int ContextCapacity { get; }
    void Reset();
    GpuTensor Evaluate(IReadOnlyList<int> tokens);
    (int Token, float Score) GreedyToken(GpuTensor hidden);
}

public sealed record GeneratedToken(int Id, float Score, double ElapsedMilliseconds);
public sealed record TextGenerationResult(IReadOnlyList<GeneratedToken> Tokens, bool EndTokenReached,
    double PrefillMilliseconds, double TimeToFirstTokenMilliseconds, double TotalMilliseconds);

/// <summary>Single-sequence greedy generation over an explicit model and scheduler.
/// Applications own prompt formatting, decoding and streaming presentation.</summary>
public sealed class CausalTextGenerator(ICausalTextModel model, IComputeSchedule schedule)
{
    public TextGenerationResult Generate(IReadOnlyList<int> prompt, int maximumTokens,
        IReadOnlySet<int> endTokens, Action<GeneratedToken>? observe = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(endTokens);
        if (prompt.Count < 1 || maximumTokens < 1 || (long)prompt.Count + maximumTokens > model.ContextCapacity || endTokens.Count == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTokens), "Prompt and generated tokens must fit the explicit context capacity.");
        }
        model.Reset();
        var timer = Stopwatch.StartNew();
        var tokens = new List<GeneratedToken>();
        double prefillMilliseconds = 0;
        bool stopped = false;
        try
        {
            schedule.Run(Stages(), cancellationToken);
        }
        catch
        {
            model.Reset();
            throw;
        }
        if (tokens.Count == 0)
        {
            model.Reset();
            throw new InvalidOperationException("AUR-CAUSAL-001: Scheduler completed without evaluating the model.");
        }
        return new(tokens.AsReadOnly(), stopped, prefillMilliseconds, tokens[0].ElapsedMilliseconds, timer.Elapsed.TotalMilliseconds);

        IEnumerable<ComputeStage> Stages()
        {
            GpuTensor? hidden = null;
            try
            {
                yield return new("prefill", () =>
                {
                    hidden = model.Evaluate(prompt);
                    prefillMilliseconds = timer.Elapsed.TotalMilliseconds;
                });
                for (int step = 0; step < maximumTokens; step++)
                {
                    yield return new("predict." + step, () =>
                    {
                        var prediction = model.GreedyToken(hidden!);
                        hidden!.Dispose();
                        hidden = null;
                        var token = new GeneratedToken(prediction.Token, prediction.Score, timer.Elapsed.TotalMilliseconds);
                        tokens.Add(token);
                        stopped = endTokens.Contains(token.Id);
                        observe?.Invoke(token);
                    });
                    if (stopped || tokens.Count == maximumTokens) { break; }
                    yield return new("decode." + step, () => hidden = model.Evaluate([tokens[^1].Id]));
                }
            }
            finally
            {
                hidden?.Dispose();
            }
        }
    }
}
