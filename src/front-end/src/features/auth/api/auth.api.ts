import axios from "axios";
import type { AuthUser } from "@/shared/store/authStore";

export interface LoginCredentials {
  username: string;
  password: string;
}

interface TokenResponse {
  access_token: string;
  token_type: string;
  expires_in: number;
}

export async function loginRequest(credentials: LoginCredentials): Promise<{ token: string; user: AuthUser }> {
  const params = new URLSearchParams({
    grant_type: "password",
    username: credentials.username,
    password: credentials.password,
    client_id: "urbanx-admin",
    scope: "openid profile roles",
  });

  const { data } = await axios.post<TokenResponse>("/connect/token", params, {
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
  });

  return { token: data.access_token, user: decodeJwtUser(data.access_token) };
}

function decodeJwtUser(token: string): AuthUser {
  const payload = JSON.parse(atob(token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/")));
  return {
    sub: payload.sub ?? "",
    name: payload.name ?? payload.preferred_username ?? "",
    email: payload.email ?? "",
    roles: Array.isArray(payload.role) ? payload.role : payload.role ? [payload.role] : [],
  };
}
