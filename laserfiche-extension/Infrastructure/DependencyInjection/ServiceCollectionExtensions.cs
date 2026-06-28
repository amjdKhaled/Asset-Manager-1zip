using LaserficheAIExtension.Infrastructure.Logging;
using LaserficheAIExtension.Models;
using LaserficheAIExtension.Services;
using LaserficheAIExtension.SDK;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.IO;

namespace LaserficheAIExtension.Infrastructure.DependencyInjection
{
    /// <summary>
    /// Extension methods for registering AI extension services.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLaserficheAIExtension(this IServiceCollection services)
        {
            // Logging
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LaserficheAIExtension",
                "logs",
                "extension-.log");

            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                .CreateLogger();

            services.AddSingleton<ILogger>(logger);
            services.AddSingleton(typeof(ILogger<>), typeof(SerilogLoggerAdapter<>));

            // Settings
            services.AddSingleton<ExtensionSettings>(sp => ExtensionSettings.Load());

            // Core services
            services.AddSingleton<ILaserficheDocumentService, LaserficheDocumentService>();
            services.AddSingleton<IWebAppCommunicationService, WebAppCommunicationService>();
            services.AddSingleton<IConnectionMonitorService, ConnectionMonitorService>();
            services.AddSingleton<IDocumentContextTracker, DocumentContextTracker>();
            services.AddSingleton<ICommandHandlerService, CommandHandlerService>();

            // SDK wrapper
            services.AddSingleton<ILaserficheSdkWrapper, LaserficheSdkWrapper>();

            return services;
        }
    }
}
