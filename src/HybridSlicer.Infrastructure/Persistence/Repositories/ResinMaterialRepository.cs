using HybridSlicer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HybridSlicer.Infrastructure.Persistence.Repositories;

public interface IResinMaterialRepository
{
    Task<ResinMaterial?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ResinMaterial>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(ResinMaterial m, CancellationToken ct = default);
    Task UpdateAsync(ResinMaterial m, CancellationToken ct = default);
}

public sealed class ResinMaterialRepository : IResinMaterialRepository
{
    private readonly AppDbContext _db;
    public ResinMaterialRepository(AppDbContext db) => _db = db;

    public Task<ResinMaterial?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.ResinMaterials.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<ResinMaterial>> GetAllAsync(CancellationToken ct = default)
        => await _db.ResinMaterials.OrderBy(x => x.Name).ToListAsync(ct);

    public async Task AddAsync(ResinMaterial m, CancellationToken ct = default)
    {
        await _db.ResinMaterials.AddAsync(m, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ResinMaterial m, CancellationToken ct = default)
    {
        _db.ResinMaterials.Update(m);
        await _db.SaveChangesAsync(ct);
    }
}
