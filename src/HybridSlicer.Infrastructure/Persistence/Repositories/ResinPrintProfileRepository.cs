using HybridSlicer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HybridSlicer.Infrastructure.Persistence.Repositories;

public interface IResinPrintProfileRepository
{
    Task<ResinPrintProfile?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ResinPrintProfile>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(ResinPrintProfile profile, CancellationToken ct = default);
    Task UpdateAsync(ResinPrintProfile profile, CancellationToken ct = default);
}

public sealed class ResinPrintProfileRepository : IResinPrintProfileRepository
{
    private readonly AppDbContext _db;
    public ResinPrintProfileRepository(AppDbContext db) => _db = db;

    public Task<ResinPrintProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.ResinPrintProfiles.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<ResinPrintProfile>> GetAllAsync(CancellationToken ct = default)
        => await _db.ResinPrintProfiles.OrderBy(x => x.Name).ToListAsync(ct);

    public async Task AddAsync(ResinPrintProfile profile, CancellationToken ct = default)
    {
        await _db.ResinPrintProfiles.AddAsync(profile, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ResinPrintProfile profile, CancellationToken ct = default)
    {
        _db.ResinPrintProfiles.Update(profile);
        await _db.SaveChangesAsync(ct);
    }
}
