using FalloutLoc.Backends.Models;

namespace FalloutLoc.Backends.Abstractions;

public interface IPluginStringDecoder
{
    DecodedString Decode(string? backendValue);

    void VerifyByteRecoveryInvariant();
}
