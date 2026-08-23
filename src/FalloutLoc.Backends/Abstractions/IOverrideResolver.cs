using FalloutLoc.Backends.Models;

namespace FalloutLoc.Backends.Abstractions;

public interface IOverrideResolver
{
    OverrideTraceResult Trace(OverrideTraceRequest request, CancellationToken cancellationToken = default);
}
