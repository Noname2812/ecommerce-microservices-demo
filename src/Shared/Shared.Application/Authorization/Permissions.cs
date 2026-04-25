namespace Shared.Application.Authorization;

public static class Permissions
{
    public static class Products
    {
        public const string Read = "product:read";
        public const string Write = "product:write";
    }

    public static class Inventory
    {
        public const string Read = "inventory:read";
        public const string Write = "inventory:write";
    }
}

public static class Roles
{
    public const string Admin = "admin";
    public const string Seller = "seller";
    public const string Customer = "customer";
}
