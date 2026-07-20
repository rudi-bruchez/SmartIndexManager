using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Services;

public interface IAuthAvailability
{
    bool IsAvailable(AuthMode mode);
    string? UnavailableReason(AuthMode mode);   // non-null only when IsAvailable is false
}
