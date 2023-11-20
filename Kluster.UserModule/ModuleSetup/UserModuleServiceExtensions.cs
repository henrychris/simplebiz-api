﻿using Kluster.Shared.Domain;
using Kluster.Shared.Infrastructure;
using Kluster.UserModule.Data;
using Microsoft.AspNetCore.Identity;

namespace Kluster.UserModule.ModuleSetup
{
    public static class UserModuleServiceExtensions
    {
        internal static void AddCore(this IServiceCollection services)
        {
            AddMSIdentity(services);
            DbExtensions.AddDatabase<UserModuleDbContext>(services);
        }

        private static void RegisterDependencies(IServiceCollection services)
        { }

        private static void AddMSIdentity(IServiceCollection services)
        {
            services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 6;

                // Lockout settings.
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;
            }).AddEntityFrameworkStores<UserModuleDbContext>()
                .AddDefaultTokenProviders();
        }
    }
}