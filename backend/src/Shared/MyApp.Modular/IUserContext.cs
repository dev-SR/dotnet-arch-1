namespace MyApp.Modular;

/// <summary>Current actor for audit columns (HTTP claims or <c>system</c>).</summary>
public interface IUserContext
{
    string UserId { get; }
}
