namespace MyApp.Features.Auth;

/// <summary>
/// Domain result of token issuance. <see cref="RawRefreshToken"/> is the one-time
/// client secret; <see cref="RefreshToken"/> is the DB entity (hash only).
/// </summary>
public sealed record IssuedTokens(
    string AccessToken,
    int AccessTokenExpiresInSeconds,
    string RawRefreshToken,
    RefreshToken RefreshToken);
