using System;
using System.Linq;
using System.Threading.Tasks;
using Foundatio.Hosting;
using Foundatio.Hosting.Jobs;
using Foundatio.Hosting.Startup;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Serilog.AspNetCore;
using Serilog;
using System.Diagnostics;

namespace Foundatio.HostingSample {
    public class Program {
        private static Microsoft.Extensions.Logging.ILogger _logger;

        public static async Task<int> Main(string[] args) {
            try {
                await CreateWebHostBuilder(args).Build().RunAsync(_logger);
                return 0;
            } catch (Exception ex) {
                _logger.LogError(ex, "Job host terminated unexpectedly");
                return 1;
            } finally {
                Log.CloseAndFlush();
                
                if (Debugger.IsAttached)
                    Console.ReadKey();
            }
        }
                
        public static IWebHostBuilder CreateWebHostBuilder(string[] args) {
            bool all = args.Contains("all", StringComparer.OrdinalIgnoreCase);
            bool sample1 = all || args.Contains("sample1", StringComparer.OrdinalIgnoreCase);
            bool sample2 = all || args.Contains("sample2", StringComparer.OrdinalIgnoreCase);
            bool everyMinute = all || args.Contains("everyMinute", StringComparer.OrdinalIgnoreCase);
            bool evenMinutes = all || args.Contains("evenMinutes", StringComparer.OrdinalIgnoreCase);
            
            var loggerFactory = new SerilogLoggerFactory(new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console()
                .CreateLogger());

            _logger = loggerFactory.CreateLogger<Program>();

            var builder = WebHost.CreateDefaultBuilder(args)
                .SuppressStatusMessages(true)
                .ConfigureServices(s => {
                    s.AddSingleton<ILoggerFactory>(loggerFactory);

                    // will shutdown the host if no jobs are running
                    s.AddJobLifetimeService();

                    // inserts a startup action that does not complete until the critical health checks are healthy
                    // gets inserted as 1st startup action so that any other startup actions dont run until the critical resources are available
                    s.AddStartupActionToWaitForHealthChecks("Critical");

                    s.AddHealthChecks().AddCheck<MyCriticalHealthCheck>("My Critical Resource", tags: new[] { "Critical" });

                    // add health check that does not return healthy until the startup actions have completed
                    // useful for readiness checks
                    s.AddHealthChecks().AddCheckForStartupActions("Critical");

                    if (everyMinute)
                        s.AddCronJob<EveryMinuteJob>("* * * * *");

                    if (evenMinutes)
                        s.AddCronJob<EvenMinutesJob>("*/2 * * * *");

                    if (sample1)
                        s.AddJob(sp => new Sample1Job(sp.GetRequiredService<ILoggerFactory>()), o => o.ApplyDefaults<Sample1Job>().WaitForStartupActions(true).InitialDelay(TimeSpan.FromSeconds(5)));

                    if (sample2) {
                        s.AddHealthChecks().AddCheck<Sample2Job>("Sample2Job");
                        s.AddJob<Sample2Job>(true);
                    }

                    // if you don't specify priority, actions will automatically be assigned an incrementing priority starting at 0
                    s.AddStartupAction("Test1", async () => {
                        _logger.LogTrace("Running startup 1 action.");
                        for (int i = 0; i < 3; i++) {
                            await Task.Delay(1000);
                            _logger.LogTrace("Running startup 1 action...");
                        }

                        _logger.LogTrace("Done running startup 1 action.");
                    });

                    // then these startup actions will run concurrently since they both have the same priority
                    s.AddStartupAction<MyStartupAction>(priority: 100);
                    s.AddStartupAction<OtherStartupAction>(priority: 100);

                    s.AddStartupAction("Test2", async () => {
                        _logger.LogTrace("Running startup 2 action.");
                        for (int i = 0; i < 2; i++) {
                            await Task.Delay(1500);
                            _logger.LogTrace("Running startup 2 action...");
                        }
                        //throw new ApplicationException("Boom goes the startup.");
                        _logger.LogTrace("Done running startup 2 action.");
                    });
                    
                    //s.AddStartupAction("Boom", () => throw new ApplicationException("Boom goes the startup"));
                })
                .Configure(app => {
                    app.UseHealthChecks("/health");
                    app.UseReadyHealthChecks("Critical");

                    // this middleware will return Service Unavailable until the startup actions have completed
                    app.UseWaitForStartupActionsBeforeServingRequests();

                    // add mvc or other request middleware after the UseWaitForStartupActionsBeforeServingRequests call
                });

            return builder;
        }
    }
}
