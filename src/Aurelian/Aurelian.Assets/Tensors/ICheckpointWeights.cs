namespace Aurelian.Assets.Tensors;

public record CheckpointTensorInfo(string Name, string DataType, IReadOnlyList<int> Shape);

/// <summary>Read-only named tensor source. Reads return bounded, finite FP32
/// values in stored order; implementations own format parsing and lifetime.</summary>
public interface ICheckpointWeights
{
    IReadOnlyDictionary<string, CheckpointTensorInfo> Tensors { get; }
    float[] ReadVector(string name);
    float[] ReadRows(string name, int firstRow, int rowCount);
    float[] ReadSlices(string name, int firstSlice, int sliceCount);
}
