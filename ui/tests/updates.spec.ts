import { test, expect, type Page } from "@playwright/test";
import type { State, UpdateInfo } from "../src/lib/bridge";

async function settings(page: Page, available = true) {
  await page.goto("/");
  const state: State = await page.evaluate(async () => {
    const path = "/src/lib/bridge.ts";
    return (await import(path)).call("state");
  });
  const info: UpdateInfo = {
    currentVersion: "0.4.0",
    status: "idle",
    message: "Noch nicht nach Updates gesucht.",
    automatic: true,
    lastChecked: null,
    latestVersion: null,
    releaseUrl: null,
    progress: null,
  };
  await page.addInitScript(
    ({ state, info, available }) => {
      const listeners: ((event: { data: any }) => void)[] = [];
      const emit = (data: any) =>
        listeners.forEach((listener) => listener({ data }));
      (window as any).updateRequests = [];
      (window as any).emitUpdateState = emit;
      Object.defineProperty(window, "chrome", {
        configurable: true,
        value: {
          webview: {
            addEventListener: (
              _: string,
              callback: (event: { data: any }) => void,
            ) => listeners.push(callback),
            postMessage: (request: {
              id: string;
              method: string;
              payload: unknown;
            }) => {
              (window as any).updateRequests.push(request.method);
              let result: unknown = true;
              if (request.method === "state") result = state;
              if (request.method === "updates") result = info;
              if (request.method === "updateAutomatic") {
                info.automatic = !!request.payload;
                result = info;
              }
              if (request.method === "checkUpdate") {
                info = {
                  ...info,
                  status: available ? "available" : "unpublished",
                  latestVersion: available ? "0.4.1" : null,
                  message: available
                    ? "Version 0.4.1 ist verfügbar."
                    : "Noch kein öffentliches Release verfügbar. Das Repository kann noch privat sein.",
                  lastChecked: new Date().toISOString(),
                };
                result = info;
              }
              if (request.method === "installUpdate") {
                info = {
                  ...info,
                  status: "installing",
                  progress: 100,
                  message: "Dateien geprüft. SetupPulse wird neu gestartet …",
                };
                emit({ event: "updates", data: info });
                result = info;
              }
              if (request.method === "homeAssistantSettings")
                result = {
                  options: { enabled: false },
                  connection: { connected: false, state: "Deaktiviert" },
                  passwordSaved: false,
                  availableSetups: [],
                };
              if (request.method === "bluetoothState") result = [];
              queueMicrotask(() =>
                emit({ id: request.id, result: structuredClone(result) }),
              );
            },
          },
        },
      });
    },
    { state, info, available },
  );
  await page.reload();
  await page
    .getByRole("button", { name: "Einstellungen", exact: true })
    .click();
}

test("update checks are optional and installation requires the explicit button", async ({
  page,
}) => {
  await settings(page);
  await expect(
    page.getByText("Installierte Version:", { exact: false }),
  ).toContainText("0.4.0");
  await page
    .getByRole("switch", {
      name: "Automatisch nach Updates suchen",
      exact: true,
    })
    .click();
  await expect(
    page.getByRole("switch", {
      name: "Automatisch nach Updates suchen",
      exact: true,
    }),
  ).not.toBeChecked();
  await page
    .getByRole("button", { name: "Nach Updates suchen", exact: true })
    .click();
  await expect(
    page.getByText("Version 0.4.1 ist verfügbar.", { exact: true }),
  ).toBeVisible();
  expect(
    await page.evaluate(() => (window as any).updateRequests),
  ).not.toContain("installUpdate");
  await page
    .getByRole("button", { name: "Jetzt aktualisieren", exact: true })
    .click();
  await expect(
    page.getByRole("progressbar", { name: "Update-Download" }),
  ).toHaveAttribute("value", "100");
  await expect(
    page.getByRole("button", { name: "Jetzt aktualisieren", exact: true }),
  ).toBeDisabled();
  expect(await page.evaluate(() => (window as any).updateRequests)).toContain(
    "installUpdate",
  );
  await page.screenshot({ path: "../dev/previews/updates-settings.png" });
});

test("a private repository is informational and a running switch disables updating", async ({
  page,
}) => {
  await settings(page, false);
  await page
    .getByRole("button", { name: "Nach Updates suchen", exact: true })
    .click();
  await expect(
    page.getByText("Noch kein öffentliches Release verfügbar.", {
      exact: false,
    }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Jetzt aktualisieren", exact: true }),
  ).toHaveCount(0);
  await page.evaluate(() =>
    (window as any).emitUpdateState({
      event: "busy",
      data: true,
      scope: "switch",
    }),
  );
  await expect(
    page.getByRole("button", { name: "Nach Updates suchen", exact: true }),
  ).toBeDisabled();
});
