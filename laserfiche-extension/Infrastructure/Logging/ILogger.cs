using System;

namespace LaserficheAIExtension.Infrastructure.Logging
{
    /// <summary>
    /// Simple abstraction over logging to avoid direct Serilog dependency in business code.
    /// </summary>
    public interface ILogger<T>
    {
        void Information(string message);
        void Information(string message, params object[] args);
        void Debug(string message);
        void Debug(string message, params object[] args);
        void Warning(string message);
        void Warning(string message, params object[] args);
        void Error(string message);
        void Error(string message, Exception exception);
        void Error(Exception exception, string message, params object[] args);
    }
}
