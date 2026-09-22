// Features/Products/CreateProduct/CreateProductCommand.cs

public sealed record CreateProductCommand(
    string Name,
    string Category,
    decimal Price,
    int Stock) : IRequest<Guid>;
