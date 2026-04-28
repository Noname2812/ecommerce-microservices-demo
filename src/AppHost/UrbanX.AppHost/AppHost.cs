var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL
var postgres = builder.AddPostgres("postgres")
    .WithPgWeb();

var catalogDb = postgres.AddDatabase("catalogdb", "urbanx_catalog");
//var orderDb = postgres.AddDatabase("orderdb", "urbanx_order");
//var merchantDb = postgres.AddDatabase("merchantdb", "urbanx_merchant");
//var paymentDb = postgres.AddDatabase("paymentdb", "urbanx_payment");
var inventoryDb = postgres.AddDatabase("inventorydb", "urbanx_inventory");
var identityDb = postgres.AddDatabase("identitydb", "urbanx_identity");

// Add Redis
var redis = builder.AddRedis("redis");

// Add RabbitMQ
var rabbitMq = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin();

// Add Elasticsearch
//  var elasticsearch = builder.AddElasticsearch("elasticsearch");

// Add Services
var identityService = builder.AddProject<Projects.UrbanX_Identity_API>("identity")
    .WithReference(identityDb)
    .WithReference(rabbitMq)
    .WaitFor(identityDb)
    .WaitFor(rabbitMq);

// var searchService = builder.AddProject<Projects.UrbanX_Search_API>("search")
//     .WithReference(identityService)
//     .WithReference(rabbitMq)
//     .WaitFor(rabbitMq)
//     .WaitFor(elasticsearch);

var catalogService = builder.AddProject<Projects.UrbanX_Catalog_API>("catalog")
    .WithReference(catalogDb)
    .WithReference(identityService)
    .WithReference(rabbitMq)
    .WaitFor(catalogDb)
    .WaitFor(identityService)
    .WaitFor(rabbitMq);

//var orderService = builder.AddProject<Projects.UrbanX_Services_Order>("order")
//    .WithReference(orderDb)
//    .WithReference(identityService)
//    .WaitFor(orderDb)
//    .WaitFor(identityService);

//var merchantService = builder.AddProject<Projects.UrbanX_Services_Merchant>("merchant")
//    .WithReference(merchantDb)
//    .WithReference(identityService)
//    .WaitFor(merchantDb)
//    .WaitFor(identityService);

//var paymentService = builder.AddProject<Projects.UrbanX_Services_Payment>("payment")
//    .WithReference(paymentDb)
//    .WithReference(identityService)
//    .WaitFor(paymentDb)
//    .WaitFor(identityService);

var inventoryService = builder.AddProject<Projects.UrbanX_Inventory_API>("inventory")
    .WithReference(inventoryDb)
    .WithReference(identityService)
    .WithReference(rabbitMq)
    .WaitFor(inventoryDb)
    .WaitFor(identityService)
    .WaitFor(rabbitMq);

// Add Gateway with references to all services
var gateway = builder.AddProject<Projects.UrbanX_Gateway>("gateway")
    .WithReference(catalogService)
    //.WithReference(orderService)
    //.WithReference(merchantService)
    //.WithReference(paymentService)
    .WithReference(inventoryService)
    .WithReference(identityService)
    //.WaitFor(searchService)
    .WaitFor(catalogService)
    //.WaitFor(orderService)
    //.WaitFor(merchantService)
    //.WaitFor(paymentService)
    .WaitFor(inventoryService)
    .WaitFor(identityService);

//var frontend = builder.AddViteApp("frontend", "../../frontend/urbanx-react")
//    .WithReference(gateway)
//    .WaitFor(gateway)
//    .WithExternalHttpEndpoints();


builder.Build().Run();
