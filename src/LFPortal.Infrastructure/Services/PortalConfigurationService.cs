using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LFPortal.Application.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LFPortal.Infrastructure.Services;

/// <summary>
/// Persists portal-level connection settings to a writable JSON file and reports
/// the current credential storage status. Registered as a singleton.
/// </summary>
internal sealed class PortalConfigurationService : IPortalConfigurationService
{
    private const string DefaultRepositoryKey        = "default";
    private const string DpapiFileExtension          = ".dpapi";
    private const string DataProtectionFileExtension = ".dprot";

    private readonly string _configFilePath;
    private readonly string _dpRotCredentialDirectory;
    private readonly ILogger<PortalConfigurationService> _logger;

    private static readonly JsonSerializerOptions WriteOptions =
        new() { WriteIndented = true };

    /// <summary>Initialises the service and ensures the config directory exists.</summary>
    public PortalConfigurationService(
        IHostEnvironment hostEnvironment,
        ILogger<PortalConfigurationService> logger)
    {
        var configDirectory = Path.Combine(hostEnvironment.ContentRootPath, "config");
        _configFilePath = Path.Combine(configDirectory, "laserfiche.json");
        _dpRotCredentialDirectory = Path.Combine(configDirectory, "credentials");
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SaveConnectionSettingsAsync(
        string serverUrl,
        string repositoryId,
        string displayName,
        string apiBasePath,
        string apiVersion,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_configFilePath)!;
        Directory.CreateDirectory(directory);

        var content = new
        {
            Laserfiche = new
            {
                ServerUrl    = serverUrl.TrimEnd('/'),
                RepositoryId = repositoryId,
                DisplayName  = displayName,
                ApiBasePath  = apiBasePath,
                ApiVersion   = apiVersion
            }
        };

        var json = JsonSerializer.Serialize(content, WriteOptions);
        await File.WriteAllTextAsync(_configFilePath, json, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Connection settings saved: ServerUrl={ServerUrl}, RepositoryId={RepositoryId}, " +
            "ApiBasePath={ApiBasePath}, ApiVersion={ApiVersion}.",
            serverUrl, repositoryId, apiBasePath, apiVersion);
    }

    /// <inheritdoc />
    public bool HasSavedCredentials()
    {
        var hash = GetKeyHash(DefaultRepositoryKey);

        // Non-Windows: Data Protection encrypted file
        var dpRotPath = Path.Combine(_dpRotCredentialDirectory, $"{hash}{DataProtectionFileExtension}");
        if (File.Exists(dpRotPath)) return true;

        // Windows: DPAPI encrypted file
        if (OperatingSystem.IsWindows())
        {
            var dpapiDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "LFPortal", "credentials");
            var dpapiPath = Path.Combine(dpapiDir, $"{hash}{DpapiFileExtension}");
            if (File.Exists(dpapiPath)) return true;
        }

        return false;
    }

    /// <inheritdoc />
    public bool HasEnvironmentVariableCredentials() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LF_USERNAME")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LF_PASSWORD"));

    private static string GetKeyHash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
}
