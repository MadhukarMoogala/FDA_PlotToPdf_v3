using Autodesk.Forge.Core;
using Autodesk.Forge.DesignAutomation;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ClientV3
{

    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAPSService(this IServiceCollection services, ApplicationSettings settings)
        {
            // Register HttpClient
            services.AddHttpClient();

            // Register SDKManager factory
            services.AddSingleton<ISDKManagerFactory, SDKManagerFactory>();

            // Register APS services
            services.AddSingleton<IAPSAuthenticationService, APSAuthenticationService>();
            services.AddSingleton<IAPSStorageService, APSStorageService>();
            services.AddSingleton<IFileDownloadService, FileDownloadService>();

            // Register configuration
            services.AddSingleton(sp =>
            {
                
                return new ApsAppConfiguration
                {
                    ActivityName = "AutoCAD.PlotToPDF+prod",
                    Owner = "dasplottingmad",
                    InputFilePath = settings.InputFilePath,
                    OutputFolderPath = settings.OutputFolderPath               
                };
            });

            // Register the main ForgeApp
            services.AddSingleton<ApsApp>();

            return services;
        }
    }
    public class ApplicationSettings
    {
        public string InputFilePath { get; set; }
        public string OutputFolderPath { get; set; }
    }

    public class ConsoleHost : IHostedService
    {
        private readonly ILogger<ConsoleHost> _logger;

        public ConsoleHost(ILogger<ConsoleHost> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Service started at: {time}", DateTimeOffset.Now);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Service stopped at: {time}", DateTimeOffset.Now);
            return Task.CompletedTask;
        }
    }

    public class Program
    {
        private const string APPLICATION_NAME = "Plot To PDF";
        private const string APPLICATION_DESCRIPTION = "A utility to convert AutoCAD Drawing file to a PDF document!";
        private const string USAGE_INSTRUCTIONS = "\nclient.exe -i <input AutoCAD Drawing file> -o <output folder>\n";

        public static async Task Main(string[] args)
        {
            try
            {
                var settings = ParseCommandLineArguments(args);
                if (settings == null)
                    return;

                await BuildAndRunApplication(settings);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private static ApplicationSettings ParseCommandLineArguments(string[] args)
        {
            var cli = ConfigureCommandLineApplication();

            if (ShouldShowHelp(cli, args))
                return null;

            var (inputOption, outputOption) = AddCommandLineOptions(cli);
            cli.Execute(args);

            if (!AreCommandLineOptionsValid(inputOption, outputOption))
                return null;

            return new ApplicationSettings
            {
                InputFilePath = inputOption.Value(),
                OutputFolderPath = outputOption.Value()
            };
        }

        private static CommandLineApplication ConfigureCommandLineApplication()
        {
            var cli = new CommandLineApplication(throwOnUnexpectedArg: true)
            {
                FullName = APPLICATION_NAME,
                Description = APPLICATION_DESCRIPTION,
                ExtendedHelpText = USAGE_INSTRUCTIONS
            };

            string version = typeof(Program).Assembly.GetName().Version.ToString();
            cli.VersionOption("-v", version, $"version {version}");

            return cli;
        }

        private static bool ShouldShowHelp(CommandLineApplication cli, string[] args)
        {
            var helpOption = cli.HelpOption("-? | -h | --help");

            if (helpOption.HasValue() || args.Length == 0)
            {
                cli.ShowHelp();
                cli.ShowRootCommandFullNameAndVersion();
                return true;
            }

            return false;
        }

        private static (CommandOption input, CommandOption output) AddCommandLineOptions(CommandLineApplication cli)
        {
            var input = cli.Option("-i", "Full path to the input AutoCAD drawing.", CommandOptionType.SingleValue);
            var output = cli.Option("-o", "Full path to the output Folder where PDF document should be written.", CommandOptionType.SingleValue);
            return (input, output);
        }

        private static bool AreCommandLineOptionsValid(CommandOption input, CommandOption output)
        {
            return input.HasValue() &&
                   output.HasValue() &&
                   !string.IsNullOrWhiteSpace(input.Value()) &&
                   !string.IsNullOrWhiteSpace(output.Value());
        }

        private static async Task BuildAndRunApplication(ApplicationSettings settings)
        {
            var host = CreateHostBuilder(settings).Build();

            using (host)
            {
                await host.StartAsync();
                var app = host.Services.GetRequiredService<ApsApp>();
                await app.RunAsync();
                await host.StopAsync();
            }
        }

        private static IHostBuilder CreateHostBuilder(ApplicationSettings settings)
        {
            return new HostBuilder()
                .ConfigureHostConfiguration(ConfigureHost)
                .ConfigureAppConfiguration(ConfigureApp)
                .ConfigureLogging(ConfigureLogging)
                .ConfigureServices((hostContext, services) => ConfigureServices(hostContext, services, settings))
                .UseConsoleLifetime();
        }

        private static void ConfigureHost(IConfigurationBuilder builder)
        {
            builder.AddJsonFile("appsettings.json");
        }

        private static void ConfigureApp(IConfigurationBuilder builder)
        {
            builder.AddJsonFile("appsettings.user.json")
                   .AddEnvironmentVariables().AddForgeAlternativeEnvironmentVariables();
        }

        private static void ConfigureLogging(HostBuilderContext hostContext, ILoggingBuilder builder)
        {
            builder.AddConfiguration(hostContext.Configuration.GetSection("Logging"))
                   .AddConsole();
        }

        private static void ConfigureServices(HostBuilderContext hostContext, IServiceCollection services, ApplicationSettings settings)
        {
            services.AddSingleton(settings);
            services.AddHostedService<ConsoleHost>();
            services.AddDesignAutomation(hostContext.Configuration);

            // Add all APS services using the extension method
            services.AddAPSService(settings);
        }
    }
}