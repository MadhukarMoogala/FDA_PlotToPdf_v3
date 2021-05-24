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

    public static class FilePaths
    {
        public static string InputFile { get; set; }
        public static string OutPut { get; set; }
    }

    class ConsoleHost : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            var cli = new CommandLineApplication(throwOnUnexpectedArg: true)
            {
                FullName = "Plot To PDF",
                Description = "A utility to convert AutoCAD Drawing file to a PDF document!",
                ExtendedHelpText = "\nclient.exe -i <input AutoCAD Drawing file> -o <output folder>\n"

            };
           
            var helpOption = cli.HelpOption("-? | -h | --help");
            if (helpOption.HasValue())
            {
                cli.ShowHelp();
                cli.ShowRootCommandFullNameAndVersion();
                return;
            }
            if(args.Length == 0)
            {
                cli.ShowHelp();                
                cli.ShowRootCommandFullNameAndVersion();
                return;
            }
            string version = typeof(Program).Assembly.GetName().Version.ToString();
            cli.VersionOption("-v", version, string.Format("version {0}", version));
            var input = cli.Option("-i", "Full path to the input AutoCAD drawing.",CommandOptionType.SingleValue);
            var output = cli.Option("-o", "Full path to the output Folder where PDF document should be written.",CommandOptionType.SingleValue);
            cli.Execute(args);
            if (!input.HasValue() || !output.HasValue())
            {
                cli.ShowRootCommandFullNameAndVersion();
                return;
            }
            if (string.IsNullOrWhiteSpace(input.Values[0]) ||
                string.IsNullOrWhiteSpace(output.Values[0]))
            {
                cli.ShowRootCommandFullNameAndVersion();
                return;
            }
            FilePaths.InputFile = input.Values[0];
            FilePaths.OutPut = output.Values[0];

            // Use HostBuilder to bootstrap the application
            var host = new HostBuilder()
                .ConfigureHostConfiguration(builder =>
                {
                    // some logging settings
                    builder.AddJsonFile("appsettings.json");
                })
                .ConfigureAppConfiguration(builder =>
                {
                    // TODO1: you must supply your appsettings.user.json with the following content:
                    //{
                    //    "Forge": {
                    //        "ClientId": "<your client Id>",
                    //        "ClientSecret": "<your secret>"
                    //    }
                    //}
                    builder.AddJsonFile("appsettings.user.json");
                    // Next line means that you can use Forge__ClientId and Forge__ClientSecret environment variables
                    builder.AddEnvironmentVariables();
                    // Finally, allow the use of "legacy" FORGE_CLIENT_ID and FORGE_CLIENT_SECRET environment variables
                    builder.AddForgeAlternativeEnvironmentVariables();
                })
                .ConfigureLogging((hostContext, builder) =>
                {
                    // set up console logging, could be skipped but useful
                    builder.AddConfiguration(hostContext.Configuration.GetSection("Logging"));
                    builder.AddConsole();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    // add our no-op host (required by the HostBuilder)
                    services.AddHostedService<ConsoleHost>();

                    // our own app where all the real stuff happens
                    services.AddSingleton<App>();

                    // add and configure DESIGN AUTOMATION
                    services.AddDesignAutomation(hostContext.Configuration);
                })
                .UseConsoleLifetime()
                .Build();
            using (host)
            {
                await host.StartAsync();

                // Get a reference to our App and run it
                var app = host.Services.GetRequiredService<App>();
                await app.RunAsync();

                await host.StopAsync();
            }
        }

    }

}
