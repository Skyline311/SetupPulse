import { useEffect, useState, type Dispatch, type SetStateAction } from "react";
import { House, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Switch } from "@/components/ui/switch";
import {
  call,
  subscribe,
  type HomeAssistantInfo,
  type HomeAssistantOptions,
} from "@/lib/bridge";

export type HomeAssistantDraft = {
  options: HomeAssistantOptions;
  password: string;
  clearPassword: boolean;
};

export function HomeAssistantSettings({
  disabled,
  draft,
  setDraft,
}: {
  disabled: boolean;
  draft: HomeAssistantDraft | null;
  setDraft: Dispatch<SetStateAction<HomeAssistantDraft | null>>;
}) {
  const [info, setInfo] = useState<HomeAssistantInfo | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const refresh = () =>
    call<HomeAssistantInfo>("homeAssistantSettings")
      .then(setInfo)
      .catch((e) => setError(e.message));
  useEffect(() => {
    let active = true;
    void call<HomeAssistantInfo>("homeAssistantSettings")
      .then((value) => {
        if (active) setInfo(value);
      })
      .catch((e) => {
        if (active) setError(e.message);
      });
    const unsubscribe = subscribe((message) => {
      if (message.event === "homeAssistant")
        setInfo(message.data as HomeAssistantInfo);
    });
    return () => {
      active = false;
      unsubscribe();
    };
  }, []);
  const options = draft?.options ?? info?.options;
  const password = draft?.password ?? "",
    clearPassword = draft?.clearPassword ?? false;
  const edit = (value: Partial<HomeAssistantDraft>) => {
    if (options)
      setDraft((current) => ({
        options,
        password,
        clearPassword,
        ...current,
        ...value,
      }));
    setSaved(false);
  };
  const patch = (value: Partial<HomeAssistantOptions>) => {
    if (options) edit({ options: { ...options, ...value } });
  };
  const blocked = disabled || saving;
  const save = async () => {
    if (!options) return;
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      const value = await call<HomeAssistantInfo>("saveHomeAssistant", {
        options,
        password: password || null,
        clearPassword,
      });
      setInfo(value);
      setDraft(null);
      setSaved(true);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  };
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex gap-2 items-center">
          <House size={18} />
          Home Assistant
        </CardTitle>
      </CardHeader>
      <CardContent className="stack">
        <p className="text-sm">
          Setups über dein Dashboard oder einen Zigbee-Taster wechseln. Die
          Verbindung läuft über deinen MQTT-Broker.
        </p>
        {options && (
          <>
            <div className="row">
              <label htmlFor="ha-enabled">MQTT-Anbindung aktivieren</label>
              <Switch
                id="ha-enabled"
                aria-label="Home Assistant aktivieren"
                checked={options.enabled}
                disabled={blocked}
                onCheckedChange={(enabled) => patch({ enabled })}
              />
            </div>
            <p role="status" className="helper">
              Verbindung: {info?.connection.state ?? "Wird geladen …"}
              {draft ? " · Ungespeicherte Änderungen" : ""}
            </p>
            <fieldset disabled={blocked} className="stack min-w-0">
              <div className="field-grid">
                <label className="stack gap-2 text-sm">
                  Broker-Adresse
                  <input
                    aria-label="MQTT-Broker"
                    placeholder="192.168.1.10"
                    value={options.host}
                    maxLength={253}
                    onChange={(e) => patch({ host: e.target.value })}
                    autoComplete="off"
                  />
                </label>
                <label className="stack gap-2 text-sm">
                  Port
                  <input
                    aria-label="MQTT-Port"
                    type="number"
                    min={1}
                    max={65535}
                    value={options.port}
                    onChange={(e) => patch({ port: Number(e.target.value) })}
                  />
                </label>
              </div>
              <div className="field-grid">
                <label className="stack gap-2 text-sm">
                  Benutzername
                  <input
                    aria-label="MQTT-Benutzername"
                    value={options.username}
                    maxLength={256}
                    onChange={(e) => patch({ username: e.target.value })}
                    autoComplete="off"
                  />
                </label>
                <label className="stack gap-2 text-sm">
                  Passwort
                  <input
                    aria-label="MQTT-Passwort"
                    type="password"
                    value={password}
                    maxLength={4096}
                    placeholder={
                      info?.passwordSaved && !clearPassword
                        ? "Gespeichertes Passwort beibehalten"
                        : "Optional"
                    }
                    autoComplete="new-password"
                    onChange={(e) => {
                      edit({ password: e.target.value, clearPassword: false });
                    }}
                  />
                </label>
              </div>
              {info?.passwordSaved && (
                <Button
                  variant="ghost"
                  className="self-start"
                  onClick={() => {
                    edit({ clearPassword: !clearPassword, password: "" });
                  }}
                >
                  {clearPassword
                    ? "Passwort doch beibehalten"
                    : "Gespeichertes Passwort entfernen"}
                </Button>
              )}
              <div className="row">
                <label htmlFor="ha-tls">TLS-Verschlüsselung</label>
                <Switch
                  id="ha-tls"
                  aria-label="MQTT TLS"
                  checked={options.useTls}
                  onCheckedChange={(useTls) =>
                    patch({
                      useTls,
                      port:
                        options.port === 1883 || options.port === 8883
                          ? useTls
                            ? 8883
                            : 1883
                          : options.port,
                    })
                  }
                />
              </div>
              <label className="stack gap-2 text-sm">
                Gerätename in Home Assistant
                <input
                  aria-label="Home-Assistant-Gerätename"
                  value={options.deviceName}
                  maxLength={80}
                  onChange={(e) => patch({ deviceName: e.target.value })}
                />
              </label>
              <details>
                <summary className="text-sm cursor-pointer">
                  Erweiterte Einstellungen
                </summary>
                <label className="stack gap-2 text-sm mt-3">
                  Discovery-Präfix
                  <input
                    aria-label="MQTT-Discovery-Präfix"
                    value={options.discoveryPrefix}
                    maxLength={128}
                    onChange={(e) => patch({ discoveryPrefix: e.target.value })}
                  />
                </label>
              </details>
            </fieldset>
            <p className="helper">
              Benutze denselben Broker wie Zigbee2MQTT. Zugangsdaten bleiben auf
              diesem PC; das Passwort wird für deinen Windows-Benutzer
              verschlüsselt gespeichert.
            </p>
            <div className="row gap-2 flex-wrap">
              <Button disabled={blocked} onClick={() => void save()}>
                {saving ? "Speichere …" : "MQTT-Verbindung speichern"}
              </Button>
              <Button
                variant="ghost"
                size="icon"
                aria-label="MQTT-Status aktualisieren"
                disabled={saving}
                onClick={() => void refresh()}
              >
                <RefreshCw size={16} />
              </Button>
            </div>
            {saved && (
              <p role="status" className="helper">
                Gespeichert.{" "}
                {info?.options.enabled
                  ? "SetupPulse verbindet sich und meldet sich automatisch in Home Assistant an."
                  : "Die MQTT-Anbindung ist deaktiviert."}
              </p>
            )}
            <p className="helper">
              {info?.availableSetups.length ?? 0} Setups fernsteuerbar. Neue
              oder geänderte Monitoranordnungen zuerst am PC aktivieren und
              bestätigen.
            </p>
            <p className="helper">
              Audioausgabe, Mikrofon und verfügbare Audiofavoriten lassen sich in
              Home Assistant separat auswählen, ohne die Monitore zu wechseln.
            </p>
          </>
        )}
        {(error || info?.connection?.error) && (
          <div role="alert" className="notice danger select-text">
            {error || info?.connection?.error}
          </div>
        )}
        {!options && !error && (
          <p className="helper">Einstellungen werden geladen …</p>
        )}
      </CardContent>
    </Card>
  );
}
