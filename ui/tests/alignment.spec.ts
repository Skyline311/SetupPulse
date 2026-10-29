import { test, expect, type Page } from "@playwright/test";
import { readFileSync } from "node:fs";
const features = JSON.parse(
  readFileSync(new URL("../../features.json", import.meta.url), "utf8"),
);

test.skip(
  !features.liveMonitorAlignment,
  "Live height alignment is temporarily disabled; retained for reactivation.",
);

test("monitors excluded from the setup stay locked and unchanged during live alignment", async ({
  page,
}) => {
  await page.goto("/?demoMonitors=3");
  const before = await currentState(page);
  await page
    .getByRole("button", { name: "Setup erstellen", exact: true })
    .click();
  const editor = page.getByRole("dialog", { name: "Setup bearbeiten" });
  await editor
    .getByRole("switch", { name: "Demo Rechts aktiv", exact: true })
    .click();
  await expect(
    editor.getByText("Im Setup ausgeschaltet", { exact: true }),
  ).toBeVisible();
  await editor
    .getByRole("button", { name: "Monitorhöhe live abgleichen" })
    .click();
  await expect(
    page.getByRole("spinbutton", { name: "Monitor 3 Y", exact: true }),
  ).toBeDisabled();
  await expect(
    page.getByRole("slider", { name: "Monitor 3 Höhe", exact: true }),
  ).toBeDisabled();
  await page
    .getByRole("spinbutton", { name: "Monitor 2 Y", exact: true })
    .fill("-60");
  await page.getByRole("button", { name: "Anordnung behalten" }).click();
  await expect(editor).toBeVisible();
  await expect(
    editor.getByRole("switch", { name: "Demo Rechts aktiv", exact: true }),
  ).not.toBeChecked();
  const after = await currentState(page);
  expect(after.inventory.displays[2]).toEqual(before.inventory.displays[2]);
  expect(after.inventory.displays[1].current.y).toBe(-60);
});

test("a native alignment failure stays readable when the setup editor returns", async ({
  page,
}) => {
  await page.goto("/?demoMonitors=3");
  const initial = await currentState(page);
  await page.addInitScript((hostState) => {
    const listeners: ((event: { data: any }) => void)[] = [];
    const emit = (data: any) =>
      listeners.forEach((listener) => listener({ data }));
    Object.defineProperty(window, "chrome", {
      configurable: true,
      value: {
        webview: {
          addEventListener: (
            _: string,
            listener: (event: { data: any }) => void,
          ) => listeners.push(listener),
          postMessage: (request: { id: string; method: string }) => {
            const response: { id: string; result?: any; error?: string } = {
              id: request.id,
              result: true,
            };
            if (request.method === "state")
              response.result = structuredClone(hostState);
            if (request.method === "beginAlignment") {
              const displays = hostState.inventory.displays
                .filter((display: any) => display.active)
                .map((display: any) => display.current);
              hostState.pending = {
                id: "alignment-failure",
                deadline: new Date(Date.now() + 90000).toISOString(),
                liveAlignment: true,
                displays,
                requestedDisplays: displays,
              };
            }
            if (request.method === "updateAlignment") {
              hostState.pending = null;
              queueMicrotask(() =>
                emit({ event: "state", data: structuredClone(hostState) }),
              );
              response.error = "DPI-Bereich wird nicht unterstützt.";
            }
            queueMicrotask(() => emit(response));
          },
        },
      },
    });
  }, initial);
  await page.reload();
  await page
    .getByRole("button", { name: "Setup erstellen", exact: true })
    .click();
  const editor = page.getByRole("dialog", { name: "Setup bearbeiten" });
  await editor
    .getByRole("textbox", { name: "Setupname", exact: true })
    .fill("Büro Entwurf");
  await editor
    .getByRole("button", { name: "Monitorhöhe live abgleichen" })
    .click();
  await page
    .getByRole("spinbutton", { name: "Monitor 2 Y", exact: true })
    .fill("-90");
  await expect(editor).toBeVisible();
  await expect(editor.getByRole("alert")).toContainText(
    "DPI-Bereich wird nicht unterstützt.",
  );
  await expect(editor.getByRole("alert")).toBeInViewport();
  await expect(
    editor.getByRole("textbox", { name: "Setupname", exact: true }),
  ).toHaveValue("Büro Entwurf");
});

