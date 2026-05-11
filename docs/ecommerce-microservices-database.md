# Ecommerce Microservices — Database Design

> **Stack**: PostgreSQL (per service)  
> **Patterns**: Outbox Pattern, CQRS, Database-per-Service, Event-Driven

---

## Mục lục

1. [Auth Service](Dùng Identity)
2. [Product Service](#2-product-service)
3. [Inventory Service](#3-inventory-service)
4. [Order Service](#4-order-service)
5. [Payment Service](#5-payment-service)
6. [Denormalization Map](#6-denormalization-map)

---

## 2. Product Service

**Database**: PostgreSQL  
**Trách nhiệm**: Catalog sản phẩm, variants, categories, brands  
**Events publish**: `ProductCreated`, `ProductUpdated`, `ProductDeleted`, `ProductStatusChanged`

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
    -- ĐÃ XÓA: total_stock, is_in_stock  → thuộc về Inventory Service
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
-- OUTBOX
-- ============================================================
CREATE TABLE outbox_messages (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    aggregate_type VARCHAR(100) NOT NULL,
    aggregate_id   UUID NOT NULL,
    event_type     VARCHAR(255) NOT NULL,
    -- 'ProductCreated' | 'ProductUpdated' | 'ProductDeleted' | 'VariantUpdated'
    payload        JSONB NOT NULL,
    status         VARCHAR(20) DEFAULT 'PENDING',
    created_at     TIMESTAMPTZ DEFAULT NOW(),
    sent_at        TIMESTAMPTZ,
    retry_count    INT DEFAULT 0
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
CREATE INDEX idx_outbox_status         ON outbox_messages(status, created_at) WHERE status = 'PENDING';
```

---

## 3. Inventory Service

**Database**: PostgreSQL  
**Trách nhiệm**: Quản lý tồn kho, reservation, audit trail  
**Events publish**: `StockReserved`, `StockReleased`, `StockConfirmed`, `StockUpdated`, `LowStockAlert`  
**Events consume**: `ProductCreated`, `VariantUpdated` (để sync denormalized product info), `OrderCancelled` (để release reservation)

```sql
-- ============================================================
-- WAREHOUSES
-- ============================================================
CREATE TABLE warehouses (
    id        UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name      VARCHAR(255) NOT NULL,
    code      VARCHAR(50) UNIQUE NOT NULL,
    address   JSONB NOT NULL,
    is_active BOOLEAN DEFAULT TRUE
);

-- ============================================================
-- INVENTORY ITEMS
-- Per variant per warehouse — đây là nguồn sự thật duy nhất về stock
-- ============================================================
CREATE TABLE inventory_items (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    -- Denormalized từ Product service (sync qua ProductCreated/VariantUpdated events)
    product_id          UUID NOT NULL,
    product_name        VARCHAR(500) NOT NULL,
    variant_id          UUID NOT NULL,
    variant_sku         VARCHAR(100) NOT NULL,
    variant_name        VARCHAR(255),
    warehouse_id        UUID REFERENCES warehouses(id),
    -- Stock quantities
    quantity_on_hand    INT NOT NULL DEFAULT 0,
    quantity_reserved   INT NOT NULL DEFAULT 0,  -- Đang được giữ bởi pending orders
    quantity_available  INT GENERATED ALWAYS AS (quantity_on_hand - quantity_reserved) STORED,
    -- Reorder config
    reorder_point       INT DEFAULT 10,
    reorder_quantity    INT DEFAULT 50,
    updated_at          TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE (variant_id, warehouse_id)
);

-- ============================================================
-- INVENTORY RESERVATIONS (CQRS Write side)
-- Giữ hàng khi order được tạo, release nếu order expire/cancel
-- ============================================================
CREATE TABLE inventory_reservations (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    inventory_item_id UUID REFERENCES inventory_items(id),
    order_id          UUID NOT NULL,
    order_item_id     UUID NOT NULL,
    quantity          INT NOT NULL,
    status            VARCHAR(20) DEFAULT 'RESERVED',
    -- RESERVED | CONFIRMED | RELEASED | CANCELLED
    expires_at        TIMESTAMPTZ NOT NULL,  -- Auto-release nếu order không confirm trong thời hạn
    created_at        TIMESTAMPTZ DEFAULT NOW(),
    updated_at        TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================================
-- STOCK MOVEMENTS (audit trail đầy đủ, không bao giờ xóa)
-- ============================================================
CREATE TABLE stock_movements (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    inventory_item_id UUID REFERENCES inventory_items(id),
    movement_type     VARCHAR(50) NOT NULL,
    -- RECEIPT | SALE | RETURN | ADJUSTMENT | TRANSFER_IN | TRANSFER_OUT | RESERVATION | RELEASE
    quantity_change   INT NOT NULL,       -- Positive = nhập, Negative = xuất
    quantity_before   INT NOT NULL,
    quantity_after    INT NOT NULL,
    reference_type    VARCHAR(50),        -- 'ORDER' | 'PURCHASE_ORDER' | 'MANUAL_ADJUSTMENT'
    reference_id      UUID,
    note              TEXT,
    created_by_id     UUID,              -- user_id (denormalized)
    created_by_name   VARCHAR(255),
    created_at        TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================================
-- OUTBOX
-- ============================================================
CREATE TABLE outbox_messages (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    aggregate_type VARCHAR(100) NOT NULL,
    aggregate_id   UUID NOT NULL,
    event_type     VARCHAR(255) NOT NULL,
    -- 'StockReserved' | 'StockReleased' | 'StockConfirmed' | 'StockUpdated' | 'LowStockAlert'
    payload        JSONB NOT NULL,
    status         VARCHAR(20) DEFAULT 'PENDING',
    created_at     TIMESTAMPTZ DEFAULT NOW(),
    sent_at        TIMESTAMPTZ,
    retry_count    INT DEFAULT 0
);

-- ============================================================
-- INDEXES
-- ============================================================
CREATE INDEX idx_inventory_variant      ON inventory_items(variant_id);
CREATE INDEX idx_inventory_product      ON inventory_items(product_id);
CREATE INDEX idx_inventory_warehouse    ON inventory_items(warehouse_id);
CREATE INDEX idx_reservations_order     ON inventory_reservations(order_id);
CREATE INDEX idx_reservations_status    ON inventory_reservations(status, expires_at);
CREATE INDEX idx_movements_item         ON stock_movements(inventory_item_id, created_at DESC);
CREATE INDEX idx_outbox_status          ON outbox_messages(status, created_at) WHERE status = 'PENDING';
```

---

## 4. Order Service

**Database**: PostgreSQL  
**Trách nhiệm**: Quản lý vòng đời đơn hàng  
**Events publish**: `OrderCreated`, `OrderConfirmed`, `OrderCancelled`, `OrderCompleted`, `OrderShipped`  
**Events consume**: `PaymentCompleted`, `PaymentFailed`, `StockReserved`, `StockConfirmed`  
**Lưu ý**: Order items và shipping address là **snapshot** tại thời điểm đặt hàng — không thay đổi dù product bị edit sau đó

```sql
-- ============================================================
-- ORDERS
-- ============================================================
CREATE TABLE orders (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_number     VARCHAR(50) UNIQUE NOT NULL,  -- Readable: ORD-20240123-0001
    -- Customer info snapshot (denormalized từ Auth lúc đặt hàng)
    customer_id      UUID NOT NULL,
    customer_email   VARCHAR(255) NOT NULL,
    customer_name    VARCHAR(255) NOT NULL,
    customer_phone   VARCHAR(20),
    -- Address snapshots (không dùng FK — snapshot tại thời điểm mua)
    shipping_address JSONB NOT NULL,
    -- {street, ward, district, city, province, country, zip_code, recipient_name, recipient_phone}
    billing_address  JSONB,
    -- Pricing
    subtotal         DECIMAL(18,2) NOT NULL,
    discount_amount  DECIMAL(18,2) DEFAULT 0,
    shipping_fee     DECIMAL(18,2) DEFAULT 0,
    tax_amount       DECIMAL(18,2) DEFAULT 0,
    total_amount     DECIMAL(18,2) NOT NULL,
    -- Coupon
    coupon_code      VARCHAR(50),
    coupon_discount  DECIMAL(18,2) DEFAULT 0,
    -- Status
    -- Flow: PENDING → CONFIRMED → PROCESSING → SHIPPED → DELIVERED → COMPLETED
    --       PENDING → CANCELLED
    --       DELIVERED → REFUND_REQUESTED → REFUNDED
    status           VARCHAR(30) DEFAULT 'PENDING',
    payment_status   VARCHAR(30) DEFAULT 'UNPAID',
    -- UNPAID | PAID | REFUNDED | PARTIAL_REFUNDED
    -- Payment info (denormalized từ Payment service qua event)
    payment_method   VARCHAR(50),     -- 'CREDIT_CARD' | 'MOMO' | 'VNPAY' | 'COD'
    payment_reference VARCHAR(255),   -- Transaction ID từ Payment service
    -- Shipping
    shipping_method       VARCHAR(100),
    tracking_number       VARCHAR(255),
    estimated_delivery_at TIMESTAMPTZ,
    shipped_at            TIMESTAMPTZ,
    delivered_at          TIMESTAMPTZ,
    -- Notes
    customer_note    TEXT,
    internal_note    TEXT,
    cancelled_reason TEXT,
    -- Idempotency key (tránh duplicate order)
    idempotency_key  VARCHAR(255) UNIQUE,
    created_at       TIMESTAMPTZ DEFAULT NOW(),
    updated_at       TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================================
-- ORDER ITEMS (snapshot sản phẩm tại thời điểm mua)
-- ============================================================
CREATE TABLE order_items (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id     UUID REFERENCES orders(id) ON DELETE CASCADE,
    -- Product snapshot — KHÔNG thay đổi dù product bị edit/xóa sau đó
    product_id   UUID NOT NULL,
    product_name VARCHAR(500) NOT NULL,
    product_slug VARCHAR(500),
    variant_id   UUID NOT NULL,
    variant_sku  VARCHAR(100) NOT NULL,
    variant_name VARCHAR(255),
    -- Seller snapshot
    seller_id    UUID NOT NULL,
    seller_name  VARCHAR(255) NOT NULL,
    -- Pricing snapshot
    unit_price       DECIMAL(18,2) NOT NULL,
    quantity         INT NOT NULL,
    discount_amount  DECIMAL(18,2) DEFAULT 0,
    subtotal         DECIMAL(18,2) NOT NULL,
    -- Image snapshot (URL tại thời điểm mua)
    image_url    VARCHAR(500),
    -- Fulfillment
    status            VARCHAR(30) DEFAULT 'PENDING',
    refunded_quantity INT DEFAULT 0
);

-- ============================================================
-- ORDER STATUS HISTORY (audit trail)
-- ============================================================
CREATE TABLE order_status_history (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id        UUID REFERENCES orders(id) ON DELETE CASCADE,
    from_status     VARCHAR(30),
    to_status       VARCHAR(30) NOT NULL,
    note            TEXT,
    changed_by_id   UUID,
    changed_by_name VARCHAR(255),
    created_at      TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================================
-- OUTBOX
-- ============================================================
CREATE TABLE outbox_messages (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    aggregate_type VARCHAR(100) NOT NULL,
    aggregate_id   UUID NOT NULL,
    event_type     VARCHAR(255) NOT NULL,
    -- 'OrderCreated' | 'OrderConfirmed' | 'OrderCancelled' | 'OrderCompleted' | 'OrderShipped'
    payload        JSONB NOT NULL,
    status         VARCHAR(20) DEFAULT 'PENDING',
    created_at     TIMESTAMPTZ DEFAULT NOW(),
    sent_at        TIMESTAMPTZ,
    retry_count    INT DEFAULT 0
);

-- ============================================================
-- READ MODEL: Materialized view cho seller dashboard (CQRS Read side)
-- ============================================================
CREATE MATERIALIZED VIEW mv_order_summary_by_seller AS
SELECT
    oi.seller_id,
    oi.seller_name,
    DATE_TRUNC('day', o.created_at) AS order_date,
    COUNT(DISTINCT o.id)            AS total_orders,
    SUM(oi.subtotal)                AS total_revenue,
    SUM(oi.quantity)                AS total_items_sold
FROM orders o
JOIN order_items oi ON oi.order_id = o.id
WHERE o.status NOT IN ('CANCELLED')
GROUP BY oi.seller_id, oi.seller_name, DATE_TRUNC('day', o.created_at);

CREATE UNIQUE INDEX ON mv_order_summary_by_seller (seller_id, order_date);

-- ============================================================
-- INDEXES
-- ============================================================
CREATE INDEX idx_orders_customer     ON orders(customer_id);
CREATE INDEX idx_orders_status       ON orders(status);
CREATE INDEX idx_orders_created      ON orders(created_at DESC);
CREATE INDEX idx_order_items_product ON order_items(product_id);
CREATE INDEX idx_order_items_seller  ON order_items(seller_id);
CREATE INDEX idx_outbox_status       ON outbox_messages(status, created_at) WHERE status = 'PENDING';
```

---

## 5. Payment Service

**Database**: PostgreSQL  
**Trách nhiệm**: Xử lý thanh toán, hoàn tiền, webhook từ payment gateways  
**Events publish**: `PaymentCompleted`, `PaymentFailed`, `RefundProcessed`  
**Events consume**: `OrderCreated` (để init payment intent), `OrderCancelled` (để trigger refund nếu cần)

```sql
-- ============================================================
-- PAYMENT PROVIDERS
-- ============================================================
CREATE TABLE payment_providers (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name                  VARCHAR(100) NOT NULL,   -- 'Stripe', 'MoMo', 'VNPay', 'COD'
    type                  VARCHAR(50)  NOT NULL,   -- 'CARD' | 'EWALLET' | 'BANK_TRANSFER' | 'COD'
    config                JSONB,                   -- API keys/endpoints (encrypted at rest)
    is_active             BOOLEAN DEFAULT TRUE,
    supported_currencies  TEXT[] DEFAULT ARRAY['VND', 'USD']
);

-- ============================================================
-- PAYMENTS
-- ============================================================
CREATE TABLE payments (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    -- Order info (denormalized từ OrderCreated event)
    order_id                UUID NOT NULL,
    order_number            VARCHAR(50) NOT NULL,
    -- Customer info (denormalized)
    customer_id             UUID NOT NULL,
    customer_email          VARCHAR(255) NOT NULL,
    -- Payment details
    provider_id             UUID REFERENCES payment_providers(id),
    provider_name           VARCHAR(100) NOT NULL,
    amount                  DECIMAL(18,2) NOT NULL,
    currency                VARCHAR(10) DEFAULT 'VND',
    -- External transaction
    provider_transaction_id VARCHAR(255),   -- ID từ Stripe/MoMo/VNPay
    provider_response       JSONB,          -- Raw response (lưu để debug)
    -- Status: PENDING → PROCESSING → COMPLETED / FAILED / CANCELLED
    status                  VARCHAR(30) DEFAULT 'PENDING',
    failure_reason          TEXT,
    -- Idempotency (tránh charge 2 lần)
    idempotency_key         VARCHAR(255) UNIQUE NOT NULL,
    -- Metadata
    payment_method_details  JSONB,
    -- {type: 'card', card_last4: '4242', card_brand: 'visa'}
    -- {type: 'ewallet', wallet_phone: '09xx'}
    ip_address              INET,
    paid_at                 TIMESTAMPTZ,
    created_at              TIMESTAMPTZ DEFAULT NOW(),
    updated_at              TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================================
-- REFUNDS
-- ============================================================
CREATE TABLE refunds (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    payment_id        UUID REFERENCES payments(id),
    order_id          UUID NOT NULL,
    amount            DECIMAL(18,2) NOT NULL,
    reason            VARCHAR(255),
    provider_refund_id VARCHAR(255),
    status            VARCHAR(30) DEFAULT 'PENDING',  -- PENDING | COMPLETED | FAILED
    processed_at      TIMESTAMPTZ,
    created_at        TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================================
-- PAYMENT EVENTS LOG (full audit trail + webhook log)
-- ============================================================
CREATE TABLE payment_events (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    payment_id   UUID REFERENCES payments(id),
    event_type   VARCHAR(100) NOT NULL,
    -- 'payment.created' | 'payment.succeeded' | 'payment.failed'
    -- 'webhook.stripe.charge.succeeded' | 'webhook.momo.notify'
    payload      JSONB NOT NULL,
    source       VARCHAR(50),   -- 'INTERNAL' | 'WEBHOOK_STRIPE' | 'WEBHOOK_MOMO' | 'WEBHOOK_VNPAY'
    created_at   TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================================
-- OUTBOX
-- ============================================================
CREATE TABLE outbox_messages (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    aggregate_type VARCHAR(100) NOT NULL,
    aggregate_id   UUID NOT NULL,
    event_type     VARCHAR(255) NOT NULL,
    -- 'PaymentCompleted' | 'PaymentFailed' | 'RefundProcessed'
    payload        JSONB NOT NULL,
    status         VARCHAR(20) DEFAULT 'PENDING',
    created_at     TIMESTAMPTZ DEFAULT NOW(),
    sent_at        TIMESTAMPTZ,
    retry_count    INT DEFAULT 0
);

-- ============================================================
-- INDEXES
-- ============================================================
CREATE INDEX idx_payments_order        ON payments(order_id);
CREATE INDEX idx_payments_customer     ON payments(customer_id);
CREATE INDEX idx_payments_status       ON payments(status);
CREATE INDEX idx_payments_provider_txn ON payments(provider_transaction_id);
CREATE INDEX idx_refunds_payment       ON refunds(payment_id);
CREATE INDEX idx_payment_events        ON payment_events(payment_id, created_at DESC);
CREATE INDEX idx_outbox_status         ON outbox_messages(status, created_at) WHERE status = 'PENDING';
```

---

## 6. Denormalization Map

Nguyên tắc denormalize: chỉ copy data **ít thay đổi** hoặc khi cần **snapshot tại thời điểm** (như order). Data thay đổi thường xuyên (stock, price) **không** denormalize vào service khác — queries đọc trực tiếp từ service owner.

| Service nhận    | Field được denormalize                                   | Nguồn gốc           | Cập nhật khi nào                               |
| --------------- | -------------------------------------------------------- | ------------------- | ---------------------------------------------- |
| **Product**     | `seller_name`                                            | Auth                | `UserUpdated` event                            |
| **Product**     | `category_name`, `brand_name`                            | Product internal    | Khi save category/brand                        |
| **Inventory**   | `product_name`, `variant_sku`, `variant_name`            | Product             | `ProductCreated`, `VariantUpdated` event       |
| **Order**       | `customer_email`, `customer_name`, `customer_phone`      | Auth                | **Snapshot** lúc đặt hàng — không update       |
| **Order**       | `product_name`, `variant_sku`, `unit_price`, `image_url` | Product             | **Snapshot** lúc đặt hàng — không update       |
| **Order**       | `seller_id`, `seller_name`                               | Auth/Product        | **Snapshot** lúc đặt hàng — không update       |
| **Order**       | `shipping_address` (JSON)                                | Auth                | **Snapshot** lúc đặt hàng — không update       |
| **Order**       | `payment_method`, `payment_reference`                    | Payment             | `PaymentCompleted` event                       |
| **Payment**     | `order_number`, `customer_id`, `customer_email`          | Order               | `OrderCreated` event                           |
| **Catalog (read schema)** | `read.product_list_view`, `read.product_detail_view` | Catalog write schema | Projection consumers (via Outbox events) |

### Điều KHÔNG denormalize

| Data                                   | Lý do không denormalize                             |
| -------------------------------------- | --------------------------------------------------- |
| `stock_quantity` vào Product           | Thay đổi liên tục → write amplification, stale data |
| `price` vào Order (ngoại trừ snapshot) | Dễ sai — đã có snapshot trong order_items           |
| User password/sensitive data           | Security                                            |
| Payment provider config                | Security                                            |

---
