export interface Product {
  id: string;
  name: string;
  description: string;
  price: number;
  stock: number;
  categoryId: string;
}

export interface CreateProductPayload {
  name: string;
  description: string;
  price: number;
  stock: number;
  categoryId: string;
}

export interface UpdateProductPayload extends CreateProductPayload {
  id: string;
}

export interface GetProductsParams {
  pageNumber?: number;
  pageSize?: number;
  search?: string;
}
