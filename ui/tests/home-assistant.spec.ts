import { test, expect } from "@playwright/test";

test("MQTT stays optional, preserves edits during status updates and never exports credentials with profiles", async ({
  page,
}) => {
  await page.goto("/");
  await page
    .getByRole("button", { name: "Einstellungen", exact: true })
    .click();
  const enabled = page.getByRole("switch", {
    name: "Home Assistant aktivieren",
    exact: true,
  });
  await expect(enabled).not.toBeChecked();
  await enabled.click();
  await page
    .getByRole("textbox", { name: "MQTT-Broker", exact: true })
    .fill("192.168.1.20");
  await page
    .getByRole("textbox", { name: "MQTT-Benutzername", exact: true })
    .fill("test-only-user");
  const password = page.getByRole("textbox", {
    name: "MQTT-Passwort",
    exact: true,
  });
  await password.fill("test-only-password");
  await expect(password).toHaveAttribute("type", "password");
  await page.getByRole("switch", { name: "MQTT TLS", exact: true }).click();
  await expect(
    page.getByRole("spinbutton", { name: "MQTT-Port", exact: true }),
  ).toHaveValue("8883");
  await page
    .getByRole("button", { name: "MQTT-Verbindung speichern", exact: true })
    .click();
  await expect(
    page.getByText("Verbindung: Demo – keine Verbindung", { exact: true }),
  ).toBeVisible();
  await expect(password).toHaveValue("");
  await expect(password).toHaveAttribute(
    "placeholder",
    "Gespeichertes Passwort beibehalten",
  );
  expect(await page.evaluate(() => JSON.stringify(localStorage))).not.toContain(
    "test-only-password",
  );
  await page
    .getByRole("textbox", { name: "Home-Assistant-Gerätename", exact: true })
    .fill("Mein Büro-PC");
  await page
    .getByRole("button", { name: "MQTT-Status aktualisieren", exact: true })
    .click();
  await expect(
    page.getByRole("textbox", {
      name: "Home-Assistant-Gerätename",
      exact: true,
    }),
  ).toHaveValue("Mein Büro-PC");
  await page
    .getByRole("button", { name: "MQTT-Verbindung speichern", exact: true })
    .click();
  await page
    .getByRole("button", {
      name: "Gespeichertes Passwort entfernen",
      exact: true,
    })
    .click();
  await page
    .getByRole("button", { name: "MQTT-Verbindung speichern", exact: true })
    .click();
  await expect(password).toHaveAttribute("placeholder", "Optional");
  await enabled.click();
  await page
    .getByRole("button", { name: "MQTT-Verbindung speichern", exact: true })
    .click();
  await expect(
    page.getByText("Verbindung: Deaktiviert", { exact: true }),
  ).toBeVisible();
  await page
    .getByRole("switch", { name: "Home Assistant aktivieren", exact: true })
    .scrollIntoViewIfNeeded();
  await page.screenshot({
    path: "../dev/previews/home-assistant-settings.png",
  });
});

test("invalid MQTT settings keep the entered form readable for correction", async ({
  page,
}) => {
  await page.goto("/");
  await page
    .getByRole("button", { name: "Einstellungen", exact: true })
    .click();
  await page
    .getByRole("switch", { name: "Home Assistant aktivieren", exact: true })
    .click();
  await page
    .getByRole("button", { name: "MQTT-Verbindung speichern", exact: true })
    .click();
  await expect(page.getByRole("alert")).toContainText("MQTT-Hostnamen");
  await expect(
    page.getByRole("switch", {
      name: "Home Assistant aktivieren",
      exact: true,
    }),
  ).toBeChecked();
  await page
    .getByRole("textbox", { name: "MQTT-Broker", exact: true })
    .fill("localhost");
  await page
    .getByRole("button", { name: "MQTT-Verbindung speichern", exact: true })
    .click();
  await expect(page.getByRole("alert")).toHaveCount(0);
});
