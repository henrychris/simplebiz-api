﻿using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Kluster.Shared.Domain
{
    public class ApplicationUser : IdentityUser
    {
        // todo: use global property for ID length
        [MaxLength(AppConstants.MaxIdLength)]
        public override string Id { get; set; } = "U-" + Guid.NewGuid();

        [MaxLength(AppConstants.MaxNameLength)]
        public required string FirstName { get; set; } = string.Empty;

        [MaxLength(AppConstants.MaxNameLength)]
        public required string LastName { get; set; } = string.Empty;
        // ensure phone number and email are never null.

        /// <summary>
        /// Can be either client or business.
        ///  todo: refactor and update database to use Role not UserType
        /// </summary>
        [MaxLength(10)]
        public required string Role { get; set; }
    }

    public static class AppConstants
    {
        /// <summary>
        /// Use globally to enforce ID lengths
        /// </summary>
        public const int MaxIdLength = 450;

        public const int MinNameLength = 3;
        public const int MaxNameLength = 50;
    }
}