namespace Trayage.Core.Providers;

/// <summary>
/// The information shown to the user during GitHub's OAuth device flow: type the
/// <see cref="UserCode"/> at <see cref="VerificationUri"/> in a browser.
/// </summary>
// ReSharper disable once NotAccessedPositionalProperty.Global
public sealed record DeviceCodePrompt(string UserCode, string VerificationUri, TimeSpan ExpiresIn);

/// <summary>Raised when a provider is asked to authenticate but has no client id configured.</summary>
public sealed class ProviderNotConfiguredException : Exception
{
    public ProviderNotConfiguredException(string message) : base(message) { }
}
