namespace Aurelian.Inference.Dominatus;

public sealed record ComputeCompletionPrediction(double ComputeOnlyFinish, double TransferAwareFinish);

/// <summary>Advisory forward models for an ordered compute queue. Measured GPU
/// costs calibrate the plant; host harvest residuals are evidence only. This is
/// a Smith-style delay experiment, not a qualified closed-loop Smith controller.</summary>
public sealed class ComputeCompletionPredictor
{
    private readonly double[] computeMean = new double[2];
    private double transferMillisecondsPerByte;
    private double computeOnlyFinish;
    private double transferAwareFinish;
    private long observations;
    private double computeOnlyAbsoluteError;
    private double transferAwareAbsoluteError;

    public object Evidence => new
    {
        Observations = observations,
        ComputeOnlyMeanAbsoluteErrorMilliseconds = observations == 0 ? 0 : computeOnlyAbsoluteError / observations,
        TransferAwareMeanAbsoluteErrorMilliseconds = observations == 0 ? 0 : transferAwareAbsoluteError / observations,
        EstimatedTransferGigabytesPerSecond = transferMillisecondsPerByte == 0 ? 0 : 1 / transferMillisecondsPerByte / 1_000_000,
        Scope = "Host-observed errors include harvest latency; costs train only from GPU timestamps. Advisory, no fence authority.",
    };

    public void Begin(double now)
    {
        computeOnlyFinish = now;
        transferAwareFinish = now;
    }

    public ComputeCompletionPrediction Submit(double now, ulong uploadBytes)
    {
        double compute = computeMean[uploadBytes == 0 ? 0 : 1];
        computeOnlyFinish = Math.Max(now, computeOnlyFinish) + compute;
        transferAwareFinish = Math.Max(now, transferAwareFinish) + compute + uploadBytes * transferMillisecondsPerByte;
        return new(computeOnlyFinish, transferAwareFinish);
    }

    public double Complete(ComputeCompletionPrediction prediction, double observedAt,
        ulong uploadBytes, double? computeMilliseconds, double? transferMilliseconds)
    {
        observations++;
        computeOnlyAbsoluteError += Math.Abs(observedAt - prediction.ComputeOnlyFinish);
        double residual = observedAt - prediction.TransferAwareFinish;
        transferAwareAbsoluteError += Math.Abs(residual);
        int kind = uploadBytes == 0 ? 0 : 1;
        if (computeMilliseconds is { } compute && double.IsFinite(compute) && compute >= 0)
        {
            computeMean[kind] = Smooth(computeMean[kind], compute);
        }
        if (uploadBytes > 0 && transferMilliseconds is { } transfer && double.IsFinite(transfer) && transfer >= 0)
        {
            transferMillisecondsPerByte = Smooth(transferMillisecondsPerByte, transfer / uploadBytes);
        }
        return residual;
    }

    private static double Smooth(double previous, double measured)
    {
        return previous == 0 ? measured : previous * .8 + measured * .2;
    }
}
