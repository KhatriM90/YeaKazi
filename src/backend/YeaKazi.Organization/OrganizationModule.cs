using Microsoft.Extensions.DependencyInjection;
using YeaKazi.BuildingBlocks.Contracts;
using YeaKazi.BuildingBlocks.Persistence;
using YeaKazi.Organization.Persistence;
using YeaKazi.Organization.Services;

namespace YeaKazi.Organization;

public static class OrganizationModule
{
    public static IServiceCollection AddOrganizationModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<OrganizationDbContext>(OrganizationDbContext.Schema);
        services.AddScoped<OrganizationService>();
        services.AddScoped<IOrganizationProvisioning, OrganizationProvisioning>();
        services.AddScoped<IOrganizationDirectory, OrganizationDirectory>();
        return services;
    }
}
