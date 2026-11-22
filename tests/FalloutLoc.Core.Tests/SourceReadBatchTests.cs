using FalloutLoc.Core.IO;

namespace FalloutLoc.Core.Tests;

public sealed class SourceReadBatchTests
{
    [Fact]
    public void CustomListingIsNotCalledConcurrentlyOrOutOfOrder()
    {
        var source = new CallerBoundListing();
        var paths = Enumerable.Range(0, 32).Select(n => n.ToString()).ToArray();
        var result = SourceReadBatch.Map(paths, source, path => source.FileExists(path) ? path : null);
        Assert.Equal(paths, source.Visits);
        Assert.Equal(paths, result);
    }

    private sealed class CallerBoundListing : ISourceFileListing
    {
        private readonly int caller = Environment.CurrentManagedThreadId;
        public List<string> Visits { get; } = [];
        public bool FileExists(string path)
        {
            Assert.Equal(caller, Environment.CurrentManagedThreadId);
            Visits.Add(path);
            return true;
        }
        public bool DirectoryExists(string path) => throw new NotSupportedException();
        public IEnumerable<string> EnumerateDirectories(string path) => throw new NotSupportedException();
        public IEnumerable<string> EnumerateFiles(string path) => throw new NotSupportedException();
    }
}
