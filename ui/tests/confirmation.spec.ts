import { test, expect } from "@playwright/test";

for (const confirmation of ["Enter", "click", "direct"] as const) {
  test(`confirmation controls appear only when ready and accept ${confirmation}`, async ({
    page,
  }) => {
    await page.goto("/");
    const initial = await page.evaluate(async () => {
      const modulePath = "/src/lib/bridge.ts";
      return (await import(modulePath)).call("state");
    });
    await page.addInitScript(
      ({ hostState, completeDirectly }) => {
        hostState.busy = true;
        hostState.pending = {
          id: "applying",
          deadline: new Date(Date.now() + 90000).toISOString(),
          applying: true,
        };
        const listeners: ((event: { data: any }) => void)[] = [];
        const emit = (data: any) =>
          listeners.forEach((listener) => listener({ data }));
        (window as any).confirmationWrites = 0;
        (window as any).applicationStillApplying = () => {
          hostState.busy = false;
          emit({ event: "busy", data: false, scope: "switch" });
          emit({ event: "state", data: structuredClone(hostState) });
        };
        (window as any).applicationReady = () => {
          hostState.busy = false;
          if (completeDirectly) hostState.pending = null;
          else hostState.pending.applying = false;
          hostState.warning =
            "Monitorwechsel fortgesetzt. TV-Audio ist nicht verfügbar.";
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
                if (request.method === "state")
                  result = structuredClone(hostState);
                if (request.method === "confirm") {
                  (window as any).confirmationWrites++;
                  hostState.pending = null;
                }
                queueMicrotask(() => emit({ id: request.id, result }));
              },
            },
          },
        });
      },
      { hostState: initial, completeDirectly: confirmation === "direct" },
    );
    await page.reload();
    await expect(
      page.getByRole("heading", {
        name: "Monitorwechsel wird angewendet …",
        exact: true,
      }),
    ).toBeVisible();
    await expect(
      page.getByRole("button", { name: "Behalten · Enter", exact: true }),
    ).toHaveCount(0);
    await expect(
      page.getByRole("button", { name: "Zurücksetzen", exact: true }),
    ).toHaveCount(0);
    await page.keyboard.press("Enter");
    expect(await page.evaluate(() => (window as any).confirmationWrites)).toBe(
      0,
    );
    // A stale pending state after the busy event must not expose confirmation.
    await page.evaluate(() => (window as any).applicationStillApplying());
    await expect(
      page.getByRole("button", { name: "Behalten · Enter", exact: true }),
    ).toHaveCount(0);
    await page.keyboard.press("Enter");
    expect(await page.evaluate(() => (window as any).confirmationWrites)).toBe(
      0,
    );
    await page.evaluate(() => (window as any).applicationReady());
    if (confirmation === "direct") {
      await expect(page.getByRole("dialog")).toHaveCount(0);
      await expect(
        page.getByRole("button", { name: "Behalten · Enter", exact: true }),
      ).toHaveCount(0);
      expect(
        await page.evaluate(() => (window as any).confirmationWrites),
      ).toBe(0);
      return;
    }
    await expect(
      page.getByRole("heading", { name: "Ist das Bild richtig?", exact: true }),
    ).toBeVisible();
    await expect(
      page.getByRole("button", { name: "Behalten · Enter", exact: true }),
    ).toBeEnabled();
    await expect(
      page.getByText(
        "Monitorwechsel fortgesetzt. TV-Audio ist nicht verfügbar.",
        { exact: true },
      ),
    ).toBeVisible();
    if (confirmation === "Enter") await page.keyboard.press("Enter");
    else
      await page
        .getByRole("button", { name: "Behalten · Enter", exact: true })
        .click();
    await expect(page.getByRole("dialog")).toHaveCount(0);
    expect(await page.evaluate(() => (window as any).confirmationWrites)).toBe(
      1,
    );
  });
}
