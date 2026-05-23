import { createBrowserRouter, Navigate, Outlet } from "react-router-dom";
import { useAuthStore } from "@/shared/store/authStore";
import { AppLayout } from "@/shared/components/layout/AppLayout";
import { LoginPage } from "@/features/auth";
import { ProductsPage } from "@/features/products";
import { TestPlaceOrderPage } from "@/features/test-order";

function ProtectedRoute() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  return isAuthenticated ? <Outlet /> : <Navigate to="/test" replace />;
}

function GuestRoute() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  return isAuthenticated ? <Navigate to="/products" replace /> : <Outlet />;
}

export const router = createBrowserRouter([
  { path: "/test", element: <TestPlaceOrderPage /> },
  {
    element: <GuestRoute />,
    children: [{ path: "/login", element: <LoginPage /> }],
  },
  {
    element: <ProtectedRoute />,
    children: [
      {
        element: <AppLayout />,
        children: [
          { index: true, element: <Navigate to="/products" replace /> },
          { path: "/products", element: <ProductsPage /> },
        ],
      },
    ],
  },
  { path: "*", element: <Navigate to="/products" replace /> },
]);
