// Marker class for ASP.NET Core resource-based localization.
// IMPORTANT: namespace must be LFPortal.Web (the assembly root namespace)
// so that AddLocalization(ResourcesPath="Resources") resolves the file at
// Resources/SharedResource.resx.  Do not move to LFPortal.Web.Resources.
namespace LFPortal.Web;

/// <summary>
/// Marker class for the shared localization resource.
/// All Razor views and controllers resolve strings via
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{SharedResource}"/>.
/// </summary>
public sealed class SharedResource { }
