import { test, expect } from "@playwright/test";

test("legacy demo favorites survive the rename and remain editable", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Audiofavorit hinzufügen" }).click();
  await page
    .getByRole("textbox", { name: "Favoritenname" })
    .fill("Bestehendes Headset");
  await page
    .getByRole("combobox", { name: "Standard-Ausgabe", exact: true })
    .selectOption("demo-headset");
  await page.getByRole("button", { name: "Audiofavorit speichern" }).click();
  await page.evaluate(() => {
    localStorage.setItem(
      "setupswitch-demo",
      localStorage.getItem("setuppulse-demo")!,
    );
    localStorage.removeItem("setuppulse-demo");
  });
  await page.reload();
  await expect(page).toHaveTitle("SetupPulse");
  await page
    .getByRole("button", {
      name: "Bestehendes Headset Kein Shortcut",
      exact: true,
    })
    .click();
  await expect(
    page.getByRole("combobox", { name: "Standard-Ausgabe", exact: true }),
  ).toHaveValue("demo-headset");
  await page
    .getByRole("textbox", { name: "Favoritenname" })
    .fill("Weiterverwendetes Headset");
  await page.getByRole("button", { name: "Audiofavorit speichern" }).click();
  await page.reload();
  await expect(
    page.getByRole("button", {
      name: "Weiterverwendetes Headset Kein Shortcut",
      exact: true,
    }),
  ).toBeVisible();
});

test("number pad shortcuts keep their physical key when saved", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Audiofavorit hinzufügen" }).click();
  await page
    .getByRole("textbox", { name: "Favoritenname" })
    .fill("Number pad headset");
  await page
    .getByRole("combobox", { name: "Standard-Ausgabe", exact: true })
    .selectOption("demo-headset");
  const shortcut = page.getByRole("textbox", { name: "Tastenkombination" });
  await shortcut.focus();
  await shortcut.dispatchEvent("keydown", {
    key: "2",
    code: "Numpad2",
    ctrlKey: true,
    altKey: true,
  });
  await expect(shortcut).toHaveValue("Ctrl+Alt+Num2");
  await page.getByRole("button", { name: "Audiofavorit speichern" }).click();
  const favorite = await page.evaluate(
    () =>
      JSON.parse(localStorage.getItem("setuppulse-demo")!).audioFavorites[0],
  );
  expect(favorite.shortcut).toBe("Ctrl+Alt+Num2");
  await page
    .getByRole("button", {
      name: "Number pad headset Ctrl+Alt+Num2",
      exact: true,
    })
    .click();
  await expect(
    page.getByRole("textbox", { name: "Tastenkombination" }),
  ).toHaveValue("Ctrl+Alt+Num2");
});

test("resolution presets, manual dimensions and portrait orientation", async ({
  page,
}) => {
  await page.goto("/");
  await page
    .getByRole("button", { name: "Setup erstellen", exact: true })
    .click();
  const resolution = page.getByRole("combobox", {
    name: "Demo Monitor Auflösung",
    exact: true,
  });
  await expect(resolution).toHaveValue("2560x1440");
  await expect(resolution.locator("option:checked")).toContainText(
    "(empfohlen)",
  );
  await expect(
    page.getByRole("spinbutton", { name: "Demo Monitor width", exact: true }),
  ).toHaveCount(0);
  await resolution.selectOption("3840x2160");
  await expect(resolution.locator("option:checked")).toContainText("16:9");
  await resolution.selectOption("custom");
  await page
    .getByRole("spinbutton", { name: "Demo Monitor width", exact: true })
    .fill("3440");
  await page
    .getByRole("spinbutton", { name: "Demo Monitor height", exact: true })
    .fill("1440");
  await expect(resolution).toHaveValue("custom");
  await resolution.selectOption("1920x1080");
  await expect(
    page.getByRole("spinbutton", { name: "Demo Monitor width", exact: true }),
  ).toHaveCount(0);
  await page
    .getByRole("combobox", { name: "Demo Monitor Ausrichtung", exact: true })
    .selectOption("2");
  await expect(resolution).toHaveValue("1080x1920");
  await expect(resolution.locator("option:checked")).toContainText("9:16");
  await page.screenshot({
    path: "../dev/previews/preview-resolution-picker.png",
    fullPage: true,
  });
  await page
    .getByRole("button", { name: "Setup speichern", exact: true })
    .click();
  const saved = await page.evaluate(
    () =>
      JSON.parse(localStorage.getItem("setuppulse-demo")!).profiles[0]
        .monitors[0],
  );
  expect(saved).toMatchObject({ width: 1080, height: 1920, rotation: 2 });
});

