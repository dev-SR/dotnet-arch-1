using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MyApp.Modules.Catalog.Products;

public static class CatalogTelemetry
{
    public static readonly ActivitySource Source = new("MyApp.Catalog", "1.0.0");
    public static readonly Meter Meter = new("MyApp.Catalog", "1.0.0");

    public static readonly Counter<long> ProductsCreated =
        Meter.CreateCounter<long>("myapp.catalog.products_created");

    public static readonly Histogram<double> CreateDuration =
        Meter.CreateHistogram<double>("myapp.catalog.create_duration", unit: "ms");
}
