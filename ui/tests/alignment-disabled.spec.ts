import { test, expect } from "@playwright/test";
import { readFileSync } from "node:fs";
const features = JSON.parse(
  readFileSync(new URL("../../features.json", import.meta.url), "utf8"),
);

test.skip(
  features.liveMonitorAlignment,
  "Only applies while live alignment is withdrawn.",
);

test("live controls are absent while saved Y positions remain editable", async ({
  page,
}) => {
  await page.goto("/?demoMonitors=3");
  await page
    .getByRole("button", { name: "Setup erstellen", exact: true })
    .click();
  const editor = page.getByRole("dialog", { name: "Setup bearbeiten" });
  await expect(
    editor.getByRole("button", { name: "Monitorhöhe live abgleichen" }),
  ).toHaveCount(0);
  const position = editor.getByRole("spinbutton", {
    name: "Demo Rechts y",
    exact: true,
  });
  await position.fill("-60");
  await expect(position).toHaveValue("-60");
  const y = await page.evaluate(async () => {
    const modulePath = "/src/lib/bridge.ts";
    const state = await (await import(modulePath)).call("state");
    return state.inventory.displays.find((d: any) => d.name === "Demo Rechts")
      .current.y;
  });
  expect(y).not.toBe(-60);
});

test("withdrawn alignment commands cannot start or modify a transaction", async ({
  page,
}) => {
  await page.goto("/");
  const result = await page.evaluate(async () => {
    const modulePath = "/src/lib/bridge.ts";
    const bridge = await import(modulePath);
    const before = await bridge.call("state");
    const errors: string[] = [];
    for (const method of ["beginAlignment", "updateAlignment"]) {
      try {
        await bridge.call(method, { id: "disabled", monitors: [] });
      } catch (error) {
        errors.push((error as Error).message);
      }
    }
    return { before, after: await bridge.call("state"), errors };
  });
  expect(result.errors).toHaveLength(2);
  expect(
    result.errors.every((message) => message.includes("deaktiviert")),
  ).toBe(true);
  expect(result.after).toEqual(result.before);
});
