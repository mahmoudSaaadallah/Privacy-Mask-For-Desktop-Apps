using System.Threading;
using System.Threading.Tasks;
using PrivacyMask.Core.Models;

namespace PrivacyMask.Core.Contracts;

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
