import { test, expect } from "@playwright/test";

test("Windows startup and minimized startup are independent persisted preferences", async ({
  page,
}) => {
  await page.goto("/");
  await page
    .getByRole("button", { name: "Einstellungen", exact: true })
    .click();
  await page.getByRole("button", { name: "Bearbeiten", exact: true }).click();
  const autostart = page.getByRole("switch", {
    name: "Autostart",
    exact: true,
  });
  const minimized = page.getByRole("switch", {
    name: "Minimierter Windows-Start",
    exact: true,
  });
  await expect(autostart).not.toBeChecked();
  await expect(minimized).toBeDisabled();
  await expect(minimized).toBeChecked();
  await autostart.click();
  await expect(minimized).toBeEnabled();
  await minimized.click();
  await page.getByRole("button", { name: "Einstellungen speichern" }).click();
  await page.reload();
  await page
    .getByRole("button", { name: "Einstellungen", exact: true })
    .click();
  await page.getByRole("button", { name: "Bearbeiten", exact: true }).click();
  await expect(autostart).toBeChecked();
  await expect(minimized).not.toBeChecked();
  await autostart.click();
  await expect(minimized).toBeDisabled();
  await autostart.click();
  await expect(minimized).not.toBeChecked();
  await minimized.click();
  await page.getByRole("button", { name: "Einstellungen speichern" }).click();
  const preferences = await page.evaluate(
    () => JSON.parse(localStorage.getItem("setuppulse-demo")!).preferences,
  );
  expect(preferences.startWithWindows).toBe(true);
  expect(preferences.startMinimized).toBe(true);
});
