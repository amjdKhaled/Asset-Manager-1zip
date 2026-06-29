using Serilog;
using System;

namespace LaserficheAIExtension.Infrastructure.Logging
{
    /// <summary>
    /// Adapter wrapping Serilog for the ILogger<T> interface.
    /// </summary>
    public class SerilogLoggerAdapter<T> : ILogger<T>
    {
        private readonly Serilog.ILogger _logger;

        public SerilogLoggerAdapter(Serilog.ILogger logger)
        {
            _logger = logger?.ForContext<T>() ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Information(string message) => _logger.Information(message);
        public void Information(string message, params object[] args) => _logger.Information(message, args);
        public void Debug(string message) => _logger.Debug(message);
        public void Debug(string message, params object[] args) => _logger.Debug(message, args);
        public void Debug(Exception exception, string message, params object[] args) => _logger.Debug(exception, message, args);
        public void Warning(string message) => _logger.Warning(message);
        public void Warning(string message, params object[] args) => _logger.Warning(message, args);
        public void Warning(Exception exception, string message, params object[] args) => _logger.Warning(exception, message, args);
        public void Error(string message) => _logger.Error(message);
        public void Error(string message, Exception exception) => _logger.Error(exception, message);
        public void Error(Exception exception, string message, params object[] args) => _logger.Error(exception, message, args);
    }
}
