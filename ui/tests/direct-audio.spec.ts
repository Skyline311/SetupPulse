import { test, expect } from "@playwright/test";

test("setup can save, reopen and activate a detected device without any favorite", async ({
  page,
}) => {
  await page.goto("/");
  await page
    .getByRole("button", { name: "Setup erstellen", exact: true })
    .click();
  await page.getByRole("textbox", { name: "Setupname" }).fill("Direkter Sound");
  await page
    .getByRole("switch", { name: "Monitoranordnung anwenden", exact: true })
    .click();
  const output = page.getByRole("combobox", {
    name: "Setup-Ausgabe",
    exact: true,
  });
  const mic = page.getByRole("combobox", {
    name: "Setup-Mikrofon",
    exact: true,
  });
  await expect(output.locator('option[value="demo-mic"]')).toHaveCount(0);
  await expect(mic.locator('option[value="demo-headset"]')).toHaveCount(0);
  await output.selectOption("demo-headset");
  await mic.selectOption("demo-mic");
  await page
    .getByRole("button", { name: "Setup speichern", exact: true })
    .click();
  const saved = await page.evaluate(() =>
    JSON.parse(localStorage.getItem("setuppulse-demo")!),
  );
  expect(saved.audioFavorites).toEqual([]);
  expect(saved.profiles[0]).toMatchObject({
    audioFavoriteId: null,
    audio: {
      output: { deviceId: "demo-headset", volume: null, muted: null },
      input: { deviceId: "demo-mic" },
      name: null,
    },
  });
  await page.reload();
  await expect(page.getByText(/Demo Headset · Demo Mikrofon/)).toBeVisible();
  await page.getByRole("button", { name: "Aktivieren", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Ist das Bild richtig?" }),
  ).toHaveCount(0);
  await expect(
    page.getByText("Demo Headset", { exact: true }).last(),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "Direkter Sound bearbeiten", exact: true })
    .click();
  await expect(
    page.getByRole("combobox", { name: "Setup-Ausgabe", exact: true }),
  ).toHaveValue("demo-headset");
  await expect(
    page.getByRole("combobox", { name: "Setup-Mikrofon", exact: true }),
  ).toHaveValue("demo-mic");
  await page
    .getByRole("textbox", { name: "Audio-Anzeigename", exact: true })
    .fill("Mein Sound");
  await page
    .getByRole("textbox", { name: "Audio-Anzeigename", exact: true })
    .scrollIntoViewIfNeeded();
  await page.screenshot({ path: "../dev/previews/direct-setup-audio.png" });
  await page
    .getByRole("button", { name: "Setup speichern", exact: true })
    .click();
  await expect(
    page.getByText(/Monitore unverändert\s*Mein Sound/),
  ).toBeVisible();
});

test("remembered missing device remains selectable and can be cleared to leave audio unchanged", async ({
  page,
}) => {
  await page.goto("/");
  await page
    .getByRole("button", { name: "Setup erstellen", exact: true })
    .click();
  await page
    .getByRole("combobox", { name: "Setup-Ausgabe", exact: true })
    .selectOption("demo-headset");
  await page
    .getByRole("button", { name: "Setup speichern", exact: true })
    .click();
  await page.evaluate(() => {
    const settings = JSON.parse(localStorage.getItem("setuppulse-demo")!);
    settings.profiles[0].audio.output.deviceId = "offline-tv";
    localStorage.setItem("setuppulse-demo", JSON.stringify(settings));
  });
  await page.reload();
  await page
    .getByRole("button", { name: "Neues Setup bearbeiten", exact: true })
    .click();
  const output = page.getByRole("combobox", {
    name: "Setup-Ausgabe",
    exact: true,
  });
  await expect(output).toHaveValue("offline-tv");
  await expect(output.locator("option:checked")).toHaveText(
    "Gespeichertes Gerät fehlt",
  );
  await output.selectOption("");
  await page
    .getByRole("button", { name: "Setup speichern", exact: true })
    .click();
  const saved = await page.evaluate(() =>
    JSON.parse(localStorage.getItem("setuppulse-demo")!),
  );
  expect(saved.profiles[0].audio).toBeNull();
  expect(saved.audioFavorites).toEqual([]);
});

test("recognized devices can be favorited with their endpoint already selected", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Audio", exact: true }).click();
  await page
    .getByRole("button", { name: "Demo Mikrofon favorisieren", exact: true })
    .click();
  await expect(
    page.getByRole("textbox", { name: "Favoritenname" }),
  ).toHaveValue("Demo Mikrofon");
  await expect(
    page.getByRole("combobox", { name: "Standard-Eingabe", exact: true }),
  ).toHaveValue("demo-mic");
  await expect(
    page.getByRole("combobox", { name: "Standard-Ausgabe", exact: true }),
  ).toHaveValue("");
  await page
    .getByRole("button", { name: "Audiofavorit speichern", exact: true })
    .click();
  const settings = await page.evaluate(() =>
    JSON.parse(localStorage.getItem("setuppulse-demo")!),
  );
  expect(settings.profiles).toEqual([]);
  expect(settings.audioFavorites[0].input.deviceId).toBe("demo-mic");
  await expect(
    page.getByRole("button", {
      name: "Demo Mikrofon favorisieren",
      exact: true,
    }),
  ).toHaveCount(0);
  await expect(
    page.getByRole("button", {
      name: "Audiofavorit Demo Mikrofon bearbeiten",
      exact: true,
    }),
  ).toBeVisible();
});

