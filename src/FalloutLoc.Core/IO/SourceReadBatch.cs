namespace FalloutLoc.Core.IO;

/// <summary>Read independent sources concurrently, but return results in input order.
/// Custom file listings have no thread-safety contract and are always read serially.</summary>
public static class SourceReadBatch
{
    public static TResult[] Map<T, TResult>(
        IReadOnlyList<T> inputs, ISourceFileListing source, Func<T, TResult> read, int maxWorkers = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWorkers, 1);
        var workers = source is SourceFileSystem or IndexFileListing
            ? Math.Min(Math.Min(maxWorkers, Environment.ProcessorCount), inputs.Count) : 1;
        var results = new TResult[inputs.Count];
        if (workers <= 1)
        {
            for (var i = 0; i < results.Length; i++) results[i] = read(inputs[i]);
        }
        else
        {
            Parallel.ForAsync(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = workers }, (i, _) =>
            {
                results[i] = read(inputs[i]);
                return ValueTask.CompletedTask;
            }).GetAwaiter().GetResult(); // Expose the original guarded I/O error to the CLI.
        }
        return results;
    }
}
