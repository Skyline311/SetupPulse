import { test, expect, type Page } from "@playwright/test";

async function read(page: Page, method: string) {
  return page.evaluate(async (method) => {
    const modulePath = "/src/lib/bridge.ts";
    return (await import(modulePath)).call(method);
  }, method);
}

test("separate mixer opens at the top without scrolling through audio favorites", async ({
  page,
}) => {
  await page.goto("/");
  await page.evaluate(async () => {
    const path = "/src/lib/bridge.ts";
    const bridge = await import(path);
    const state = await bridge.call("state");
    state.settings.preferences.theme = "dark";
    state.settings.audioFavorites = Array.from({ length: 25 }, (_, index) => ({
      id: `favorite-${index}`,
      name: `Favorit ${index}`,
      output: { deviceId: "demo-speakers", volume: null, muted: null },
      input: null,
      communicationsOutput: null,
      communicationsInput: null,
      shortcut: null,
    }));
    await bridge.call("save", state.settings);
  });
  await page.getByRole("button", { name: "Audio", exact: true }).click();
  await expect(
    page.getByText("Erkannte Audiogeräte", { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("textbox", { name: "Apps im Mixer suchen" }),
  ).toHaveCount(0);
  await page.evaluate(() =>
    window.scrollTo(0, document.documentElement.scrollHeight),
  );
  expect(await page.evaluate(() => window.scrollY)).toBeGreaterThan(0);
  await page.getByRole("button", { name: "Audio-Mixer", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "Audio-Mixer", exact: true }),
  ).toHaveAttribute("aria-current", "page");
  await expect(
    page.getByRole("textbox", { name: "Apps im Mixer suchen" }),
  ).toBeInViewport();
  expect(await page.evaluate(() => window.scrollY)).toBe(0);
  await expect(
    page.getByText("Erkannte Audiogeräte", { exact: true }),
  ).toHaveCount(0);
  await page.screenshot({ path: "../dev/previews/separate-audio-mixer.png" });
});

test("volume range reaches both endpoints with no text-field padding in dark and light themes", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Audio-Mixer", exact: true }).click();
  const slider = page.getByRole("slider", {
    name: "Demo Lautsprecher Lautstärke",
    exact: true,
  });
  for (const dark of [true, false]) {
    await page.evaluate(
      (dark) => document.documentElement.classList.toggle("dark", dark),
      dark,
    );
    for (const [key, volume] of [
      ["Home", 0],
      ["End", 100],
    ] as const) {
      await slider.press(key);
      await expect
        .poll(
          async () =>
            (await read(page, "mixerState")).devices.find(
              (device: any) => device.id === "demo-speakers",
            ).volume,
        )
        .toBe(volume);
      await expect(slider).toHaveValue(String(volume));
      expect(
        await slider.evaluate((input) => {
          const style = getComputedStyle(input);
          return [
            style.paddingLeft,
            style.paddingRight,
            style.borderLeftWidth,
            style.appearance,
            (input as HTMLElement).style.getPropertyValue("--volume-position"),
          ];
        }),
      ).toEqual(["0px", "0px", "0px", "none", `${volume}%`]);
      await slider.locator("..").screenshot({
        path: `../dev/previews/volume-${dark ? "dark" : "light"}-${volume}.png`,
      });
    }
    // Pointer clicks near each track edge must also reach the actual volume endpoints.
    const bounds = (await slider.boundingBox())!;
    await page.mouse.click(bounds.x + 1, bounds.y + bounds.height / 2);
    await expect(slider).toHaveValue("0");
    await page.mouse.click(
      bounds.x + bounds.width - 1,
      bounds.y + bounds.height / 2,
    );
    await expect(slider).toHaveValue("100");
  }
});

