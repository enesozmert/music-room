namespace MusicRoom.Application.Abstractions.Security;

/// <summary>
/// Turns a password into something safe to store, and checks a candidate against it.
/// </summary>
/// <remarks>
/// Declared here, implemented in <c>Infrastructure</c> — ADR-0028. The algorithm is ADR-0007's
/// decision and this interface deliberately does not name it, so superseding that ADR touches one
/// project.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Hashes <paramref name="password"/>, salt included, ready to be stored.</summary>
    /// <param name="password">The plaintext password. Never logged, never returned.</param>
    /// <returns>The complete hash string, including the algorithm and cost parameters.</returns>
    string Hash(string password);

    /// <summary>Checks a candidate password against a stored hash.</summary>
    /// <param name="password">The plaintext candidate.</param>
    /// <param name="passwordHash">The stored hash.</param>
    /// <returns>True when the candidate matches.</returns>
    /// <remarks>Nothing calls this until the login spec; it is on the interface because a hasher
    /// that cannot verify is half an abstraction.</remarks>
    bool Verify(string password, string passwordHash);
}