async function currentState(page: Page) {
  return page.evaluate(async () => {
    const modulePath = "/src/lib/bridge.ts";
    return (await import(modulePath)).call("state");
  });
}

async function openAlignment(page: Page) {
  await page
    .getByRole("button", { name: "Setup erstellen", exact: true })
    .click();
  await page
    .getByRole("dialog", { name: "Setup bearbeiten" })
    .getByRole("button", { name: "Monitorhöhe live abgleichen" })
    .click();
}

test("alignment from the setup editor keeps the draft and copies confirmed heights", async ({
  page,
}) => {
  await page.goto("/?demoMonitors=3");
  await expect(
    page.getByRole("button", { name: "Monitorhöhe live abgleichen" }),
  ).toHaveCount(0);
  await page
    .getByRole("button", { name: "Setup erstellen", exact: true })
    .click();
  const editor = page.getByRole("dialog", { name: "Setup bearbeiten" });
  await editor.getByRole("textbox", { name: "Setupname" }).fill("Mein Entwurf");
  await editor
    .getByRole("textbox", { name: "Tastenkombination" })
    .fill("Ctrl+Alt+8");
  await editor
    .getByRole("spinbutton", { name: "Demo Links x", exact: true })
    .fill("-1910");
  await editor
    .getByRole("button", { name: "Monitorhöhe live abgleichen" })
    .click();
  await expect(editor).not.toBeVisible();
  await page
    .getByRole("spinbutton", { name: "Monitor 2 Y", exact: true })
    .fill("-70");
  await page.getByRole("button", { name: "Anordnung behalten" }).click();
  await expect(editor).toBeVisible();
  await expect(editor.getByRole("textbox", { name: "Setupname" })).toHaveValue(
    "Mein Entwurf",
  );
  await expect(
    editor.getByRole("textbox", { name: "Tastenkombination" }),
  ).toHaveValue("Ctrl+Alt+8");
  await expect(
    editor.getByRole("spinbutton", { name: "Demo Links y", exact: true }),
  ).toHaveValue("-70");
  await expect(
    editor.getByRole("spinbutton", { name: "Demo Links x", exact: true }),
  ).toHaveValue("-1910");
  expect((await currentState(page)).settings.profiles).toHaveLength(0);
  await editor
    .getByRole("spinbutton", { name: "Demo Links x", exact: true })
    .fill("-1920");
  await editor
    .getByRole("button", { name: "Setup speichern", exact: true })
    .click();
  expect((await currentState(page)).settings.profiles[0].monitors[1].y).toBe(
    -70,
  );
});

test("cancel and timeout return to the unchanged setup draft", async ({
  page,
}) => {
  await page.clock.install();
  await page.goto("/?demoMonitors=3");
  const before = await currentState(page);
  await page
    .getByRole("button", { name: "Setup erstellen", exact: true })
    .click();
  const editor = page.getByRole("dialog", { name: "Setup bearbeiten" });
  await editor
    .getByRole("textbox", { name: "Setupname" })
    .fill("Nicht verlieren");
  await editor
    .getByRole("spinbutton", { name: "Demo Links y", exact: true })
    .fill("-25");
  for (const timeout of [false, true]) {
    await editor
      .getByRole("button", { name: "Monitorhöhe live abgleichen" })
      .click();
    await page
      .getByRole("spinbutton", { name: "Monitor 2 Y", exact: true })
      .fill("-90");
    await page.clock.fastForward(1000);
    await expect
      .poll(
        async () => (await currentState(page)).inventory.displays[1].current.y,
      )
      .toBe(-90);
    if (timeout) await page.clock.fastForward(90001);
    else
      await page
        .getByRole("button", { name: "Abbrechen & zurücksetzen" })
        .click();
    await expect(editor).toBeVisible();
    await expect(
      editor.getByRole("textbox", { name: "Setupname" }),
    ).toHaveValue("Nicht verlieren");
    await expect(
      editor.getByRole("spinbutton", { name: "Demo Links y", exact: true }),
    ).toHaveValue("-25");
    expect((await currentState(page)).inventory).toEqual(before.inventory);
    expect((await currentState(page)).settings.profiles).toHaveLength(0);
  }
});