for (const [device, endpoint] of [
  ["Demo Lautsprecher", "demo-speakers"],
  ["Demo Headset", "demo-headset"],
]) {
  test(`recognized ${device} opens its renamed favorite without creating a duplicate`, async ({
    page,
  }) => {
    await page.goto("/");
    await page.getByRole("button", { name: "Audio", exact: true }).click();
    await page
      .getByRole("button", { name: `${device} favorisieren`, exact: true })
      .click();
    await page
      .getByRole("textbox", { name: "Favoritenname" })
      .fill("Mein Sound");
    await page
      .getByRole("button", { name: "Audiofavorit speichern", exact: true })
      .click();
    const saved = await page.evaluate(
      () => JSON.parse(localStorage.getItem("setuppulse-demo")!).audioFavorites,
    );
    await page.reload();
    await page.getByRole("button", { name: "Audio", exact: true }).click();
    await expect(
      page.getByRole("button", { name: `${device} favorisieren`, exact: true }),
    ).toHaveCount(0);
    const existing = page.getByRole("button", {
      name: "Audiofavorit Mein Sound bearbeiten",
      exact: true,
    });
    await expect(existing).toHaveText("Mein Sound");
    await page.screenshot({ path: `../dev/previews/favorite-${endpoint}.png` });
    await existing.click();
    await expect(
      page.getByRole("textbox", { name: "Favoritenname" }),
    ).toHaveValue("Mein Sound");
    await expect(
      page.getByRole("combobox", { name: "Standard-Ausgabe", exact: true }),
    ).toHaveValue(endpoint);
    await page
      .getByRole("button", { name: "Audiofavorit speichern", exact: true })
      .click();
    const reopened = await page.evaluate(
      () => JSON.parse(localStorage.getItem("setuppulse-demo")!).audioFavorites,
    );
    expect(reopened).toEqual(saved);
  });
}

test("all favorites referencing a communications device are shown, with input and output kept distinct", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Audio", exact: true }).click();
  await page
    .getByRole("button", { name: "Demo Mikrofon favorisieren", exact: true })
    .click();
  await page
    .getByRole("button", { name: "Audiofavorit speichern", exact: true })
    .click();
  await page.evaluate(() => {
    const settings = JSON.parse(localStorage.getItem("setuppulse-demo")!);
    const original = settings.audioFavorites[0];
    settings.audioFavorites = [
      {
        ...original,
        id: "comm-a",
        name: "Kommunikation A",
        input: null,
        communicationsInput: "demo-mic",
        shortcut: "Ctrl+Alt+Num1",
      },
      {
        ...original,
        id: "comm-b",
        name: "Kommunikation B",
        input: null,
        communicationsInput: "demo-mic",
      },
      {
        ...original,
        id: "other-direction",
        name: "Andere Richtung",
        input: null,
        output: { deviceId: "demo-mic", volume: null, muted: null },
      },
    ];
    localStorage.setItem("setuppulse-demo", JSON.stringify(settings));
  });
  await page.reload();
  await page.getByRole("button", { name: "Audio", exact: true }).click();
  await expect(
    page.getByRole("button", {
      name: "Demo Mikrofon favorisieren",
      exact: true,
    }),
  ).toHaveCount(0);
  await expect(
    page.getByRole("button", {
      name: "Audiofavorit Andere Richtung bearbeiten",
      exact: true,
    }),
  ).toHaveCount(0);
  await expect(
    page.getByRole("button", {
      name: "Audiofavorit Kommunikation B bearbeiten",
      exact: true,
    }),
  ).toBeVisible();
  await page
    .getByRole("button", {
      name: "Audiofavorit Kommunikation A bearbeiten",
      exact: true,
    })
    .click();
  await expect(
    page.getByRole("combobox", { name: "Kommunikation Eingabe", exact: true }),
  ).toHaveValue("demo-mic");
  await page
    .getByRole("button", { name: "Audiofavorit speichern", exact: true })
    .click();
  const saved = await page.evaluate(
    () => JSON.parse(localStorage.getItem("setuppulse-demo")!).audioFavorites,
  );
  expect(saved).toHaveLength(3);
  expect(saved.find((f: { id: string }) => f.id === "comm-a").shortcut).toBe(
    "Ctrl+Alt+Num1",
  );
});
