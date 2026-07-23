namespace Fenrix.IaCStudio.Application.Abstractions;

/// <summary>
/// Runs one-time startup work: create the workspace directory tree and ensure the
/// database exists. Invoked once from the app host at launch.
/// </summary>
public interface IAppInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
