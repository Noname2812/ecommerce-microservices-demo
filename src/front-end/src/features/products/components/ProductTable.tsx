import { Pencil, Trash2 } from "lucide-react";
import { Button } from "@/shared/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/shared/ui/table";
import { Badge } from "@/shared/ui/badge";
import { formatCurrency } from "@/shared/lib/utils";
import type { Product } from "@/features/products/types";

interface ProductTableProps {
  products: Product[];
  isLoading: boolean;
  onEdit: (product: Product) => void;
  onDelete: (product: Product) => void;
}

export function ProductTable({ products, isLoading, onEdit, onDelete }: ProductTableProps) {
  if (isLoading) {
    return <div className="flex h-40 items-center justify-center text-sm text-muted-foreground">Loading...</div>;
  }

  if (products.length === 0) {
    return <div className="flex h-40 items-center justify-center text-sm text-muted-foreground">No products found.</div>;
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Name</TableHead>
          <TableHead>Category</TableHead>
          <TableHead className="text-right">Price</TableHead>
          <TableHead className="text-right">Stock</TableHead>
          <TableHead className="w-[100px]" />
        </TableRow>
      </TableHeader>
      <TableBody>
        {products.map((p) => (
          <TableRow key={p.id}>
            <TableCell>
              <p className="font-medium">{p.name}</p>
              {p.description && <p className="text-xs text-muted-foreground line-clamp-1">{p.description}</p>}
            </TableCell>
            <TableCell>
              <Badge variant="secondary">{p.categoryId}</Badge>
            </TableCell>
            <TableCell className="text-right font-mono text-sm">{formatCurrency(p.price)}</TableCell>
            <TableCell className="text-right">
              <span className={p.stock === 0 ? "font-medium text-destructive" : ""}>{p.stock}</span>
            </TableCell>
            <TableCell>
              <div className="flex justify-end gap-1">
                <Button variant="ghost" size="icon" onClick={() => onEdit(p)}>
                  <Pencil className="h-4 w-4" />
                </Button>
                <Button variant="ghost" size="icon" onClick={() => onDelete(p)}>
                  <Trash2 className="h-4 w-4 text-destructive" />
                </Button>
              </div>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
