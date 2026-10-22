import { useEffect, useState } from "react";
import { Download, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Switch } from "@/components/ui/switch";
import { call, subscribe, type UpdateInfo } from "@/lib/bridge";

export function UpdateSettings({ disabled }: { disabled: boolean }) {
  const [info, setInfo] = useState<UpdateInfo | null>(null);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let active = true;
    void call<UpdateInfo>("updates")
      .then((value) => {
        if (active) setInfo(value);
      })
      .catch((e) => {
        if (active) setError(e.message);
      });
    const unsubscribe = subscribe((message) => {
      if (message.event === "updates") setInfo(message.data as UpdateInfo);
    });
    return () => {
      active = false;
      unsubscribe();
    };
  }, []);
  const run = async (method: string, payload: unknown = null) => {
    setWorking(true);
    setError(null);
    try {
      setInfo(await call<UpdateInfo>(method, payload));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setWorking(false);
    }
  };
  const busy =
    working ||
    ["checking", "downloading", "installing"].includes(info?.status ?? "");
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex gap-2 items-center">
          <Download size={18} /> Updates
        </CardTitle>
      </CardHeader>
      <CardContent className="stack">
        {info && (
          <>
            <p>
              Installierte Version: <strong>{info.currentVersion}</strong>
            </p>
            <div className="row">
              <label htmlFor="automatic-updates">
                Beim Start nach Updates suchen
              </label>
              <Switch
                id="automatic-updates"
                aria-label="Automatisch nach Updates suchen"
                checked={info.automatic}
                disabled={disabled || busy}
                onCheckedChange={(value) => void run("updateAutomatic", value)}
              />
            </div>
            <p className="helper">
              Die automatische Prüfung läuft höchstens einmal täglich.
              Installiert wird erst nach deinem Klick.
            </p>
            <div
              role={info.status === "error" ? "alert" : "status"}
              className={
                info.status === "error"
                  ? "notice danger select-text"
                  : "notice select-text"
              }
            >
              {info.message}
            </div>
            {info.progress !== null && (
              <div className="stack gap-1">
                <progress
                  aria-label="Update-Download"
                  max={100}
                  value={info.progress}
                className="update-progress"
                />
                <span className="helper">{info.progress}%</span>
              </div>
            )}
            {info.lastChecked && (
              <p className="helper">
                Zuletzt geprüft:{" "}
                {new Date(info.lastChecked).toLocaleString("de-DE")}
              </p>
            )}
            <div className="flex gap-2 flex-wrap">
              <Button
                variant="outline"
                disabled={disabled || busy}
                onClick={() => void run("checkUpdate")}
              >
                <RefreshCw
                  size={16}
                  className={info.status === "checking" ? "animate-spin" : ""}
                />{" "}
                Nach Updates suchen
              </Button>
              {info.latestVersion && (
                <Button
                  disabled={disabled || busy}
                  onClick={() => void run("installUpdate")}
                >
                  <Download size={16} /> Jetzt aktualisieren
                </Button>
              )}
            </div>
            {info.latestVersion && (
              <p className="helper">
                Version {info.latestVersion} wird von GitHub geladen und
                geprüft. SetupPulse startet anschließend neu. Deine Profile und
                Einstellungen bleiben erhalten.
              </p>
            )}
          </>
        )}
        {!info && !error && (
          <p className="helper">Update-Informationen werden geladen …</p>
        )}
        {error && error !== info?.message && (
          <div role="alert" className="notice danger select-text">
            {error}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
