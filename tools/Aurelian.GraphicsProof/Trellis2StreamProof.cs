using System.Runtime.InteropServices;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;

internal static class Trellis2StreamProof
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian streamed compute qualification", EnableCooperativeMatrices: true));
        Require(initialization.Success, "Vulkan plant creation failed.");
        using var plant = initialization.Plant!;
        using var math = new VulkanComputeMath(plant, new GameAssets().ComputeShader, maximumTensorElements: 67108864);
        using var controller = new Trellis2ComputeFeedforward();
        var measurements = new List<object>();
        CheckLinear(17, 35, 41, 16, false);
        CheckLinear(513, 128, 80, 32, false);
        CheckLinear(513, 128, 80, 32, true);
        CheckLinear(17, 8192, 33, 16, true);
        CheckLinear(17, 6144, 33, 16, true);
        if (math.CooperativeMatrixEnabled)
        {
            CheckHalfPacking();
        }
        CheckFailures();
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true,
            CooperativeEnabled = math.CooperativeMatrixEnabled,
            Measurements = measurements,
            Feedforward = controller.Evidence,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_TRELLIS2_STREAM_PIPELINE_PASSED");

        void CheckLinear(int rows, int inner, int columns, int tileRows, bool cooperative)
        {
            using var input = math.CreateTensor(rows, inner);
            using var result = math.CreateTensor(rows, columns);
            float[] source = Enumerable.Range(0, input.Elements).Select(index => MathF.Sin(index * .017f) * .2f).ToArray();
            var plan = new StreamingLinearPlan(inner, columns, tileRows);
            math.Upload(input, source);
            math.StreamController = null;
            var baseline = math.LinearStreamed(input, result, plan, Load);
            float[] expected = math.Read(result);
            math.StreamController = controller;
            math.UseCooperativeLinear = cooperative;
            int reads = math.ReadbackCount;
            var execution = math.LinearStreamed(input, result, plan, Load);
            Require(math.ReadbackCount == reads, "Stream performed an intermediate readback.");
            float[] actual = math.Read(result);
            Require(actual.All(float.IsFinite), "Nonfinite streamed linear result.");
            bool identical = MemoryMarshal.AsBytes(actual.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(expected.AsSpan()));
            double error = actual.Zip(expected, (a, b) => Math.Pow((double)a - b, 2)).Sum();
            double energy = expected.Sum(value => (double)value * value);
            double relative = Math.Sqrt(error / Math.Max(energy, 1e-30));
            Require(execution.Cooperative ? relative < .001 : identical,
                $"Linear qualification failed: {rows}/{inner}/{columns}, RMS={relative}, identical={identical}.");
            measurements.Add(new { Rows = rows, Inner = inner, Columns = columns, CooperativeRequested = cooperative,
                Actual = execution, RelativeRms = relative, BitExact = identical, BaselineDispatches = baseline.Dispatches });
            Console.WriteLine($"stream {rows}/{inner}/{columns}: cooperative={execution.Cooperative}, RMS={relative:G5}, identical={identical}");

            float[] Load(LinearWeightTile tile)
            {
                var values = new float[plan.TileRows * (inner + 1)];
                for (int row = 0; row < tile.OutputCount; row++)
                {
                    for (int column = 0; column < inner; column++)
                    {
                        values[row * (inner + 1) + column] = MathF.Cos((tile.FirstOutput + row) * .07f + column * .013f) * .15f;
                    }
                    values[row * (inner + 1) + inner] = (tile.FirstOutput + row) * .001f;
                }
                return values;
            }
        }

        void CheckHalfPacking()
        {
            float[] cases = [0, -0f, MathF.Pow(2, -25), 3 * MathF.Pow(2, -25),
                -3 * MathF.Pow(2, -25), MathF.Pow(2, -14), 1.00048828125f, 1.00146484375f,
                -1.00146484375f, 65504, -65504];
            float[] source = Enumerable.Range(0, 256).Select(index => cases[index % cases.Length]).ToArray();
            float[] identity = Enumerable.Range(0, 256).Select(index => index / 16 == index % 16 ? 1f : 0f).ToArray();
            using var input = math.CreateTensor(16, 16);
            using var packed = math.CreateTensor(16, 16, GpuTensorFormat.PackedFloat16);
            using var weights = math.CreateTensor(16, 16, GpuTensorFormat.PackedFloat16);
            using var result = math.CreateTensor(16, 16);
            math.Upload(input, source);
            math.Upload(weights, identity);
            var batch = math.CreateBatch();
            batch.PackMatrixHalf(input, packed);
            batch.MultiplyCooperative(packed, weights, result);
            math.Execute(batch);
            float[] actual = math.Read(result);
            float[] expected = source.Select(value => (float)(Half)value).ToArray();
            Require(actual.Zip(expected, (a, b) => a == b).All(equal => equal),
                "GPU half packing disagrees with .NET binary16 on ties/subnormals/maximum finite values.");
            measurements.Add(new { HalfPacking = "CPU binary16 rounding oracle through cooperative identity", Exact = true });
        }

        void CheckFailures()
        {
            using var input = math.CreateTensor(17, 128);
            using var result = math.CreateTensor(17, 48);
            math.Upload(input, Enumerable.Repeat(.1f, input.Elements).ToArray());
            var plan = new StreamingLinearPlan(128, 48, 16);
            math.StreamController = controller;
            math.UseCooperativeLinear = false;
            ulong live = math.MemoryTelemetry.LiveBytes;
            var expected = new InvalidOperationException("Intentional loader failure after submission");
            try
            {
                math.LinearStreamed(input, result, plan, tile => tile.FirstOutput == 0
                    ? new float[16 * 129] : throw expected);
                throw new InvalidDataException("Loader failure was swallowed.");
            }
            catch (InvalidOperationException exception) when (ReferenceEquals(exception, expected))
            {
            }
            Require(math.MemoryTelemetry.LiveBytes == live, "Failed stream leaked physical allocations.");
            math.StreamController = new FailureController();
            try
            {
                math.LinearStreamed(input, result, plan, _ => new float[16 * 129]);
                throw new InvalidDataException("Controller failure was swallowed.");
            }
            catch (InvalidOperationException exception) when (exception.Message == "Intentional submitted-observer failure")
            {
            }
            Require(math.MemoryTelemetry.LiveBytes == live, "Controller failure leaked in-flight allocations.");
            math.StreamController = new FixedController(5);
            try
            {
                math.LinearStreamed(input, result, plan, _ => new float[16 * 129]);
                throw new InvalidDataException("Invalid depth was accepted.");
            }
            catch (ArgumentException exception) when (exception.Message.StartsWith("AUR-COMPUTE-MATH-001:", StringComparison.Ordinal))
            {
            }
            Require(math.MemoryTelemetry.LiveBytes == live, "Invalid depth allocated resources.");
            math.StreamController = controller;
            math.LinearStreamed(input, result, plan, _ => new float[16 * 129]);
            Require(math.Read(result).All(value => value == 0), "Owner did not recover after injected failures.");
            measurements.Add(new { LoaderFailure = true, ObserverFailure = true, InvalidDepth = true,
                LiveAllocationBytesRestored = true, OwnerReusable = true });
        }
    }

    private sealed class FixedController(int depth) : IComputeStreamController
    {
        public int Plan(ComputeStreamForecast forecast) => depth;
        public void Observe(ComputeStreamObservation observation) { }
    }

    private sealed class FailureController : IComputeStreamController
    {
        public int Plan(ComputeStreamForecast forecast) => 2;
        public void Observe(ComputeStreamObservation observation)
        {
            if (!observation.Completed)
            {
                throw new InvalidOperationException("Intentional submitted-observer failure");
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
