import { useState } from "react";
import { Plus, Search } from "lucide-react";
import { Button } from "@/shared/ui/button";
import { Input } from "@/shared/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/shared/ui/dialog";
import { ProductTable } from "@/features/products/components/ProductTable";
import { ProductForm, type ProductFormValues } from "@/features/products/components/ProductForm";
import { DeleteDialog } from "@/features/products/components/DeleteDialog";
import { useProducts, useCreateProduct, useUpdateProduct, useDeleteProduct } from "@/features/products/hooks/useProducts";
import type { Product } from "@/features/products/types";

type DialogMode = "create" | "edit" | null;

export function ProductsPage() {
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const pageSize = 10;

  const [dialogMode, setDialogMode] = useState<DialogMode>(null);
  const [selected, setSelected] = useState<Product | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<Product | null>(null);

  const { data, isLoading } = useProducts({ pageNumber: page, pageSize, search: search || undefined });
  const createMutation = useCreateProduct();
  const updateMutation = useUpdateProduct();
  const deleteMutation = useDeleteProduct();

  const products = data?.items ?? [];
  const totalPages = data?.totalPages ?? 1;
  const isMutating = createMutation.isPending || updateMutation.isPending;

  function openCreate() { setSelected(null); setDialogMode("create"); }
  function openEdit(p: Product) { setSelected(p); setDialogMode("edit"); }
  function closeDialog() { setDialogMode(null); setSelected(null); }

  async function handleSubmit(values: ProductFormValues) {
    if (dialogMode === "create") {
      await createMutation.mutateAsync(values);
    } else if (dialogMode === "edit" && selected) {
      await updateMutation.mutateAsync({ id: selected.id, ...values });
    }
    closeDialog();
  }

  async function handleDeleteConfirm(id: string) {
    await deleteMutation.mutateAsync(id);
    setDeleteTarget(null);
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">Products</h1>
          <p className="text-sm text-muted-foreground">Manage your product catalog</p>
        </div>
        <Button onClick={openCreate} className="gap-2">
          <Plus className="h-4 w-4" />
          Add Product
        </Button>
      </div>

      <Card>
        <CardHeader className="pb-3">
          <div className="flex items-center gap-3">
            <CardTitle className="text-base">All Products</CardTitle>
            <div className="relative ml-auto w-64">
              <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
              <Input
                placeholder="Search..."
                className="pl-8"
                value={search}
                onChange={(e) => { setSearch(e.target.value); setPage(1); }}
              />
            </div>
          </div>
        </CardHeader>
        <CardContent className="p-0">
          <ProductTable products={products} isLoading={isLoading} onEdit={openEdit} onDelete={setDeleteTarget} />
        </CardContent>
      </Card>

      {totalPages > 1 && (
        <div className="flex items-center justify-end gap-2">
          <Button variant="outline" size="sm" disabled={page === 1} onClick={() => setPage((p) => p - 1)}>Previous</Button>
          <span className="text-sm text-muted-foreground">Page {page} of {totalPages}</span>
          <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>Next</Button>
        </div>
      )}

      <Dialog open={dialogMode !== null} onOpenChange={(v) => !v && closeDialog()}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>{dialogMode === "create" ? "Create Product" : "Edit Product"}</DialogTitle>
          </DialogHeader>
          <ProductForm
            defaultValues={selected ?? undefined}
            isPending={isMutating}
            onSubmit={handleSubmit}
            onCancel={closeDialog}
          />
        </DialogContent>
      </Dialog>

      <DeleteDialog
        product={deleteTarget}
        open={deleteTarget !== null}
        isPending={deleteMutation.isPending}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDeleteConfirm}
      />
    </div>
  );
}
