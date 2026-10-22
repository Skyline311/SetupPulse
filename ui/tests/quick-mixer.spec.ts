import { test, expect } from "@playwright/test";

test("compact tray surface fits, closes with Escape, preserves setup drafts and opens full audio", async ({
  page,
}) => {
  await page.goto("/");
  const initial = await page.evaluate(async () => {
    const path = "/src/lib/bridge.ts";
    const bridge = await import(path);
    return {
      state: { ...(await bridge.call("state")), visible: false },
      mixer: await bridge.call("mixerState"),
      homeAssistant: await bridge.call("homeAssistantSettings"),
      bluetooth: await bridge.call("bluetoothState"),
    };
  });
  await page.addInitScript((initial) => {
    const listeners: ((event: { data: any }) => void)[] = [];
    const emit = (data: any) =>
      listeners.forEach((listener) => listener({ data }));
    (window as any).emitHost = emit;
    (window as any).hostRequests = [];
    Object.defineProperty(window, "chrome", {
      configurable: true,
      value: {
        webview: {
          addEventListener: (
            _: string,
            listener: (event: { data: any }) => void,
          ) => listeners.push(listener),
          postMessage: (request: { id: string; method: string }) => {
            (window as any).hostRequests.push(request.method);
            queueMicrotask(() => {
              if (
                request.method === "closeQuickMixer" ||
                request.method === "openFullMixer"
              )
                emit({
                  event: "navigate",
                  data:
                    request.method === "openFullMixer" ? "mixer" : "mainWindow",
                });
              emit({
                id: request.id,
                result:
                  request.method === "state"
                    ? initial.state
                    : request.method === "mixerState"
                      ? initial.mixer
                      : request.method === "homeAssistantSettings"
                        ? initial.homeAssistant
                        : request.method === "bluetoothState"
                          ? initial.bluetooth
                          : true,
              });
            });
          },
        },
      },
    });
  }, initial);
  await page.reload();
  await page
    .getByRole("button", { name: "Einstellungen", exact: true })
    .click();
  await page
    .getByRole("textbox", { name: "MQTT-Broker", exact: true })
    .fill("draft-broker.local");
  await page
    .getByRole("textbox", { name: "MQTT-Passwort", exact: true })
    .fill("draft-only-password");
  await page.evaluate(() =>
    (window as any).emitHost({ event: "navigate", data: "quickMixer" }),
  );
  await expect(
    page.getByRole("dialog", { name: "Audio-Schnellmixer", exact: true }),
  ).toBeVisible();
  await page.evaluate(() =>
    (window as any).emitHost({ event: "navigate", data: "mainWindow" }),
  );
  await expect(
    page.getByRole("textbox", { name: "MQTT-Broker", exact: true }),
  ).toHaveValue("draft-broker.local");
  await expect(
    page.getByRole("textbox", { name: "MQTT-Passwort", exact: true }),
  ).toHaveValue("draft-only-password");
  await page.getByRole("button", { name: "Workspace", exact: true }).click();
  await page
    .getByRole("button", { name: "Setup erstellen", exact: true })
    .click();
  await page
    .getByRole("textbox", { name: "Setupname", exact: true })
    .fill("Unsaved office");
  await page.evaluate(() =>
    (window as any).emitHost({ event: "navigate", data: "quickMixer" }),
  );
  await page.evaluate(() =>
    (window as any).emitHost({ event: "windowVisibility", data: true }),
  );
  await page.setViewportSize({ width: 460, height: 600 });
  await expect(
    page.getByRole("dialog", { name: "Audio-Schnellmixer" }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Workspace", exact: true }),
  ).toHaveCount(0);
  await expect(
    page.getByRole("slider", {
      name: "Demo Lautsprecher Lautstärke",
      exact: true,
    }),
  ).toBeVisible();
  await expect(
    page.getByRole("region", { name: "Demo Mikrofon", exact: true }),
  ).toHaveCount(0);
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await page.screenshot({ path: "../dev/previews/tray-quick-mixer.png" });
  await page.keyboard.press("Escape");
  await expect(
    page.getByRole("textbox", { name: "Setupname", exact: true }),
  ).toHaveValue("Unsaved office");
  expect(await page.evaluate(() => (window as any).hostRequests)).toContain(
    "closeQuickMixer",
  );
  await page.evaluate(() =>
    (window as any).emitHost({ event: "navigate", data: "quickMixer" }),
  );
  await page.getByRole("button", { name: "Voller Mixer", exact: true }).click();
  await page.setViewportSize({ width: 1240, height: 860 });
  expect(await page.evaluate(() => (window as any).hostRequests)).toContain(
    "openFullMixer",
  );
  // The preserved editor still covers the mixer page until explicitly dismissed.
  await expect(
    page.getByRole("textbox", { name: "Setupname", exact: true }),
  ).toHaveValue("Unsaved office");
  await page.getByRole("button", { name: "Close", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "Audio-Mixer", exact: true }),
  ).toHaveAttribute("aria-current", "page");
  await expect(
    page.getByRole("region", { name: "Demo Mikrofon", exact: true }),
  ).toBeVisible();
});
