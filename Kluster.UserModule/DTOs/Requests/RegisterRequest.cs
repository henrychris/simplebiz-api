﻿using System.ComponentModel.DataAnnotations;

namespace Kluster.UserModule.DTOs.Requests;

public record RegisterRequest(string FirstName, string LastName, string EmailAddress, string Password, string Role);