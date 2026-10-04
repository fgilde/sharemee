import createClient, { type Client } from "openapi-fetch";
import type { paths } from "./schema";

export type { paths, components } from "./schema";

/**
 * A typed Sharemee API client. `token` returns a bearer token of the Sharemee API (the same one MCP clients use);
 * leave it out when the browser sends the session cookie through the Sharemee web app.
 */
export function createSharemeClient(baseUrl: string, token?: () => string | Promise<string>): Client<paths> {
  const client = createClient<paths>({ baseUrl });
  if (token) {
    client.use({
      async onRequest({ request }) {
        request.headers.set("Authorization", `Bearer ${await token()}`);
        return request;
      },
    });
  }

  return client;
}
