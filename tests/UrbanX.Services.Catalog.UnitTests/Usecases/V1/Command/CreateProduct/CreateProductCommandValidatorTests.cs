using FluentValidation.TestHelper;
using UrbanX.Catalog.Application.Usecases.V1.Command;

namespace UrbanX.Services.Catalog.UnitTests.Usecases.V1.Command.CreateProduct;

public class CreateProductCommandValidatorTests
{
    private readonly CreateProductCommandValidator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenSkuIsEmpty_HasValidationError()
    {
        var command = ValidCommand() with { Sku = "" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Sku);
    }

    [Fact]
    public void Validate_WhenSkuExceedsMaxLength_HasValidationError()
    {
        var command = ValidCommand() with { Sku = new string('A', 101) };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Sku);
    }

    [Fact]
    public void Validate_WhenNameIsEmpty_HasValidationError()
    {
        var command = ValidCommand() with { Name = "" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WhenCategoryIdIsEmpty_HasValidationError()
    {
        var command = ValidCommand() with { CategoryId = Guid.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.CategoryId);
    }

    [Fact]
    public void Validate_WhenSellerIdIsEmpty_HasValidationError()
    {
        var command = ValidCommand() with { SellerId = Guid.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.SellerId);
    }

    [Fact]
    public void Validate_WhenSellerNameIsEmpty_HasValidationError()
    {
        var command = ValidCommand() with { SellerName = "" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.SellerName);
    }

    [Fact]
    public void Validate_WhenBasePriceIsNegative_HasValidationError()
    {
        var command = ValidCommand() with { BasePrice = -1 };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.BasePrice);
    }

    [Fact]
    public void Validate_WhenVariantsIsEmpty_HasValidationError()
    {
        var command = ValidCommand() with { Variants = [] };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Variants);
    }

    [Fact]
    public void Validate_WhenProductSkuDuplicatesVariantSku_HasValidationError()
    {
        var command = ValidCommand() with
        {
            Sku = "SKU-001",
            Variants = [ValidVariant() with { Sku = "SKU-001" }]
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void Validate_WhenTwoVariantSkusDuplicate_HasValidationError()
    {
        var command = ValidCommand() with
        {
            Variants =
            [
                ValidVariant() with { Sku = "VAR-DUP" },
                ValidVariant() with { Sku = "VAR-DUP" }
            ]
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void Validate_WhenProductImageHasInvalidUrl_HasValidationError()
    {
        var command = ValidCommand() with
        {
            ProductImages = [new CreateProductImageItem("not-a-valid-url", null, 0, true)]
        };
        var result = _validator.TestValidate(command);
        Assert.NotEmpty(result.Errors);
    }

    private static CreateProductCommand ValidCommand() => new(
        Sku: "SKU-001",
        Name: "Test Product",
        Slug: null,
        Description: null,
        ShortDescription: null,
        CategoryId: Guid.NewGuid(),
        BrandId: null,
        BasePrice: 100_000,
        SellerId: Guid.NewGuid(),
        SellerName: "Test Seller",
        Status: null,
        WeightGrams: null,
        Dimensions: null,
        Tags: null,
        MetaTitle: null,
        MetaDescription: null,
        ProductImages: [],
        Variants: [ValidVariant()]);

    private static CreateProductVariantItem ValidVariant() => new(
        Sku: "VAR-001",
        Name: "Default",
        Price: 100_000,
        CompareAtPrice: null,
        ImageUrl: null,
        Barcode: null,
        Attributes: [],
        GalleryImages: []);
}

public class CreateProductVariantItemValidatorTests
{
    private readonly CreateProductVariantItemValidator _validator = new();

    [Fact]
    public void Validate_WithValidVariant_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidVariant());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenSkuIsEmpty_HasValidationError()
    {
        var variant = ValidVariant() with { Sku = "" };
        var result = _validator.TestValidate(variant);
        result.ShouldHaveValidationErrorFor(x => x.Sku);
    }

    [Fact]
    public void Validate_WhenPriceIsZero_HasValidationError()
    {
        var variant = ValidVariant() with { Price = 0 };
        var result = _validator.TestValidate(variant);
        result.ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Fact]
    public void Validate_WhenPriceExceedsMaximum_HasValidationError()
    {
        var variant = ValidVariant() with { Price = 1_000_000_001m };
        var result = _validator.TestValidate(variant);
        result.ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Fact]
    public void Validate_WhenCompareAtPriceIsZero_HasValidationError()
    {
        var variant = ValidVariant() with { CompareAtPrice = 0 };
        var result = _validator.TestValidate(variant);
        result.ShouldHaveValidationErrorFor(x => x.CompareAtPrice);
    }

    [Fact]
    public void Validate_WhenImageUrlIsInvalid_HasValidationError()
    {
        var variant = ValidVariant() with { ImageUrl = "not-a-valid-url" };
        var result = _validator.TestValidate(variant);
        result.ShouldHaveValidationErrorFor(x => x.ImageUrl);
    }

    private static CreateProductVariantItem ValidVariant() => new(
        Sku: "VAR-001",
        Name: "Default",
        Price: 100_000,
        CompareAtPrice: null,
        ImageUrl: null,
        Barcode: null,
        Attributes: [],
        GalleryImages: []);
}
