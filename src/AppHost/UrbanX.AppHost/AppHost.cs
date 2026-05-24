var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL
// Connection budget for 4c/8GB VPS:
//   max_connections=300 → Npgsql pools (Order 40 + Inventory 40 + Catalog 30 + Identity 20 + Payment 20 + Promotion 20 = 170)
//   leaves ~130 headroom for outbox dispatchers, TTL jobs, and admin tooling (pgWeb, migrations).
//
// Write-throughput tuning:
//   synchronous_commit=off batches WAL fsyncs (≤200ms data loss on crash, acceptable for benchmarks).
//   shared_buffers=2GB + effective_cache_size=5GB sized for the 8GB host minus app/runtime overhead.
var postgres = builder.AddPostgres("postgres")
    .WithArgs(
        "-c", "max_connections=300",
        "-c", "shared_buffers=2GB",
        "-c", "effective_cache_size=5GB",
        "-c", "work_mem=16MB",
        "-c", "maintenance_work_mem=256MB",
        "-c", "synchronous_commit=off",
        "-c", "wal_buffers=16MB",
        "-c", "wal_writer_delay=200ms",
        "-c", "checkpoint_completion_target=0.9",
        "-c", "max_wal_size=2GB",
        "-c", "random_page_cost=1.1",
        "-c", "effective_io_concurrency=200")
    .WithPgWeb();

var identityDb = postgres.AddDatabase("identitydb", "urbanx_identity");
var catalogDb = postgres.AddDatabase("catalogdb", "urbanx_catalog");
var orderDb = postgres.AddDatabase("orderdb", "urbanx_order");
var paymentDb = postgres.AddDatabase("paymentdb", "urbanx_payment");
var inventoryDb = postgres.AddDatabase("inventorydb", "urbanx_inventory");
var promotionDb = postgres.AddDatabase("promotiondb", "urbanx_promotion");

// Add Redis (built-in Redis Commander UI — reachable from Aspire Dashboard endpoints)
var redis = builder.AddRedis("redis").WithRedisCommander();

// Add RabbitMQ
var rabbitMq = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()
    .WithImage("masstransit/rabbitmq");

// Add Services
var identityService = builder.AddProject<Projects.UrbanX_Identity_API>("identity")
    .WithReference(identityDb)
    .WithReference(redis)
    .WithReference(rabbitMq)
    .WaitFor(identityDb)
    .WaitFor(redis)
    .WaitFor(rabbitMq);

var catalogService = builder.AddProject<Projects.UrbanX_Catalog_API>("catalog")
    .WithReference(catalogDb)
    .WithReference(redis)
    .WithReference(identityService)
    .WithReference(rabbitMq)
    .WaitFor(catalogDb)
    .WaitFor(redis)
    .WaitFor(identityService)
    .WaitFor(rabbitMq);

var promotionService = builder.AddProject<Projects.UrbanX_Promotion_API>("promotion")
   .WithReference(promotionDb)
   .WithReference(redis)
   .WithReference(identityService)
   .WithReference(rabbitMq)
   .WaitFor(promotionDb)
   .WaitFor(redis)
   .WaitFor(identityService)
   .WaitFor(rabbitMq);

var inventoryService = builder.AddProject<Projects.UrbanX_Inventory_API>("inventory")
   .WithReference(inventoryDb)
   .WithReference(redis)
   .WithReference(identityService)
   .WithReference(rabbitMq)
   .WaitFor(inventoryDb)
   .WaitFor(redis)
   .WaitFor(identityService)
   .WaitFor(rabbitMq);

var orderService = builder.AddProject<Projects.UrbanX_Order_API>("order")
   .WithReference(orderDb)
   .WithReference(redis)
   .WithReference(identityService)
   .WithReference(rabbitMq)
   .WithReference(catalogService)
   .WithReference(inventoryService)
   .WithReference(promotionService)
   .WaitFor(orderDb)
   .WaitFor(redis)
   .WaitFor(identityService)
   .WaitFor(rabbitMq)
   .WaitFor(catalogService)
   .WaitFor(inventoryService)
   .WaitFor(promotionService);

var paymentService = builder.AddProject<Projects.UrbanX_Payment_API>("payment")
   .WithReference(paymentDb)
   .WithReference(redis)
   .WithReference(identityService)
   .WithReference(rabbitMq)
   .WaitFor(paymentDb)
   .WaitFor(redis)
   .WaitFor(identityService)
   .WaitFor(rabbitMq);

// Add Gateway with references to all services
var gateway = builder.AddProject<Projects.UrbanX_Gateway>("gateway")
   .WithReference(catalogService)
   .WithReference(orderService)
   .WithReference(paymentService)
   .WithReference(inventoryService)
   .WithReference(identityService)
   .WithReference(promotionService)
   .WaitFor(catalogService)
   .WaitFor(orderService)
   .WaitFor(paymentService)
   .WaitFor(inventoryService)
   .WaitFor(identityService)
   .WaitFor(promotionService);

var frontend = builder.AddViteApp("frontend", "../../front-end")
   .WithReference(gateway)
   .WaitFor(gateway)
  .WithExternalHttpEndpoints();


builder.Build().Run();
