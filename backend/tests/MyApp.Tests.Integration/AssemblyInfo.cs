using Xunit;

// Integration hosts share localhost RabbitMQ durable queues (search/orders/invoices/shipping).
// Parallel class fixtures would compete for the same messages and write to different SQLite DBs,
// causing flaky WaitFor timeouts (e.g. OrderPlaced consumed by another InvoicesApiFactory).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
