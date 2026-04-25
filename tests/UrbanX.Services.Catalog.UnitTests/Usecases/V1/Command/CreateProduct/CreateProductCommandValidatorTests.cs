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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenSkuIsEmpty_HasValidationError(string sku)
    {
        var command = ValidCommand() with { Sku = sku };
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenNameIsEmpty_HasValidationError(string name)
    {
        var command = ValidCommand() with { Name = name };
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

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_WhenBasePriceIsNegative_HasValidationError(decimal price)
    {
        var command = ValidCommand() with { BasePrice = price };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.BasePrice);
    }

    [Fact]
    public void Validate_WhenVariantsIsNull_HasValidationError()
    {
        var command = ValidCommand() with { Variants = null! };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Variants);
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
                ValidVariant() with { Sku = "VAR-DUP", Name = "Variant 2" }
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
        Assert.True(result.Errors.Any(e => e.PropertyName.Contains("Url")));
    }

    [Fact]
    public void Validate_WhenProductImageUrlIsValid_HasNoError()
    {
        var command = ValidCommand() with
        {
            ProductImages =
            [
                new CreateProductImageItem("https://example.com/img.jpg", "alt text", 0, true),
                new CreateProductImageItem("https://cdn.example.com/img2.png", null, 1, false)
            ]
        };
        var result = _validator.TestValidate(command);
        // Should not have errors on ProductImages
        Assert.False(result.Errors.Any(e => e.PropertyName.Contains("ProductImages")));
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenSkuIsEmpty_HasValidationError(string sku)
    {
        var variant = ValidVariant() with { Sku = sku };
        var result = _validator.TestValidate(variant);
        result.ShouldHaveValidationErrorFor(x => x.Sku);
    }

    [Fact]
    public void Validate_WhenSkuExceedsMaxLength_HasValidationError()
    {
        var variant = ValidVariant() with { Sku = new string('A', 101) };
        var result = _validator.TestValidate(variant);
        result.ShouldHaveValidationErrorFor(x => x.Sku);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_WhenPriceIsZeroOrNegative_HasValidationError(decimal price)
    {
        var variant = ValidVariant() with { Price = price };
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
    public void Validate_WhenPriceIsValid_HasNoError()
    {
        var variant = ValidVariant() with { Price = 1m };
        var result = _validator.TestValidate(variant);
        result.ShouldNotHaveValidationErrorFor(x => x.Price);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WhenCompareAtPriceIsZeroOrNegative_HasValidationError(decimal price)
    {
        var variant = ValidVariant() with { CompareAtPrice = price };
        var result = _validator.TestValidate(variant);
        result.ShouldHaveValidationErrorFor(x => x.CompareAtPrice);
    }

    [Fact]
    public void Validate_WhenCompareAtPriceIsNull_HasNoError()
    {
        var variant = ValidVariant() with { CompareAtPrice = null };
        var result = _validator.TestValidate(variant);
        result.ShouldNotHaveValidationErrorFor(x => x.CompareAtPrice);
    }

    [Fact]
    public void Validate_WhenCompareAtPriceIsPositive_HasNoError()
    {
        var variant = ValidVariant() with { CompareAtPrice = 150_000 };
        var result = _validator.TestValidate(variant);
        result.ShouldNotHaveValidationErrorFor(x => x.CompareAtPrice);
    }

    [Theory]
    [InlineData("not-a-valid-url")]
    [InlineData("htp://invalid.com")]
    [InlineData("")]
    public void Validate_WhenImageUrlIsInvalid_HasValidationError(string imageUrl)
    {
        var variant = ValidVariant() with { ImageUrl = imageUrl };
        var result = _validator.TestValidate(variant);
        result.ShouldHaveValidationErrorFor(x => x.ImageUrl);
    }

    [Fact]
    public void Validate_WhenImageUrlIsNull_HasNoError()
    {
        var variant = ValidVariant() with { ImageUrl = null };
        var result = _validator.TestValidate(variant);
        result.ShouldNotHaveValidationErrorFor(x => x.ImageUrl);
    }

    [Fact]
    public void Validate_WhenImageUrlIsValidHttps_HasNoError()
    {
        var variant = ValidVariant() with { ImageUrl = "https://cdn.example.com/image.jpg" };
        var result = _validator.TestValidate(variant);
        result.ShouldNotHaveValidationErrorFor(x => x.ImageUrl);
    }

    [Fact]
    public void Validate_WhenBarcodeExceedsMaxLength_HasValidationError()
    {
        var variant = ValidVariant() with { Barcode = new string('1', 101) };
        var result = _validator.TestValidate(variant);
        result.ShouldHaveValidationErrorFor(x => x.Barcode);
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
