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

    public static class Users
    {
        public const string Read = "user:read";
        public const string Write = "user:write";
        public const string ManageRoles = "user:manage-roles";
    }

    public static class Roles
    {
        public const string Read = "role:read";
        public const string Write = "role:write";
    }

    public static class Orders
    {
        public const string Read = "order:read";
        public const string Write = "order:write";
    }

    public static class Payment
    {
        public const string Read = "payment:read";
        public const string Write = "payment:write";
    }
}

public static class Roles
{
    public const string Admin = "admin";
    public const string Seller = "seller";
    public const string Customer = "customer";
}
