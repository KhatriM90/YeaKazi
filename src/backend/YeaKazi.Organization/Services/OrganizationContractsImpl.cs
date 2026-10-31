using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Contracts;
using YeaKazi.BuildingBlocks.Tenancy;
using YeaKazi.Organization.Persistence;

namespace YeaKazi.Organization.Services;

/// <summary>Organization's side of the provisioning contract. Joins the caller's shared transaction.</summary>
internal sealed class OrganizationProvisioning(OrganizationDbContext db, OrganizationService service, RequestContext context) : IOrganizationProvisioning
{
    public async Task<Guid> CreateInitialLegalEntityAsync(InitialLegalEntity spec, CancellationToken ct = default)
    {
        if (!context.IsProvisioning)
        {
            throw new InvalidOperationException("The initial legal employer can only be created during tenant provisioning.");
        }

        db.EnlistInSharedTransaction();
        var entity = await service.CreateLegalEntityCoreAsync(spec.Code, spec.Name, spec.RegistrationNumber, spec.KraPin, spec.NssfEmployerNumber, spec.Timezone, ct);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }
}

internal sealed class OrganizationDirectory(OrganizationDbContext db) : IOrganizationDirectory
{
    public Task<bool> LegalEntityExistsAsync(Guid legalEntityId, CancellationToken ct = default)
        => db.LegalEntities.AsNoTracking().AnyAsync(x => x.Id == legalEntityId && x.IsActive, ct);
}
