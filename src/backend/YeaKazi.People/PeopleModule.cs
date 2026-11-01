using Microsoft.Extensions.DependencyInjection;
using YeaKazi.BuildingBlocks.Persistence;
using YeaKazi.People.Persistence;
using YeaKazi.People.Services;

namespace YeaKazi.People;

public static class PeopleModule
{
    public static IServiceCollection AddPeopleModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<PeopleDbContext>(PeopleDbContext.Schema);
        services.AddScoped<EmployeeService>();
        services.AddScoped<BankBranchService>();
        services.AddScoped<ImportService>();
        return services;
    }
}
