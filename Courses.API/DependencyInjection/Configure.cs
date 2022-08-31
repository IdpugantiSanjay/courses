using System.Reflection;
using Courses.API.Controllers;
using Courses.API.Database;
using Elastic.Apm.NetCoreAll;
using Elastic.Apm.SerilogEnricher;
using Elastic.CommonSchema.Serilog;
using FluentValidation.AspNetCore;
using Mapster;
using MapsterMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Courses.API.DependencyInjection;

public static class ConfigurationKeys
{
    public const string PostgresConnectionString = "Postgres";
    public const string LogToPath = nameof(LogToPath);
    public const string ElasticSearchConnectionString = "ElasticSearchUrl";
}

public static class Configure
{
    public static void ConfigureInfrastructureDependencies(this WebApplicationBuilder builder,
        IConfigurationRoot configuration)
    {
        builder.ConfigureSerilog(configuration);
        builder.Services.AddCommonLibraries(configuration);
    }

    private static void ConfigureSerilog(this WebApplicationBuilder builder, IConfiguration configuration)
    {
        // Serilog.Debugging.SelfLog.Enable(Console.Error);

        // var elasticSearchServerUrl = configuration.GetConnectionString(ConfigurationKeys.ElasticSearchConnectionString);
        // var indexFormat =
        //         $"{Assembly.GetExecutingAssembly().GetName().Name!.ToLower().Replace(".", "-")}-{builder.Environment.EnvironmentName?.ToLower().Replace(".", "-")}-{DateTime.UtcNow:yyyy-MM}"
        //     ;
        //
        //     .WriteTo.Elasticsearch(new ElasticsearchSinkOptions(
        //         new Uri(elasticSearchServerUrl)
        //     )
        //     {
        //         AutoRegisterTemplate = true,
        //         AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv7,
        //         IndexFormat = indexFormat,
        //         CustomFormatter = new EcsTextFormatter()
        //     }
        // )

        builder.Host.UseSerilog((_, lc) =>
            {
                lc
                    .Enrich.FromLogContext()
                    .Enrich.WithEnvironmentName()
                    .Enrich.WithMachineName()
                    .Enrich.WithElasticApmCorrelationInfo()
                    .ReadFrom.Configuration(configuration)
                    .WriteTo.File(new EcsTextFormatter(), configuration.GetValue<string>(ConfigurationKeys.LogToPath),
                        rollingInterval: RollingInterval.Day, flushToDiskInterval: TimeSpan.FromSeconds(5))
                    .WriteTo.Console()
                    ;
            })
            .UseAllElasticApm()
            ;
    }

    private static void AddCommonLibraries(this IServiceCollection serviceCollection, IConfiguration configuration)
    {
        var config = TypeAdapterConfig.GlobalSettings;

        config.Scan(Assembly.GetExecutingAssembly());

        var configValues = new
        {
            ElasticSearchConnectionString =
                configuration.GetConnectionString(ConfigurationKeys.ElasticSearchConnectionString),
            NpgSqlConnectionString = configuration.GetConnectionString(ConfigurationKeys.PostgresConnectionString)
        };

        serviceCollection
            .AddSingleton(config)
            .AddScoped<IMapper, ServiceMapper>()
            .AddFluentValidationAutoValidation()
            .AddFluentValidationClientsideAdapters()
            .AddMediatR(typeof(CoursesController))
            .AddHealthChecks()
            .AddElasticsearch(configValues.ElasticSearchConnectionString)
            .AddNpgSql(configValues.NpgSqlConnectionString)
            ;

        serviceCollection.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configValues.NpgSqlConnectionString));
    }

    public static void ConfigureHttpPipeline(this WebApplication app)
    {
        app.MapHealthChecks("/healthz");
        app.UseSerilogRequestLogging();
    }
}