test("logarithmic preference preserves actual volume and permits silence and full volume", async ({
  page,
}) => {
  await page.goto("/");
  await page.evaluate(async () => {
    const modulePath = "/src/lib/bridge.ts";
    const bridge = await import(modulePath);
    const state = await bridge.call("state");
    state.settings.preferences.logarithmicVolume = true;
    await bridge.call("save", state.settings);
    await bridge.call("mixerEndpoint", {
      deviceId: "demo-speakers",
      volume: 10,
    });
  });
  await page.getByRole("button", { name: "Audio-Mixer", exact: true }).click();
  const slider = page.getByRole("slider", {
    name: "Demo Lautsprecher Lautstärke",
    exact: true,
  });
  await expect(slider).toBeVisible();
  const position = Number(await slider.inputValue());
  expect(position).toBeGreaterThan(50);
  expect(position).toBeLessThan(75);
  expect(
    (await read(page, "mixerState")).devices.find(
      (device: any) => device.id === "demo-speakers",
    ).volume,
  ).toBe(10);
  await slider.press("Home");
  await expect
    .poll(
      async () =>
        (await read(page, "mixerState")).devices.find(
          (device: any) => device.id === "demo-speakers",
        ).volume,
    )
    .toBe(0);
  await slider.press("End");
  await expect
    .poll(
      async () =>
        (await read(page, "mixerState")).devices.find(
          (device: any) => device.id === "demo-speakers",
        ).volume,
    )
    .toBe(100);
});

test("device and grouped app volume/mute preserve monitor profiles", async ({
  page,
}) => {
  await page.goto("/?demoMonitors=3");
  const before = await read(page, "state");
  await page.getByRole("button", { name: "Audio-Mixer", exact: true }).click();
  const speakers = page.getByRole("region", {
    name: "Demo Lautsprecher",
    exact: true,
  });
  await speakers
    .getByRole("slider", { name: "Demo Lautsprecher Lautstärke", exact: true })
    .press("Home");
  await expect
    .poll(
      async () =>
        (await read(page, "mixerState")).devices.find(
          (device: any) => device.id === "demo-speakers",
        ).volume,
    )
    .toBe(0);
  await speakers
    .getByRole("slider", {
      name: "Demo Browser auf Demo Lautsprecher Lautstärke",
      exact: true,
    })
    .press("Home");
  await expect
    .poll(async () =>
      (await read(page, "mixerState")).sessions
        .filter((session: any) => session.appId === "demo-browser")
        .every((session: any) => session.volume === 0),
    )
    .toBe(true);
  await speakers
    .getByRole("button", {
      name: "Demo Browser auf Demo Lautsprecher stummschalten",
      exact: true,
    })
    .click();
  await expect
    .poll(async () =>
      (await read(page, "mixerState")).sessions
        .filter((session: any) => session.appId === "demo-browser")
        .every((session: any) => session.muted),
    )
    .toBe(true);
  const after = await read(page, "state");
  expect(after.inventory.displays).toEqual(before.inventory.displays);
  expect(after.settings).toEqual(before.settings);
  await page.screenshot({
    path: "../dev/previews/audio-mixer.png",
    animations: "disabled",
  });
});

test("app routing moves all child processes and can return to Windows default", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Audio-Mixer", exact: true }).click();
  await page
    .getByRole("combobox", {
      name: "Demo Browser Ausgabe auf Demo Lautsprecher",
      exact: true,
    })
    .selectOption("demo-headset");
  await expect
    .poll(async () =>
      (await read(page, "mixerState")).sessions
        .filter((session: any) => session.appId === "demo-browser")
        .every(
          (session: any) =>
            session.deviceId === "demo-headset" &&
            session.routedDeviceId === "demo-headset",
        ),
    )
    .toBe(true);
  await page
    .getByRole("combobox", {
      name: "Demo Browser Ausgabe auf Demo Headset",
      exact: true,
    })
    .selectOption("");
  await expect
    .poll(async () =>
      (await read(page, "mixerState")).sessions
        .filter((session: any) => session.appId === "demo-browser")
        .every(
          (session: any) =>
            session.deviceId === "demo-speakers" &&
            session.routedDeviceId === null,
        ),
    )
    .toBe(true);
  await page
    .getByRole("region", { name: "Demo Headset", exact: true })
    .getByRole("button", { name: "Als Standard", exact: true })
    .click();
  await expect
    .poll(async () =>
      (await read(page, "mixerState")).sessions
        .filter((session: any) => session.appId === "demo-browser")
        .every(
          (session: any) =>
            session.deviceId === "demo-headset" &&
            session.routedDeviceId === null,
        ),
    )
    .toBe(true);
  await expect(
    page.getByRole("combobox", {
      name: "Demo Browser Ausgabe auf Demo Headset",
      exact: true,
    }),
  ).not.toContainText("Demo Mikrofon");
});

