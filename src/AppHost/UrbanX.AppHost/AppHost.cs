var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL
var postgres = builder.AddPostgres("postgres")
    .WithPgWeb();

var identityDb = postgres.AddDatabase("identitydb", "urbanx_identity");
var catalogDb = postgres.AddDatabase("catalogdb", "urbanx_catalog");
var orderDb = postgres.AddDatabase("orderdb", "urbanx_order");
var paymentDb = postgres.AddDatabase("paymentdb", "urbanx_payment");
var inventoryDb = postgres.AddDatabase("inventorydb", "urbanx_inventory");
var promotionDb = postgres.AddDatabase("promotiondb", "urbanx_promotion");
// var merchantDb = postgres.AddDatabase("merchantdb", "urbanx_merchant");

// Add Redis (built-in Redis Commander UI — reachable from Aspire Dashboard endpoints)
var redis = builder.AddRedis("redis").WithRedisCommander();

// Add RabbitMQ
var rabbitMq = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin();

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

//var merchantService = builder.AddProject<Projects.UrbanX_Services_Merchant>("merchant")
//    .WithReference(merchantDb)
//    .WithReference(identityService)
//    .WaitFor(merchantDb)
//    .WaitFor(identityService);

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
//var gateway = builder.AddProject<Projects.UrbanX_Gateway>("gateway")
//    .WithReference(catalogService)
//    .WithReference(orderService)
//    //.WithReference(merchantService)
//    .WithReference(paymentService)
//    .WithReference(inventoryService)
//    .WithReference(identityService)
//    .WithReference(promotionService)
//    .WaitFor(catalogService)
//    .WaitFor(orderService)
//    //.WaitFor(merchantService)
//    .WaitFor(paymentService)
//    .WaitFor(inventoryService)
//    .WaitFor(identityService)
//    .WaitFor(promotionService);

// var frontend = builder.AddViteApp("frontend", "../../front-end")
//    .WithReference(gateway)
//    .WaitFor(gateway)
//    .WithExternalHttpEndpoints();


builder.Build().Run();
