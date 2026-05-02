import apiClient from "@/shared/api/client";
import type { PageResult } from "@/shared/types";
import type { Product, CreateProductPayload, UpdateProductPayload, GetProductsParams } from "@/features/products/types";

const BASE = "/api/v1/catalog/products";

export async function getProducts(params?: GetProductsParams): Promise<PageResult<Product>> {
  const { data } = await apiClient.get<PageResult<Product>>(BASE, { params });
  return data;
}

export async function getProductById(id: string): Promise<Product> {
  const { data } = await apiClient.get<Product>(`${BASE}/${id}`);
  return data;
}

export async function createProduct(payload: CreateProductPayload): Promise<Product> {
  const { data } = await apiClient.post<Product>(BASE, payload);
  return data;
}

export async function updateProduct({ id, ...payload }: UpdateProductPayload): Promise<Product> {
  const { data } = await apiClient.put<Product>(`${BASE}/${id}`, payload);
  return data;
}

export async function deleteProduct(id: string): Promise<void> {
  await apiClient.delete(`${BASE}/${id}`);
}
