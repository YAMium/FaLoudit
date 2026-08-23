namespace FalloutLoc.Core.IO;

public sealed class SafetyViolationException : InvalidOperationException
{
    public SafetyViolationException(string message)
        : base(message)
    {
    }
}
