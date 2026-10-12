using Aurelian.Assets.Tensors;
using Aurelian.Graphics.Vulkan.ComputeMath;

/// <summary>Pinned learned sparse decoder. Expanded MLP and convolution rows
/// are tiled; CPU work owns topology, never learned feature arithmetic.</summary>
internal sealed class Trellis2SparseDecoderStage(VulkanComputeMath math, SafeTensorFile weights,
    string identity = "shape.decoder")
{
    public float[]? Features { get; private set; }
    public float[]? SubdivisionLogits { get; private set; }
    public List<object> Measurements { get; } = [];
    public SparseVoxelTopology? FinalTopology { get; private set; }
    public List<SparseVoxelSubdivision> Subdivisions { get; } = [];

    public IEnumerable<Trellis2ComputeStage> DecodeComplete(float[] latent, SparseVoxelTopology topology,
        IReadOnlyList<SparseVoxelSubdivision>? guides = null)
    {
        if (latent.Length != topology.Coordinates.Length * 32 || !latent.All(float.IsFinite))
        {
            throw new InvalidDataException("AUR-TRELLIS-DECODER-001: Decoder requires finite N x 32 latent values.");
        }
        using var input = math.CreateSparseTensor(topology.Coordinates.Length, 32);
        if (guides is not null && guides.Count != 4)
        {
            throw new InvalidDataException("AUR-TRELLIS-MATERIAL-002: Texture decoding requires four shape subdivision guides.");
        }
        math.Upload(input, latent);
        GpuTensor? hidden = null;
        try
        {
            yield return new(identity + ".from_latent", () => hidden = Affine(input, "from_latent"));
            int[] blocks = [4, 16, 8, 4, 0];
            for (int level = 0; level < blocks.Length; level++)
            {
                using var neighbors = Neighbors(topology);
                int selectedLevel = level;
                for (int block = 0; block < blocks[level]; block++)
                {
                    int selectedBlock = block;
                    yield return new($"{identity}.blocks.{level}.{block}", () =>
                    {
                        GpuTensor next = ConvNext(hidden!, neighbors, $"blocks.{selectedLevel}.{selectedBlock}");
                        hidden!.Dispose();
                        hidden = next;
                    });
                }
                if (level < blocks.Length - 1)
                {
                    yield return new($"{identity}.upsample.{level}", () =>
                    {
                        SparseVoxelSubdivision subdivision;
                        if (guides is null)
                        {
                            using var logits = Affine(hidden!, $"blocks.{selectedLevel}.{blocks[selectedLevel]}.to_subdiv");
                            subdivision = topology.Subdivide(math.Read(logits));
                        }
                        else
                        {
                            subdivision = guides[selectedLevel];
                            ValidateGuide(topology, subdivision);
                        }
                        if (subdivision.Topology.Coordinates.Length == 0)
                        {
                            throw new InvalidDataException("AUR-TRELLIS-DECODER-003: Learned subdivision produced no active children.");
                        }
                        Subdivisions.Add(subdivision);
                        GpuTensor next = Upsample(hidden!, neighbors, subdivision, $"blocks.{selectedLevel}.{blocks[selectedLevel]}");
                        hidden!.Dispose();
                        hidden = next;
                        topology = subdivision.Topology;
                        Console.WriteLine($"Learned grid {topology.Resolution}: {hidden.Rows} rows x {hidden.Columns}");
                    });
                }
            }
            yield return new(identity + ".output", () =>
            {
                Normalize(hidden!, null, epsilon: 1e-5f, rounded: false);
                using var output = Affine(hidden!, "output_layer", rounded: false);
                Features = math.Read(output);
                FinalTopology = topology;
            });
        }
        finally
        {
            hidden?.Dispose();
        }
    }

    private GpuTensor Neighbors(SparseVoxelTopology topology)
    {
        var result = math.CreateSparseTensor(topology.Coordinates.Length, 27, GpuTensorFormat.UInt32);
        try
        {
            math.UploadUInt32(result, topology.Neighbors27.Select(index => index < 0 ? uint.MaxValue : (uint)index).ToArray());
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static void ValidateGuide(SparseVoxelTopology parent, SparseVoxelSubdivision guide)
    {
        if (guide.Topology.Resolution != parent.Resolution * 2
            || guide.ParentRows.Length != guide.Topology.Coordinates.Length
            || guide.ChildSlots.Length != guide.ParentRows.Length)
        {
            throw new InvalidDataException("AUR-TRELLIS-MATERIAL-002: Shape subdivision guide dimensions do not match.");
        }
        int previousParent = -1;
        int previousSlot = -1;
        for (int child = 0; child < guide.ParentRows.Length; child++)
        {
            int row = guide.ParentRows[child];
            int slot = guide.ChildSlots[child];
            if (row < 0 || row >= parent.Coordinates.Length || slot is < 0 or > 7
                || row < previousParent || row == previousParent && slot <= previousSlot)
            {
                throw new InvalidDataException("AUR-TRELLIS-MATERIAL-002: Shape subdivision mapping is out of range or unordered.");
            }
            VoxelCoordinate coordinate = parent.Coordinates[row];
            VoxelCoordinate expected = new(coordinate.X * 2 + (slot & 1), coordinate.Y * 2 + (slot >> 1 & 1), coordinate.Z * 2 + (slot >> 2 & 1));
            if (guide.Topology.Coordinates[child] != expected)
            {
                throw new InvalidDataException("AUR-TRELLIS-MATERIAL-002: Guide child coordinates do not match their parent/slot identity.");
            }
            previousParent = row;
            previousSlot = slot;
        }
    }

    public IEnumerable<Trellis2ComputeStage> Decode(float[] latent, SparseVoxelTopology topology)
    {
        int rows = topology.Coordinates.Length;
        if (rows is < 1 or > 8192 || latent.Length != rows * 32 || !latent.All(float.IsFinite))
        {
            throw new InvalidDataException("AUR-TRELLIS-DECODER-001: First decoder stage needs finite N x 32 latent values and 1..8192 coordinate rows.");
        }
        using var neighbors = math.CreateTensor(rows, 27, GpuTensorFormat.UInt32);
        math.UploadUInt32(neighbors, topology.Neighbors27.Select(index => index < 0 ? uint.MaxValue : (uint)index).ToArray());
        using var input = math.CreateTensor(rows, 32);
        math.Upload(input, latent);
        GpuTensor? hidden = null;
        try
        {
            yield return new("shape.decoder.from_latent", () => hidden = Affine(input, "from_latent"));
            for (int block = 0; block < 4; block++)
            {
                int selected = block;
                yield return new($"shape.decoder.blocks.0.{selected}", () =>
                {
                    GpuTensor next = ConvNext(hidden!, neighbors, $"blocks.0.{selected}");
                    hidden!.Dispose();
                    hidden = next;
                });
            }
            yield return new("shape.decoder.subdivision", () =>
            {
                using var logits = Affine(hidden!, "blocks.0.4.to_subdiv");
                SubdivisionLogits = math.Read(logits);
                // Explicit qualification-only boundary, never a hidden operation
                // in the reusable sparse gather/matrix APIs.
                Features = math.Read(hidden!);
            });
        }
        finally
        {
            hidden?.Dispose();
        }
    }

    private GpuTensor ConvNext(GpuTensor input, GpuTensor neighbors, string prefix)
    {
        using var convolved = Convolve(input, neighbors, prefix + ".conv");
        Normalize(convolved, prefix + ".norm");
        var output = math.CreateSparseTensor(input.Rows, input.Columns);
        try
        {
            for (int first = 0; first < input.Rows; first += 8192)
            {
                int rows = Math.Min(8192, input.Rows - first);
                using var page = math.CreateTensor(rows, input.Columns);
                using var residual = math.CreateTensor(rows, input.Columns);
                Execute(batch =>
                {
                    batch.CopyElements(convolved, page, sourceOffset: first * input.Columns, count: page.Elements);
                    batch.CopyElements(input, residual, sourceOffset: first * input.Columns, count: residual.Elements);
                });
                using var expanded = Affine(page, prefix + ".mlp.0");
                Activate(expanded);
                using var contracted = Affine(expanded, prefix + ".mlp.2");
                Execute(batch =>
                {
                    batch.Activate(residual, contracted, TensorActivation.Residual, contracted);
                    batch.RoundFloat16(contracted, contracted);
                    batch.CopyElements(contracted, output, first * input.Columns);
                });
            }
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private void Activate(GpuTensor tensor)
    {
        Execute(batch =>
        {
            batch.Activate(tensor, tensor, TensorActivation.Silu);
            batch.RoundFloat16(tensor, tensor);
        });
    }

    private void Normalize(GpuTensor tensor, string? prefix, float epsilon = 1e-6f, bool rounded = true)
    {
        using var gammaBeta = math.CreateTensor(1, tensor.Columns * 2);
        float[] gamma = prefix is null ? Enumerable.Repeat(1f, tensor.Columns).ToArray() : weights.ReadVector(prefix + ".weight");
        float[] beta = prefix is null ? new float[tensor.Columns] : weights.ReadVector(prefix + ".bias");
        math.Upload(gammaBeta, [.. gamma, .. beta]);
        Execute(batch =>
        {
            batch.Normalize(tensor, gammaBeta, tensor, Normalization.LayerNorm, epsilon);
            if (rounded)
            {
                batch.RoundFloat16(tensor, tensor);
            }
        });
    }

    private GpuTensor Affine(GpuTensor input, string prefix, bool rounded = true)
    {
        int channels = weights.Tensors[prefix + ".weight"].Shape[0];
        var output = math.CreateSparseTensor(input.Rows, channels);
        try
        {
            var graph = new Trellis2BlockGraph(math, weights);
            for (int first = 0; first < input.Rows; first += 8192)
            {
                int rows = Math.Min(8192, input.Rows - first);
                using var page = math.CreateTensor(rows, input.Columns);
                Execute(batch => batch.CopyElements(input, page, sourceOffset: first * input.Columns, count: page.Elements));
                using var result = graph.Affine(page, prefix, rounded: false);
                Execute(batch =>
                {
                    if (rounded)
                    {
                        batch.RoundFloat16(result, result);
                    }
                    batch.CopyElements(result, output, first * channels);
                });
            }
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private GpuTensor Upsample(GpuTensor input, GpuTensor neighbors, SparseVoxelSubdivision subdivision, string prefix)
    {
        int outputChannels = weights.Tensors[prefix + ".conv2.weight"].Shape[0];
        using var normalized = math.CreateSparseTensor(input.Rows, input.Columns);
        Execute(batch => batch.CopyElements(input, normalized));
        Normalize(normalized, prefix + ".norm1");
        Activate(normalized);
        using var mapping = math.CreateSparseTensor(subdivision.ParentRows.Length, 2, GpuTensorFormat.UInt32);
        uint[] map = new uint[mapping.Elements];
        for (int child = 0; child < mapping.Rows; child++)
        {
            map[child * 2] = (uint)subdivision.ParentRows[child];
            map[child * 2 + 1] = (uint)subdivision.ChildSlots[child];
        }
        math.UploadUInt32(mapping, map);
        using var gathered = math.CreateSparseTensor(mapping.Rows, outputChannels);
        int firstChild = 0;
        ConvolutionTiles(normalized, neighbors, prefix + ".conv1", (tile, firstParent) =>
        {
            int endChild = firstChild;
            while (endChild < mapping.Rows && subdivision.ParentRows[endChild] < firstParent + tile.Rows)
            {
                endChild++;
            }
            if (endChild > firstChild)
            {
                Execute(batch => batch.SparseChannelToSpatial(tile, mapping, gathered,
                    firstChild, endChild - firstChild, firstParent));
            }
            firstChild = endChild;
        });
        Normalize(gathered, null);
        Activate(gathered);
        using var childNeighbors = Neighbors(subdivision.Topology);
        var output = Convolve(gathered, childNeighbors, prefix + ".conv2");
        try
        {
            using var skip = math.CreateSparseTensor(mapping.Rows, outputChannels);
            Execute(batch =>
            {
                batch.SparseChannelToSpatial(input, mapping, skip);
                batch.Activate(skip, output, TensorActivation.Residual, output);
                batch.RoundFloat16(output, output);
            });
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private GpuTensor Convolve(GpuTensor input, GpuTensor neighbors, string prefix)
    {
        int channels = weights.Tensors[prefix + ".weight"].Shape[0];
        var output = math.CreateSparseTensor(input.Rows, channels);
        try
        {
            ConvolutionTiles(input, neighbors, prefix, (tile, first) =>
                Execute(batch => batch.CopyElements(tile, output, first * channels)));
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private void ConvolutionTiles(GpuTensor input, GpuTensor neighbors, string prefix, Action<GpuTensor, int> consume)
    {
        var shape = weights.Tensors[prefix + ".weight"].Shape;
        if (shape.Count != 5 || shape[1] != 3 || shape[2] != 3
            || shape[3] != 3 || shape[4] != input.Columns)
        {
            throw new InvalidDataException("Sparse ConvNeXt checkpoint layout mismatch: " + prefix);
        }
        int channels = shape[0];
        int inner = input.Columns * 27;
        int weightTile = Math.Min(32, channels);
        var plan = new StreamingLinearPlan(inner, channels, weightTile);
        float[] bias = weights.ReadVector(prefix + ".bias");
        double? gpuMilliseconds = 0;
        int dispatches = 0;
        for (int first = 0; first < input.Rows; first += 2048)
        {
            int rows = Math.Min(2048, input.Rows - first);
            using var columns = math.CreateTensor(rows, inner);
            using var tileOutput = math.CreateTensor(rows, channels);
            Execute(batch => batch.SparseColumns(input, neighbors, columns, first));
            var result = math.LinearStreamed(columns, tileOutput, plan, tile =>
            {
                float[] selected = weights.ReadSlices(prefix + ".weight", tile.FirstOutput, tile.OutputCount);
                var packed = new float[plan.TileRows * (inner + 1)];
                for (int row = 0; row < tile.OutputCount; row++)
                {
                    Array.Copy(selected, row * inner, packed, row * (inner + 1), inner);
                    packed[row * (inner + 1) + inner] = bias[tile.FirstOutput + row];
                }
                return packed;
            });
            gpuMilliseconds += result.GpuMilliseconds;
            dispatches += result.Dispatches;
            Execute(batch =>
            {
                batch.RoundFloat16(tileOutput, tileOutput);
            });
            consume(tileOutput, first);
        }
        Measurements.Add(new { Operation = prefix, Dispatches = dispatches, LinearGpuMilliseconds = gpuMilliseconds });
        Console.WriteLine($"{prefix}: {input.Rows} x {channels}, {gpuMilliseconds:F1} ms matrix GPU");
    }

    private void Execute(Action<ComputeMathBatch> write)
    {
        var batch = math.CreateBatch();
        write(batch);
        math.Execute(batch);
    }
}
