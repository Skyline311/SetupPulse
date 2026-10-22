import { test, expect, type Page } from "@playwright/test";

async function hostWithFailedFirstSave(page: Page) {
  await page.goto("/?demoMonitors=3");
  const state = await page.evaluate(async () => {
    const modulePath = "/src/lib/bridge.ts";
    return (await import(modulePath)).call("state");
  });
  await page.addInitScript((initial) => {
    let saves = 0;
    const listeners: ((event: { data: unknown }) => void)[] = [];
    const hostState = initial;
    Object.defineProperty(window, "chrome", {
      configurable: true,
      value: {
        webview: {
          addEventListener: (
            _name: string,
            listener: (event: { data: unknown }) => void,
          ) => listeners.push(listener),
          postMessage: (request: {
            id: string;
            method: string;
            payload: any;
          }) => {
            const response: { id: string; result?: unknown; error?: string } = {
              id: request.id,
              result: true,
            };
            if (request.method === "state")
              response.result = structuredClone(hostState);
            if (request.method === "save") {
              if (++saves === 1)
                response.error =
                  "Ctrl+Alt+0 wird bereits von Windows oder einem anderen Programm verwendet. Prüfe auch Tastenkombinationen alter Desktop-Verknüpfungen. Windows-Fehler 1409.";
              else hostState.settings = request.payload;
            }
            queueMicrotask(() =>
              listeners.forEach((listener) => listener({ data: response })),
            );
          },
        },
      },
    });
  }, state);
  await page.reload();
}

test("save failure stays readable in the editor and retry preserves the setup draft", async ({
  page,
}) => {
  await page.clock.install();
  await hostWithFailedFirstSave(page);
  await page
    .getByRole("button", { name: "Setup erstellen", exact: true })
    .click();
  const editor = page.getByRole("dialog", { name: "Setup bearbeiten" });
  await editor.getByRole("textbox", { name: "Setupname" }).fill("TV");
  await editor
    .getByRole("textbox", { name: "Tastenkombination" })
    .fill("Ctrl+Alt+0");
  await editor.getByRole("button", { name: "Setup speichern" }).click();
  const error = editor.getByRole("alert");
  await expect(error).toContainText("Windows-Fehler 1409");
  await expect(error).toBeInViewport();
  expect(
    await error.evaluate((element) => {
      const box = element.getBoundingClientRect();
      return element.contains(
        document.elementFromPoint(
          box.x + box.width / 2,
          box.y + box.height / 2,
        ),
      );
    }),
  ).toBe(true);
  await page.clock.fastForward(60000);
  await expect(error).toBeVisible();
  await expect(editor.getByRole("textbox", { name: "Setupname" })).toHaveValue(
    "TV",
  );
  await expect(
    editor.getByRole("textbox", { name: "Tastenkombination" }),
  ).toHaveValue("Ctrl+Alt+0");
  await page.screenshot({
    path: "../dev/previews/save-error.png",
    animations: "disabled",
  });
  await editor
    .getByRole("textbox", { name: "Tastenkombination" })
    .fill("Ctrl+Alt+T");
  await editor.getByRole("button", { name: "Setup speichern" }).click();
  await expect(editor).not.toBeVisible();
  await page.getByRole("button", { name: "TV bearbeiten" }).click();
  await expect(editor.getByRole("alert")).toHaveCount(0);
  await expect(
    editor.getByRole("textbox", { name: "Tastenkombination" }),
  ).toHaveValue("Ctrl+Alt+T");
});
