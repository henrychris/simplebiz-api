using System.Text;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Hangfire;
using Hangfire.PostgreSql;
using Kluster.BusinessModule.ModuleSetup;
using Kluster.Messaging.ModuleSetup;
using Kluster.NotificationModule.ModuleSetup;
using Kluster.PaymentModule.ModuleSetup;
using Kluster.Shared.Configuration;
using Kluster.Shared.Constants;
using Kluster.Shared.Domain;
using Kluster.Shared.Filters;
using Kluster.UserModule.ModuleSetup;
using Kluster.UserModule.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace Kluster.Host
{
    public static class ServiceInitializer
    {
        public static void ConfigureSerilog(this WebApplicationBuilder builder)
        {
            using var scope = builder.Services.BuildServiceProvider().CreateScope();
            var seqSettings = scope.ServiceProvider.GetService<IOptionsSnapshot<SeqSettings>>()?.Value;

            Console.WriteLine($"Using seq @ {seqSettings?.BaseUrl}");
            builder.Host.UseSerilog((_, lc) => lc
                .WriteTo.Console(new JsonFormatter())
                .WriteTo.Seq(seqSettings?.BaseUrl ?? "http://localhost:5341")
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
            );
        }

        public static void RegisterApplicationServices(this IServiceCollection services)
        {
            BindConfigFiles(services);
            RegisterModules(services);
            SetupControllers(services);
            RegisterSwagger(services);
            RegisterFilters(services);
            SetupAuthentication(services);
            SetupCors(services);
            AddHangfire(services);
        }

        private static void SetupControllers(IServiceCollection services)
        {
            services.AddControllers();
            services.AddRouting(options => options.LowercaseUrls = true);
            services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);
            services.AddHealthChecks().AddCheck<CustomHealthCheck>("custom-health-check");
        }

        private static void RegisterSwagger(IServiceCollection services)
        {
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen();

            // setup swagger to accept bearer tokens
            services.AddSwaggerGen(options =>
            {
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description =
                        "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer"
                });
            });
        }

        private static void RegisterFilters(IServiceCollection services)
        {
            services.AddScoped<CustomValidationFilter>();
        }

        private static void SetupAuthentication(IServiceCollection services)
        {
            using var scope = services.BuildServiceProvider().CreateScope();
            var jwtSettings = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<JwtSettings>>().Value;

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultSignInScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(
                    x =>
                    {
                        x.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidAudience = jwtSettings.Audience ??
                                            throw new InvalidOperationException("Audience is null!"),
                            ValidIssuer = jwtSettings.Issuer ??
                                          throw new InvalidOperationException("Security Key is null!"),
                            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey ??
                                throw new InvalidOperationException("Security Key is null!"))),
                            ValidateAudience = true,
                            ValidateIssuer = true,
                            ValidateLifetime = true,
                            ValidateIssuerSigningKey = true,
                            RoleClaimType = JwtClaims.Role
                        };
                    });
            services.AddAuthorization();
        }

        private static void BindConfigFiles(this IServiceCollection services)
        {
            IConfiguration baseConfiguration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (env == Environments.Development)
            {
                Console.WriteLine("Fetching Development Secrets");
                // var userSecretsId = Environment.GetEnvironmentVariable("USER_SECRETS_ID");
                // Console.WriteLine($"UserSecretsId: {userSecretsId}");
                baseConfiguration = new ConfigurationBuilder()
                    .AddUserSecrets<Program>()
                    .AddEnvironmentVariables()
                    .Build();
                Console.WriteLine(
                    $"User secrets have been retrieved. Count: {baseConfiguration.AsEnumerable().Count()}");
            }
            else
            {
                Console.WriteLine("Fetching Production Secrets");
                Console.WriteLine("Trying to fetch secrets configuration from key vault.");
                baseConfiguration = GetSecretsConfigurationAsync(baseConfiguration).GetAwaiter().GetResult();
                Console.WriteLine("Fetched secrets configuration from key vault.");
            }

            ConfigureSettings<DatabaseSettings>(services, baseConfiguration);
            ConfigureSettings<RabbitMqSettings>(services, baseConfiguration);
            ConfigureSettings<MailSettings>(services, baseConfiguration);
            ConfigureSettings<PaystackSettings>(services, baseConfiguration);
            ConfigureSettings<JwtSettings>(services, baseConfiguration);
            ConfigureSettings<KeyVault>(services, baseConfiguration);
            ConfigureSettings<SeqSettings>(services, baseConfiguration);
            Console.WriteLine("Secrets have been bound to classes.");
        }

        private static async Task<IConfigurationRoot> GetSecretsConfigurationAsync(IConfiguration baseConfiguration)
        {
            var keyVaultName = baseConfiguration["KeyVault:Vault"];
            var kvUri = "https://" + keyVaultName + ".vault.azure.net";
            var client = new SecretClient(new Uri(kvUri),
                new ClientSecretCredential(baseConfiguration["KeyVault:AZURE_TENANT_ID"],
                    baseConfiguration["KeyVault:AZURE_CLIENT_ID"],
                    baseConfiguration["KeyVault:AZURE_CLIENT_SECRET"]));
            Console.WriteLine($"Created KeyVault Uri {kvUri}.");

            var secretsManager = new KeyVaultPrefixManager("KlusterApi");
            var secrets = await secretsManager.GetAllSecretsWithPrefixAsync(client);
            if (secrets is null)
            {
                throw new InvalidOperationException("SOMETHING WENT WRONG. SECRETS WEREN'T RETRIEVED CORRECTLY.");
            }

            return new ConfigurationBuilder()
                .AddConfiguration(baseConfiguration)
                .AddInMemoryCollection(secrets!)
                .Build();
        }

        private static void ConfigureSettings<T>(IServiceCollection services, IConfiguration? configuration)
            where T : class, new()
        {
            services.Configure<T>(options => configuration?.GetSection(typeof(T).Name).Bind(options));
        }

        private static void SetupCors(this IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy("AllowSimpleDev",
                    builder =>
                    {
                        builder.AllowAnyMethod().WithOrigins("https://simple-biz.fly.dev").AllowAnyHeader();
                    });

                options.AddPolicy("AllowAnyOrigin",
                    builder =>
                    {
                        builder.AllowAnyOrigin()
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                    });
            });
        }

        private static void RegisterModules(IServiceCollection services)
        {
            services.AddUserModule();
            services.AddBusinessModule();
            services.AddPaymentModule();
            services.AddMessagingModule();
            services.AddNotificationModule();
        }

        private static void AddHangfire(IServiceCollection services)
        {
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            string connectionString;
            if (env == Environments.Development)
            {
                var dbSettings = services.BuildServiceProvider().GetService<IOptionsSnapshot<DatabaseSettings>>()
                    ?.Value;
                connectionString = dbSettings!.ConnectionString!;
            }
            else
            {
                // Use connection string provided at runtime by Fly.
                connectionString = SharedLogic.GetProdPostGresConnectionString();
                Console.WriteLine($"ConnectionString: {connectionString}");
            }

            services.AddHangfire(x => x.UsePostgreSqlStorage(
                x => { x.UseNpgsqlConnection(connectionString); }));
            services.AddHangfireServer();
        }
    }
}