test("live Y updates preserve audio and keeping immediately applies the latest value", async ({
  page,
}) => {
  await page.goto("/?demoMonitors=3");
  const before = await currentState(page);
  await openAlignment(page);
  await expect(
    page.getByRole("heading", { name: "Monitorhöhe live abgleichen" }),
  ).toBeVisible();
  await expect(
    page.getByRole("spinbutton", { name: "Monitor 1 Y", exact: true }),
  ).toBeDisabled();
  await page
    .getByRole("spinbutton", { name: "Monitor 2 Y", exact: true })
    .fill("-80");
  await expect
    .poll(
      async () => (await currentState(page)).inventory.displays[1].current.y,
    )
    .toBe(-80);
  await page
    .getByRole("spinbutton", { name: "Monitor 3 Y", exact: true })
    .fill("180");
  await page.getByRole("button", { name: "Anordnung behalten" }).click();
  await expect(
    page.getByRole("heading", { name: "Monitorhöhe live abgleichen" }),
  ).toHaveCount(0);
  const after = await currentState(page);
  expect(after.pending).toBeNull();
  expect(after.inventory.displays[2].current.y).toBe(180);
  expect(after.inventory.audio).toEqual(before.inventory.audio);
  expect(after.settings.profiles).toEqual(before.settings.profiles);
});

test("cancel restores the original heights after multiple live changes", async ({
  page,
}) => {
  await page.goto("/?demoMonitors=3");
  const before = await currentState(page);
  await openAlignment(page);
  const y = page.getByRole("spinbutton", { name: "Monitor 2 Y", exact: true });
  await y.fill("50");
  await expect
    .poll(
      async () => (await currentState(page)).inventory.displays[1].current.y,
    )
    .toBe(50);
  await y.fill("-125");
  await expect
    .poll(
      async () => (await currentState(page)).inventory.displays[1].current.y,
    )
    .toBe(-125);
  await page.getByRole("button", { name: "Abbrechen & zurücksetzen" }).click();
  await expect(
    page.getByRole("heading", { name: "Monitorhöhe live abgleichen" }),
  ).toHaveCount(0);
  expect((await currentState(page)).inventory).toEqual(before.inventory);
});

test("inactivity automatically restores the original heights", async ({
  page,
}) => {
  await page.clock.install();
  await page.goto("/?demoMonitors=3");
  const before = await currentState(page);
  await openAlignment(page);
  await page
    .getByRole("spinbutton", { name: "Monitor 2 Y", exact: true })
    .fill("40");
  await page.clock.fastForward(1000);
  await expect
    .poll(
      async () => (await currentState(page)).inventory.displays[1].current.y,
    )
    .toBe(40);
  await page.clock.fastForward(90001);
  await expect(
    page.getByRole("heading", { name: "Monitorhöhe live abgleichen" }),
  ).toHaveCount(0);
  expect((await currentState(page)).inventory).toEqual(before.inventory);
});

test("disconnected positions stay unapplied and can be corrected", async ({
  page,
}) => {
  await page.goto("/?demoMonitors=3");
  await openAlignment(page);
  const y = page.getByRole("spinbutton", { name: "Monitor 2 Y", exact: true });
  await y.fill("9999");
  await expect(
    page.getByRole("status").filter({ hasText: "Noch nicht angewendet" }),
  ).toBeVisible();
  expect((await currentState(page)).inventory.displays[1].current.y).toBe(100);
  await expect(
    page.getByRole("button", { name: "Anordnung behalten" }),
  ).toBeDisabled();
  await y.fill("60");
  await expect
    .poll(
      async () => (await currentState(page)).inventory.displays[1].current.y,
    )
    .toBe(60);
  await expect(
    page.getByRole("button", { name: "Anordnung behalten" }),
  ).toBeEnabled();
});
