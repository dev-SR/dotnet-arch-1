namespace MyApp.Modules.Users.Auth;

public interface ITokenService
{
    string CreateAccessToken(User user, IEnumerable<string> roles, IEnumerable<string> permissions);
    (string Raw, string Hash, DateTime ExpiresAt) GenerateRefreshToken();
    string Hash(string raw);
    int AccessTokenExpiresInSeconds { get; }

    /// <summary>
    /// Creates access + refresh entity (caller Adds the entity and maps to TokenResponse;
    /// SaveChanges via TransactionBehavior).
    /// </summary>
    IssuedTokens IssueTokens(
        User user,
        IEnumerable<string> roles,
        IEnumerable<string> permissions,
        Guid familyId);
}
