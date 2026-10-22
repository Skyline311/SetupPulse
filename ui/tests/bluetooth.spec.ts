import { test, expect } from "@playwright/test";

test("Bluetooth inventory distinguishes started, disabled and failed adapters without changing hardware state", async ({
  page,
}) => {
  await page.goto("/");
  const before = await page.evaluate(async () => {
    const modulePath = "/src/lib/bridge.ts";
    return (await import(modulePath)).call("state");
  });
  await page
    .getByRole("button", { name: "Einstellungen", exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: "Bluetooth-Adapter", exact: true }),
  ).toBeVisible();
  await expect(page.getByText("Demo-Adapter 1", { exact: true })).toBeVisible();
  await expect(page.getByText("Gestartet", { exact: true })).toBeVisible();
  await expect(page.getByText("Deaktiviert", { exact: true })).toBeVisible();
  await expect(
    page.getByText("Windows-Fehler 31", { exact: true }),
  ).toBeVisible();
  await page
    .getByRole("button", {
      name: "Bluetooth-Adapter aktualisieren",
      exact: true,
    })
    .click();
  await expect(
    page.getByText("Windows-Fehler 31", { exact: true }),
  ).toBeVisible();
  const after = await page.evaluate(async () => {
    const modulePath = "/src/lib/bridge.ts";
    return (await import(modulePath)).call("state");
  });
  expect(after.settings).toEqual(before.settings);
  expect(after.inventory).toEqual(before.inventory);
  await page.screenshot({
    path: "../dev/previews/bluetooth-adapters.png",
    fullPage: true,
  });
});
