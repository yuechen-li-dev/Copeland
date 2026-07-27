using System.Diagnostics;
using WorkbookData = Copeland.TsonWorkbookM0.Copeland.Workbook;

long loadAllocationStart = GC.GetAllocatedBytesForCurrentThread();
var loadTimer = Stopwatch.StartNew();
var original = WorkbookData.data();
loadTimer.Stop();
long loadAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - loadAllocationStart;

var revised = WorkbookData.revisedScores();
var highScores = WorkbookData.highScores(original);

Console.WriteLine($"sheets=Scores,Employees");
Console.WriteLine($"rows={original.RowCount},{WorkbookData.employees().RowCount}");
Console.WriteLine($"direct={original.score.At(1).Value:F1}");
Console.WriteLine($"view={highScores.Length}");
Console.WriteLine($"average={WorkbookData.averageScore(original):F2}");
Console.WriteLine($"engineering-average={WorkbookData.engineeringAverage():F2}");
Console.WriteLine($"original-bob={original.score.At(1).Value:F1}");
Console.WriteLine($"revised-bob={revised.score.At(1).Value:F1}");

const int queryIterations = 100_000;
double queryChecksum = 0.0;
long queryAllocationStart = GC.GetAllocatedBytesForCurrentThread();
var queryTimer = Stopwatch.StartNew();
for (int iteration = 0; iteration < queryIterations; iteration++)
{
    queryChecksum += WorkbookData.averageScore(original);
}
queryTimer.Stop();
long queryAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - queryAllocationStart;

Console.WriteLine($"load-ms={loadTimer.Elapsed.TotalMilliseconds:F3}");
Console.WriteLine($"load-allocated-bytes={loadAllocatedBytes}");
Console.WriteLine($"query-count={queryIterations}");
Console.WriteLine($"query-ms={queryTimer.Elapsed.TotalMilliseconds:F3}");
Console.WriteLine($"query-allocated-bytes={queryAllocatedBytes}");
Console.WriteLine($"query-checksum={queryChecksum:F2}");
