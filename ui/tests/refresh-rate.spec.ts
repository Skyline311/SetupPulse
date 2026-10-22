import { test, expect, type Page } from "@playwright/test";
import type { State } from "../src/lib/bridge";

async function state(page: Page): Promise<State> {
  return page.evaluate(async () => {
    const path = "/src/lib/bridge.ts";
    return (await import(path)).call("state");
  });
}

async function openEditor(page: Page, rate = 60, missingModes = false) {
  await page.goto("/");
  const initial = await state(page);
  const display = initial.inventory.displays[0];
  display.current!.refreshRate = rate;
  display.modes = missingModes
    ? []
    : [
        { width: 2560, height: 1440, refreshRate: 60, recommended: true },
        { width: 2560, height: 1440, refreshRate: 120, recommended: false },
        { width: 2560, height: 1440, refreshRate: 144, recommended: false },
        { width: 2560, height: 1440, refreshRate: 143.998, recommended: false },
        { width: 1920, height: 1080, refreshRate: 60, recommended: false },
        { width: 1920, height: 1080, refreshRate: 59.94, recommended: false },
      ];
  await page.addInitScript((hostState) => {
    const listeners: ((event: { data: unknown }) => void)[] = [];
    Object.defineProperty(window, "chrome", {
      configurable: true,
      value: {
        webview: {
          addEventListener: (
            _: string,
            listener: (event: { data: unknown }) => void,
          ) => listeners.push(listener),
          postMessage: (request: {
            id: string;
            method: string;
            payload: unknown;
          }) => {
            if (request.method === "save")
              hostState.settings = structuredClone(
                request.payload as State["settings"],
              );
            const result =
              request.method === "state" ? structuredClone(hostState) : true;
            queueMicrotask(() =>
              listeners.forEach((listener) =>
                listener({ data: { id: request.id, result } }),
              ),
            );
          },
        },
      },
    });
  }, initial);
  await page.reload();
  await page
    .getByRole("button", { name: "Setup erstellen", exact: true })
    .click();
  return page.getByRole("combobox", { name: "Demo Monitor Hz", exact: true });
}

test("reported Hz presets filter by resolution and orientation and save the selected value", async ({
  page,
}) => {
  const hz = await openEditor(page);
  await expect(hz.locator("option")).toHaveText([
    "144 Hz",
    "120 Hz",
    "60 Hz (aktuell)",
    "Benutzerdefiniert …",
  ]);
  await expect(hz).toHaveValue("60");
  await hz.selectOption("144");
  await expect(
    page.getByRole("spinbutton", {
      name: "Demo Monitor Hz manuell",
      exact: true,
    }),
  ).toHaveCount(0);
  const resolution = page.getByRole("combobox", {
    name: "Demo Monitor Auflösung",
    exact: true,
  });
  await resolution.selectOption("1920x1080");
  await expect(hz).toHaveValue("60");
  await expect(hz.locator("option")).toHaveText([
    "60 Hz",
    "59,94 Hz",
    "Benutzerdefiniert …",
  ]);
  await resolution.selectOption("2560x1440");
  await page
    .getByRole("combobox", { name: "Demo Monitor Ausrichtung", exact: true })
    .selectOption("2");
  await expect(resolution).toHaveValue("1440x2560");
  await expect(hz.locator("option")).toHaveText([
    "144 Hz",
    "120 Hz",
    "60 Hz",
    "Benutzerdefiniert …",
  ]);
  await hz.selectOption("144");
  await hz.scrollIntoViewIfNeeded();
  await page.screenshot({ path: "../dev/previews/refresh-rate-picker.png" });
  await page
    .getByRole("button", { name: "Setup speichern", exact: true })
    .click();
  expect((await state(page)).settings.profiles[0].monitors![0]).toMatchObject({
    refreshRate: 144,
    width: 1440,
    height: 2560,
    rotation: 2,
  });
});

test("displaying a near-integer current rate does not round or overwrite its saved value", async ({
  page,
}) => {
  const hz = await openEditor(page, 143.998);
  await expect(hz).toHaveValue("144");
  await expect(hz.locator("option:checked")).toHaveText("144 Hz (aktuell)");
  await expect(hz.locator('option[value="143.998"]')).toHaveCount(0);
  await page
    .getByRole("button", { name: "Setup speichern", exact: true })
    .click();
  expect(
    (await state(page)).settings.profiles[0].monitors![0].refreshRate,
  ).toBe(143.998);
});

test("unreported stored rates stay intact and manual entry remains optional", async ({
  page,
}) => {
  const hz = await openEditor(page, 73.5);
  await expect(hz).toHaveValue("custom");
  const manual = page.getByRole("spinbutton", {
    name: "Demo Monitor Hz manuell",
    exact: true,
  });
  await expect(manual).toHaveValue("73.5");
  await hz.selectOption("120");
  await expect(manual).toHaveCount(0);
  await hz.selectOption("custom");
  await manual.fill("72.25");
  await page
    .getByRole("button", { name: "Setup speichern", exact: true })
    .click();
  expect(
    (await state(page)).settings.profiles[0].monitors![0].refreshRate,
  ).toBe(72.25);
});

test("missing modes and custom resolutions offer no invented presets and preserve the existing rate", async ({
  page,
}) => {
  const hz = await openEditor(page, 59.951, true);
  await expect(hz.locator("option")).toHaveText(["Benutzerdefiniert …"]);
  await expect(
    page.getByText("Für diese Auflösung wurden keine Hz-Werte gemeldet.", {
      exact: false,
    }),
  ).toBeVisible();
  const resolution = page.getByRole("combobox", {
    name: "Demo Monitor Auflösung",
    exact: true,
  });
  await resolution.selectOption("custom");
  await page
    .getByRole("spinbutton", { name: "Demo Monitor width", exact: true })
    .fill("3000");
  await expect(hz.locator("option")).toHaveText(["Benutzerdefiniert …"]);
  await expect(
    page.getByRole("spinbutton", {
      name: "Demo Monitor Hz manuell",
      exact: true,
    }),
  ).toHaveValue("59.951");
  await page
    .getByRole("button", { name: "Setup speichern", exact: true })
    .click();
  expect(
    (await state(page)).settings.profiles[0].monitors![0].refreshRate,
  ).toBe(59.951);
});
