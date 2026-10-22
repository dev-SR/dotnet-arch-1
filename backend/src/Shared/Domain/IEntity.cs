namespace Shared.Domain;

public interface IEntity
{
    Guid Id { get; set; }
    DateTime? CreatedAt { get; set; }
    string? CreatedBy { get; set; }
    DateTime? LastModified { get; set; }
    string? LastModifiedBy { get; set; }
}
