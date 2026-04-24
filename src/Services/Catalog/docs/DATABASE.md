## Product Service

**Database**: PostgreSQL  
**Trách nhiệm**: Catalog sản phẩm, variants, categories, brands  
**Events publish**: `ProductCreated`, `ProductUpdated`, `ProductDeleted`, `ProductStatusChanged`

> **Code & API map**: for service layout, HTTP API behavior, and outbox events, read [CATALOG-SERVICE.md](./CATALOG-SERVICE.md).

```sql
-- ============================================================
-- CATEGORIES (self-referencing, hỗ trợ nested)
-- ============================================================
CREATE TABLE categories (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    parent_id     UUID REFERENCES categories(id),
    name          VARCHAR(255) NOT NULL,
    slug          VARCHAR(255) UNIQUE NOT NULL,
    description   TEXT,
    image_url     VARCHAR(500),
    display_order INT DEFAULT 0,
    is_active     BOOLEAN DEFAULT TRUE,
    -- Materialized path để query subtree O(1)
    path          TEXT,    -- e.g., '/electronics/phones/android'
    depth         INT DEFAULT 0,
    created_at    TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================================
-- BRANDS
-- ============================================================
CREATE TABLE brands (
    id        UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name      VARCHAR(255) UNIQUE NOT NULL,
    slug      VARCHAR(255) UNIQUE NOT NULL,
    logo_url  VARCHAR(500),
    is_active BOOLEAN DEFAULT TRUE
);

-- ============================================================
-- PRODUCTS
-- ============================================================
CREATE TABLE products (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    sku               VARCHAR(100) UNIQUE NOT NULL,
    name              VARCHAR(500) NOT NULL,
    slug              VARCHAR(500) UNIQUE NOT NULL,
    description       TEXT,
    short_description VARCHAR(500),
    category_id       UUID REFERENCES categories(id),
    brand_id          UUID REFERENCES brands(id),
    -- Denormalized để tránh JOIN nội bộ (category/brand hiếm thay đổi)
    category_name     VARCHAR(255),
    brand_name        VARCHAR(255),
    -- Pricing (base price — giá thực lấy từ variant)
    base_price        DECIMAL(18,2) NOT NULL,
    -- Seller info (denormalized từ Auth, cập nhật qua UserUpdated event)
    seller_id         UUID NOT NULL,
    seller_name       VARCHAR(255) NOT NULL,
    status            VARCHAR(20) DEFAULT 'DRAFT',  -- DRAFT | ACTIVE | INACTIVE | DELETED
    weight_grams      INT,
    dimensions        JSONB,   -- {length_cm, width_cm, height_cm}
    tags              TEXT[],
    meta_title        VARCHAR(255),
    meta_description  TEXT,
    created_at        TIMESTAMPTZ DEFAULT NOW(),
    updated_at        TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================================
-- ATTRIBUTE DEFINITIONS (dynamic attributes per category)
-- ============================================================
CREATE TABLE attribute_definitions (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    category_id          UUID REFERENCES categories(id),
    name                 VARCHAR(100) NOT NULL,   -- 'Color', 'Size', 'Material'
    type                 VARCHAR(50)  NOT NULL,   -- 'text' | 'number' | 'boolean' | 'select'
    is_variant_attribute BOOLEAN DEFAULT FALSE,
    display_order        INT DEFAULT 0
);

-- ============================================================
-- PRODUCT VARIANTS
-- ============================================================
CREATE TABLE product_variants (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id       UUID REFERENCES products(id) ON DELETE CASCADE,
    sku              VARCHAR(100) UNIQUE NOT NULL,
    name             VARCHAR(255),    -- e.g., 'Red / XL'
    price            DECIMAL(18,2) NOT NULL,
    compare_at_price DECIMAL(18,2),  -- Giá gốc để tính % giảm giá
    image_url        VARCHAR(500),
    barcode          VARCHAR(100),
    is_active        BOOLEAN DEFAULT TRUE,
    -- ĐÃ XÓA: stock_quantity, is_in_stock → thuộc về Inventory Service
    created_at       TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================================
-- VARIANT ATTRIBUTE VALUES
-- ============================================================
CREATE TABLE variant_attribute_values (
    variant_id   UUID REFERENCES product_variants(id) ON DELETE CASCADE,
    attribute_id UUID REFERENCES attribute_definitions(id),
    value        VARCHAR(255) NOT NULL,
    PRIMARY KEY (variant_id, attribute_id)
);

-- ============================================================
-- PRODUCT IMAGES
-- ============================================================
CREATE TABLE product_images (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id    UUID REFERENCES products(id) ON DELETE CASCADE,
    variant_id    UUID REFERENCES product_variants(id),
    url           VARCHAR(500) NOT NULL,
    alt_text      VARCHAR(255),
    display_order INT DEFAULT 0,
    is_primary    BOOLEAN DEFAULT FALSE
);


-- ============================================================
-- INDEXES
-- ============================================================
CREATE INDEX idx_products_seller       ON products(seller_id);
CREATE INDEX idx_products_category     ON products(category_id);
CREATE INDEX idx_products_status       ON products(status);
CREATE INDEX idx_products_slug         ON products(slug);
CREATE INDEX idx_variants_product      ON product_variants(product_id);
CREATE INDEX idx_categories_parent     ON categories(parent_id);
CREATE INDEX idx_categories_path       ON categories USING gin(path gin_trgm_ops);
```

---
