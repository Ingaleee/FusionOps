using FusionOps.Domain.Entities;

namespace FusionOps.Domain.Interfaces;

public interface ILicenseRepository
{
    Task<LicensePool?> FindByProductAsync(string product, CancellationToken ct = default);
    Task AddAsync(LicensePool pool, CancellationToken ct = default);
    Task UpdateAsync(LicensePool pool, CancellationToken ct = default);
    Task<IReadOnlyCollection<LicensePool>> GetAllAsync(CancellationToken ct = default);
}