test("mixer polling stops on the Audio devices page and while the native window is hidden", async ({
  page,
}) => {
  await page.clock.install();
  await page.goto("/");
  const initial = {
    state: await read(page, "state"),
    mixer: await read(page, "mixerState"),
    bluetooth: await read(page, "bluetoothState"),
  };
  await page.addInitScript((initial) => {
    const listeners: ((event: { data: any }) => void)[] = [];
    (window as any).mixerReads = 0;
    (window as any).bluetoothReads = 0;
    (window as any).emitHost = (data: any) =>
      listeners.forEach((listener) => listener({ data }));
    Object.defineProperty(window, "chrome", {
      configurable: true,
      value: {
        webview: {
          addEventListener: (
            _name: string,
            listener: (event: { data: any }) => void,
          ) => listeners.push(listener),
          postMessage: (request: { id: string; method: string }) => {
            if (request.method === "mixerState") (window as any).mixerReads++;
            if (request.method === "bluetoothState")
              (window as any).bluetoothReads++;
            const result =
              request.method === "state"
                ? initial.state
                : request.method === "mixerState"
                  ? initial.mixer
                  : request.method === "bluetoothState"
                    ? initial.bluetooth
                    : true;
            queueMicrotask(() =>
              (window as any).emitHost({ id: request.id, result }),
            );
          },
        },
      },
    });
  }, initial);
  await page.reload();
  await page.clock.fastForward(3000);
  expect(await page.evaluate(() => (window as any).mixerReads)).toBe(0);
  await page.getByRole("button", { name: "Audio", exact: true }).click();
  await page.clock.fastForward(2250);
  expect(await page.evaluate(() => (window as any).mixerReads)).toBe(0);
  await page.getByRole("button", { name: "Audio-Mixer", exact: true }).click();
  await page.clock.fastForward(2250);
  expect(await page.evaluate(() => (window as any).mixerReads)).toBeGreaterThan(
    1,
  );
  const slider = page.getByRole("slider", {
    name: "Demo Lautsprecher Lautstärke",
    exact: true,
  });
  await page.evaluate(() =>
    (window as any).emitHost({ event: "busy", data: true, scope: "switch" }),
  );
  await expect(slider).toBeDisabled();
  await page.evaluate(() =>
    (window as any).emitHost({ event: "busy", data: false, scope: "switch" }),
  );
  await expect(slider).toBeEnabled();
  await page.evaluate(() =>
    (window as any).emitHost({ event: "busy", data: true, scope: "mixer" }),
  );
  await expect(slider).toBeEnabled();
  await page.evaluate(
    (state) =>
      (window as any).emitHost({
        event: "state",
        data: { ...state, busy: true, busyMixer: true },
      }),
    initial.state,
  );
  await expect(slider).toBeEnabled();
  await page.evaluate((state) => {
    (window as any).emitHost({ event: "busy", data: false, scope: "mixer" });
    (window as any).emitHost({ event: "state", data: state });
  }, initial.state);
  await page.evaluate(() =>
    (window as any).emitHost({ event: "windowVisibility", data: false }),
  );
  const hiddenReads = await page.evaluate(() => (window as any).mixerReads);
  await page.clock.fastForward(5000);
  expect(await page.evaluate(() => (window as any).mixerReads)).toBe(
    hiddenReads,
  );
  await page.evaluate(() =>
    (window as any).emitHost({ event: "windowVisibility", data: true }),
  );
  await page.clock.fastForward(1500);
  expect(await page.evaluate(() => (window as any).mixerReads)).toBeGreaterThan(
    hiddenReads,
  );
  await page.evaluate(() =>
    (window as any).emitHost({ event: "navigate", data: "quickMixer" }),
  );
  await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page.getByText("Audio-Mixer", { exact: true })).toHaveCount(1);
  const quickReads = await page.evaluate(() => (window as any).mixerReads);
  await page.clock.runFor(1500);
  expect(await page.evaluate(() => (window as any).mixerReads)).toBe(
    quickReads + 2,
  );
  await page.evaluate(() =>
    (window as any).emitHost({ event: "navigate", data: "mainWindow" }),
  );
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(
    page.getByRole("button", { name: "Audio", exact: true }),
  ).toBeVisible();
  await page.evaluate(() =>
    (window as any).emitHost({ event: "navigate", data: "settings" }),
  );
  await expect(
    page.getByRole("heading", { name: "Alles an seinem Platz.", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText("Windows-Fehler 31", { exact: true }),
  ).toBeVisible();
  const bluetoothReads = await page.evaluate(
    () => (window as any).bluetoothReads,
  );
  await page.clock.fastForward(6000);
  expect(await page.evaluate(() => (window as any).bluetoothReads)).toBe(
    bluetoothReads,
  );
  await page
    .getByRole("button", {
      name: "Bluetooth-Adapter aktualisieren",
      exact: true,
    })
    .click();
  expect(await page.evaluate(() => (window as any).bluetoothReads)).toBe(
    bluetoothReads + 1,
  );
  await page.getByRole("button", { name: "Workspace", exact: true }).click();
  const stoppedReads = await page.evaluate(() => (window as any).mixerReads);
  await page.clock.fastForward(5000);
  expect(await page.evaluate(() => (window as any).mixerReads)).toBe(
    stoppedReads,
  );
});

test("late manual reads cannot undo a write and sliders reconcile native readback", async ({
  page,
}) => {
  await page.goto("/");
  const initial = {
    state: await read(page, "state"),
    mixer: await read(page, "mixerState"),
  };
  await page.addInitScript((initial) => {
    const listeners: ((event: { data: any }) => void)[] = [];
    let delayed: { id: string; result: any } | null = null;
    const emit = (data: any) =>
      listeners.forEach((listener) => listener({ data }));
    (window as any).holdMixerRead = false;
    (window as any).releaseMixerRead = () => {
      if (delayed) emit(delayed);
      delayed = null;
    };
    Object.defineProperty(window, "chrome", {
      configurable: true,
      value: {
        webview: {
          addEventListener: (
            _name: string,
            listener: (event: { data: any }) => void,
          ) => listeners.push(listener),
          postMessage: (request: {
            id: string;
            method: string;
            payload: any;
          }) => {
            if (
              request.method === "mixerEndpoint" &&
              request.payload.volume !== undefined
            )
              initial.mixer.devices.find(
                (device: any) => device.id === request.payload.deviceId,
              ).volume = 74;
            const result = structuredClone(
              request.method === "state"
                ? initial.state
                : request.method === "mixerState"
                  ? initial.mixer
                  : true,
            );
            if (
              request.method === "mixerState" &&
              (window as any).holdMixerRead
            ) {
              (window as any).holdMixerRead = false;
              delayed = { id: request.id, result };
            } else queueMicrotask(() => emit({ id: request.id, result }));
          },
        },
      },
    });
  }, initial);
  await page.reload();
  await page.getByRole("button", { name: "Audio-Mixer", exact: true }).click();
  const slider = page.getByRole("slider", {
    name: "Demo Lautsprecher Lautstärke",
    exact: true,
  });
  await expect(slider).toBeVisible();
  await page.evaluate(() => ((window as any).holdMixerRead = true));
  await page
    .getByRole("button", { name: "Mixer aktualisieren", exact: true })
    .click();
  await slider.press("End");
  await expect(slider).toHaveValue("74");
  await page.evaluate(() => (window as any).releaseMixerRead());
  await expect(slider).toHaveValue("74");
});
