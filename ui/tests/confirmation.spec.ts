import { test, expect } from "@playwright/test";

test("confirmation waits for monitor application and Enter cannot confirm while busy", async ({
  page,
}) => {
  await page.goto("/");
  const initial = await page.evaluate(async () => {
    const modulePath = "/src/lib/bridge.ts";
    return (await import(modulePath)).call("state");
  });
  await page.addInitScript((hostState) => {
    hostState.busy = true;
    hostState.pending = {
      id: "applying",
      deadline: new Date(Date.now() + 90000).toISOString(),
    };
    const listeners: ((event: { data: any }) => void)[] = [];
    const emit = (data: any) =>
      listeners.forEach((listener) => listener({ data }));
    (window as any).confirmationWrites = 0;
    (window as any).applicationReady = () => {
      hostState.busy = false;
      emit({ event: "busy", data: false, scope: "switch" });
      emit({ event: "state", data: structuredClone(hostState) });
    };
    Object.defineProperty(window, "chrome", {
      configurable: true,
      value: {
        webview: {
          addEventListener: (
            _: string,
            listener: (event: { data: any }) => void,
          ) => listeners.push(listener),
          postMessage: (request: { id: string; method: string }) => {
            let result: any = true;
            if (request.method === "state") result = structuredClone(hostState);
            if (request.method === "confirm") {
              (window as any).confirmationWrites++;
              hostState.pending = null;
            }
            queueMicrotask(() => emit({ id: request.id, result }));
          },
        },
      },
    });
  }, initial);
  await page.reload();
  await expect(
    page.getByRole("heading", {
      name: "Monitorwechsel wird angewendet …",
      exact: true,
    }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Behalten · Enter", exact: true }),
  ).toBeDisabled();
  await page.keyboard.press("Enter");
  expect(await page.evaluate(() => (window as any).confirmationWrites)).toBe(0);
  await page.evaluate(() => (window as any).applicationReady());
  await expect(
    page.getByRole("heading", { name: "Ist das Bild richtig?", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Behalten · Enter", exact: true }),
  ).toBeEnabled();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  expect(await page.evaluate(() => (window as any).confirmationWrites)).toBe(1);
});
