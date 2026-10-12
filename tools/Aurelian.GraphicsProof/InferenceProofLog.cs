internal static class InferenceProofLog
{
    public static void Write(ComputeStageMeasurement measurement)
    {
        Console.WriteLine($"Stage: {measurement.Name}, {measurement.Milliseconds:F1} ms");
    }
}