test("direct setup audio and optional favorite keep independent selections and capture physical digit", async ({
  page,
}) => {
  await page.goto("/");
  await page
    .getByRole("button", { name: "Setup erstellen", exact: true })
    .click();
  const shortcut = page.getByRole("textbox", { name: "Tastenkombination" });
  await shortcut.focus();
  await page.keyboard.down("Control");
  await page.keyboard.down("Alt");
  await shortcut.dispatchEvent("keydown", {
    key: "}",
    code: "Digit0",
    ctrlKey: true,
    altKey: true,
  });
  await page.keyboard.up("Alt");
  await page.keyboard.up("Control");
  await expect(shortcut).toHaveValue("Ctrl+Alt+0");
  await page
    .getByRole("combobox", { name: "Setup-Ausgabe", exact: true })
    .selectOption("demo-headset");
  await page
    .getByRole("textbox", { name: "Audio-Anzeigename", exact: true })
    .fill("TV Sound");
  await page
    .getByRole("button", { name: "Als Audiofavorit speichern", exact: true })
    .click();
  await expect(
    page.getByRole("textbox", { name: "Favoritenname" }),
  ).toHaveValue("TV Sound");
  await expect(
    page.getByRole("combobox", { name: "Standard-Ausgabe", exact: true }),
  ).toHaveValue("demo-headset");
  await page.getByRole("button", { name: "Audiofavorit speichern" }).click();
  await expect(
    page.getByRole("combobox", { name: "Setup-Ausgabe", exact: true }),
  ).toHaveValue("demo-headset");
  await page.getByRole("button", { name: "Setup speichern" }).click();
  const settings = await page.evaluate(() =>
    JSON.parse(localStorage.getItem("setuppulse-demo")!),
  );
  expect(settings.profiles[0].audioFavoriteId).toBeNull();
  expect(settings.profiles[0].audio.output.deviceId).toBe("demo-headset");
  expect(settings.profiles[0].audio.name).toBe("TV Sound");
  expect(settings.audioFavorites[0].output.deviceId).toBe("demo-headset");
});

test("empty first run and appearance", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByText("Dein erstes Setup beginnt hier")).toBeVisible();
  await page.screenshot({
    path: "../dev/previews/preview-first-run.png",
    fullPage: true,
  });
  await page
    .getByRole("button", { name: "Einstellungen", exact: true })
    .click();
  await page.getByRole("button", { name: "Bearbeiten", exact: true }).click();
  await page.getByRole("dialog").getByRole("combobox").selectOption("dark");
  await page.getByRole("button", { name: "Einstellungen speichern" }).click();
  await expect(page.locator("html")).toHaveClass("dark");
  await page.getByRole("button", { name: "Workspace", exact: true }).click();
  await page.screenshot({
    path: "../dev/previews/preview-dark.png",
    fullPage: true,
  });
});
test("linked setups and independent audio favorites", async ({ page }) => {
  const errors: string[] = [];
  page.on("pageerror", (e) => errors.push(e.message));
  await page.goto("/");
  await page.getByRole("button", { name: "Audiofavorit hinzufügen" }).click();
  await page.getByRole("textbox", { name: "Favoritenname" }).fill("Headset");
  await page
    .getByRole("combobox", { name: "Standard-Ausgabe", exact: true })
    .selectOption("demo-headset");
  await page
    .getByRole("textbox", { name: "Tastenkombination" })
    .press("Control+Alt+H");
  await page.getByRole("button", { name: "Audiofavorit speichern" }).click();
  await page
    .getByRole("button", { name: "Setup erstellen", exact: true })
    .click();
  await page
    .getByRole("textbox", { name: "Setupname" })
    .fill("Mein Arbeitsplatz");
  await page
    .getByRole("combobox", { name: "Audio im Setup" })
    .selectOption({ label: "Headset" });
  await page.getByRole("button", { name: "Setup speichern" }).click();
  await page.getByRole("button", { name: "Aktivieren", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Ist das Bild richtig?" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Behalten · Enter" }).click();
  const before = await page.evaluate(
    () => JSON.parse(localStorage.getItem("setuppulse-demo")!).profiles[0],
  );
  await page.getByRole("button", { name: "Audiofavorit hinzufügen" }).click();
  await page
    .getByRole("textbox", { name: "Favoritenname" })
    .fill("Lautsprecher");
  await page
    .getByRole("combobox", { name: "Standard-Ausgabe", exact: true })
    .selectOption("demo-speakers");
  await page.getByRole("button", { name: "Audiofavorit speichern" }).click();
  await page
    .getByRole("button", { name: "Wechseln", exact: true })
    .last()
    .click();
  await expect(
    page.getByText("Audio wurde separat geändert.", { exact: false }),
  ).toBeVisible();
  expect(
    await page.evaluate(
      () => JSON.parse(localStorage.getItem("setuppulse-demo")!).profiles[0],
    ),
  ).toEqual(before);
  await page.screenshot({
    path: "../dev/previews/preview-workspace.png",
    fullPage: true,
  });
  expect(errors).toEqual([]);
});
