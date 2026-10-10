using MusicRoom.Infrastructure;

namespace MusicRoom.WebAPI;

/// <summary>
/// Everything the infrastructure layer needs from configuration, read in one place — SPEC-041.
/// </summary>
/// <remarks>
/// Its own file because `Program.cs` is at S104's 300-line cap, and adding the provider rows to the options
/// record put it one line over. Issue O24 has recorded that wall since SPEC-032; this is the fifth unit to
/// answer it with a file rather than a raised severity, and the first to move something already there.
/// </remarks>
internal static class InfrastructureOptionsReader
{
    /// <summary>Reads every value the layer's registration takes.</summary>
    /// <param name="builder">The host being built.</param>
    /// <returns>The options record, with nothing defaulted that configuration should name.</returns>
    internal static InfrastructureOptions Read(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return new InfrastructureOptions(
            EnvironmentConfiguration.ReadBCryptWorkFactor(builder.Configuration),
            builder.Environment.IsDevelopment(),
            EnvironmentConfiguration.ReadMailOptions(builder.Configuration),
            EnvironmentConfiguration.ReadCatalogOptions(builder.Configuration),
            EnvironmentConfiguration.RequireJwtOptions(builder.Configuration),
            EnvironmentConfiguration.ReadCredentialAttemptLimits(builder.Configuration),
            SocialProviderConfiguration.Read(builder.Configuration));
    }
}
