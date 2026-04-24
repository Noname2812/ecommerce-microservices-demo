using Shared.Contract.Common;

namespace UrbanX.Catalog.Domain.Exceptions
{
    public static class ProductExceptions
    {
        public class VariantsAreRequired : DomainException
        {
            public VariantsAreRequired() : base("Bad Request", "At least one product variant is required.") { }
        }

        public class SkuIsRequired : DomainException
        {
            public SkuIsRequired() : base("Bad Request", "Product SKU is required.") { }
        }

        public class SellerIdIsRequired : DomainException
        {
            public SellerIdIsRequired() : base("Bad Request", "Seller id is required.") { }
        }

        public class SellerNameIsRequired : DomainException
        {
            public SellerNameIsRequired() : base("Bad Request", "Seller name is required.") { }
        }

        public class ProductNameIsRequired : DomainException
        {
            public ProductNameIsRequired() : base("Bad Request", "Product name is required.") { }
        }

        public class InvalidBasePrice : DomainException
        {
            public InvalidBasePrice() : base("Bad Request", "Base price must be non-negative.") { }
        }

        public class VariantSkuRequired : DomainException
        {
            public VariantSkuRequired() : base("Bad Request", "Variant SKU is required.") { }
        }

        public class InvalidPrice : DomainException
        {
            public InvalidPrice() : base("Bad Request", "Price must be greater than zero.") { }
        }
    }
}
