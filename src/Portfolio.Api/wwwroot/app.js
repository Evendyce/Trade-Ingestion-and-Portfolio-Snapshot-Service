"use strict";
const $ = (id) => document.getElementById(id);
const state = {
  tab: "positions",
  snapshot: null,
  trades: [],
  events: [],
  instruments: [],
  sequence: 0,
};
const money = (value) =>
  value == null
    ? "Unavailable"
    : new Intl.NumberFormat("en-US", {
        style: "currency",
        currency: "USD",
      }).format(value);
const number = (value) =>
  new Intl.NumberFormat("en-US", { maximumFractionDigits: 6 }).format(value);
const escapeHtml = (value) =>
  String(value ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const utcTime = (value) =>
  new Date(value).toLocaleString("en-GB", {
    timeZone: "UTC",
    dateStyle: "medium",
    timeStyle: "medium",
  }) + " UTC";
async function api(path, options) {
  const response = await fetch(path, options);
  const body = await response.json();
  if (!response.ok) {
    const message = body.errors
      ? Object.values(body.errors).flat().join(" ")
      : body.detail || body.title || "Request failed.";
    throw new Error(
      message + (body.traceId ? ` (Trace: ${body.traceId})` : ""),
    );
  }
  return body;
}
function notice(message, error = false) {
  $("notice").hidden = false;
  $("notice").textContent = message;
  $("notice").className = "notice" + (error ? " error" : "");
}
function setBusy(button, busy, label) {
  button.disabled = busy;
  if (label) button.textContent = label;
}
function snapshotPath() {
  const query = new URLSearchParams({ date: $("valuation-date").value });
  if ($("cutoff").value) query.set("asOf", $("cutoff").value + "Z");
  return `/api/accounts/${encodeURIComponent($("account").value)}/snapshot?${query}`;
}
async function load() {
  const sequence = ++state.sequence;
  const account = encodeURIComponent($("account").value);
  $("data-content").setAttribute("aria-busy", "true");
  const viewButton = $("snapshot-form").querySelector("button");
  setBusy(viewButton, true);
  try {
    const [snapshot, trades, events] = await Promise.all([
      api(snapshotPath()),
      api(`/api/accounts/${account}/trades`),
      api(`/api/accounts/${account}/events`),
    ]);
    if (sequence !== state.sequence) return;
    Object.assign(state, { snapshot, trades, events });
    const view = new URLSearchParams({
      account: snapshot.accountId,
      date: snapshot.date,
    });
    if (snapshot.asOf) view.set("asOf", snapshot.asOf);
    history.replaceState(null, "", "/?" + view);
    $("total-value").textContent = money(snapshot.totalMarketValueUsd);
    $("total-pnl").textContent = money(snapshot.unrealizedPnlUsd);
    $("total-pnl").className =
      "metric-value " +
      (snapshot.unrealizedPnlUsd == null
        ? ""
        : snapshot.unrealizedPnlUsd >= 0
          ? "positive"
          : "negative");
    $("total-cost").textContent = money(snapshot.totalCostBasisUsd);
    $("position-count").textContent = snapshot.positions.length;
    $("holdings-count").textContent = snapshot.positions.length;
    $("return-percent").textContent =
      snapshot.totalCostBasisUsd > 0 && snapshot.unrealizedPnlUsd != null
        ? `${snapshot.unrealizedPnlUsd >= 0 ? "+" : ""}${((snapshot.unrealizedPnlUsd / snapshot.totalCostBasisUsd) * 100).toFixed(2)}% on remaining cost`
        : "Market value less remaining cost";
    $("snapshot-caption").textContent =
      `${snapshot.accountId}  /  ${new Date(snapshot.date + "T00:00:00Z").toLocaleDateString("en-GB", { dateStyle: "long", timeZone: "UTC" })}`;
    $("version-mode").textContent = snapshot.asOf
      ? "◷ Historical version cutoff"
      : "● Latest corrected view";
    $("updated-at").textContent =
      "Updated " + new Date().toLocaleTimeString("en-GB");
    if (!snapshot.isComplete) notice(snapshot.warnings.join(" "), true);
    render();
  } catch (error) {
    if (sequence === state.sequence) {
      state.snapshot = null;
      state.trades = [];
      state.events = [];
      ["total-value", "total-pnl", "total-cost", "position-count"].forEach(
        (id) => ($(id).textContent = "—"),
      );
      $("snapshot-caption").textContent = "Snapshot could not be loaded";
      $("data-content").innerHTML =
        '<div class="empty-state"><h3>Unable to load this view</h3><p>Check the account, dates, and service connection, then try again.</p></div>';
      notice(error.message, true);
    }
  } finally {
    if (sequence === state.sequence) {
      $("data-content").setAttribute("aria-busy", "false");
      setBusy(viewButton, false);
    }
  }
}
function table(headers, rows) {
  return `<div class="table-wrap"><table><thead><tr>${headers.map(([text, numeric]) => `<th scope="col"${numeric ? ' class="numeric"' : ""}>${text}</th>`).join("")}</tr></thead><tbody>${rows.join("")}</tbody></table></div>`;
}
function empty(title, description) {
  return `<div class="empty-state"><div class="empty-symbol" aria-hidden="true">◎</div><h3>${title}</h3><p>${description}</p></div>`;
}
function render() {
  const snapshot = state.snapshot;
  if (!snapshot) return;
  $("data-content").setAttribute("aria-labelledby", "tab-" + state.tab);
  $("panel-meta").textContent =
    state.tab === "positions"
      ? "VALUED IN USD"
      : state.tab === "trades"
        ? "LATEST ACCEPTED VERSIONS"
        : "LATEST 100 RECEIPTS";
  $("panel-description").textContent =
    state.tab === "positions"
      ? "Prices use the latest available date on or before valuation."
      : "Trade ledger and activity show all dates, independently of the snapshot cutoff.";
  if (state.tab === "positions") {
    $("data-content").innerHTML = snapshot.positions.length
      ? table(
          [
            ["Instrument"],
            ["Quantity", true],
            ["Average cost", true],
            ["Market price", true],
            ["Market value", true],
            ["Unrealized P/L", true],
            ["Weight", true],
          ],
          snapshot.positions.map((p) => {
            const weight =
              snapshot.totalMarketValueUsd > 0 && p.marketValueUsd != null
                ? (p.marketValueUsd / snapshot.totalMarketValueUsd) * 100
                : null;
            const instrument = state.instruments.find(
              (i) => i.isin === p.instrumentId,
            );
            return `<tr><td><div class="instrument-cell"><span class="ticker-icon" aria-hidden="true">${escapeHtml(p.symbol.slice(0, 1))}</span><div><strong>${escapeHtml(p.symbol)}</strong><small>${escapeHtml(instrument?.name || p.instrumentId)}</small></div></div></td><td class="numeric">${number(p.quantity)}</td><td class="numeric">${money(p.unitCostUsd)}</td><td class="numeric">${money(p.marketPriceUsd)}<span class="price-date">${escapeHtml(p.priceDate || "No price")}</span></td><td class="numeric">${money(p.marketValueUsd)}</td><td class="numeric ${p.unrealizedPnlUsd == null ? "" : p.unrealizedPnlUsd >= 0 ? "positive" : "negative"}">${money(p.unrealizedPnlUsd)}</td><td class="numeric"><div class="weight"><div class="weight-track" aria-hidden="true"><span style="width:${weight == null ? 0 : Math.max(0, Math.min(100, weight))}%"></span></div>${weight == null ? "—" : weight.toFixed(1) + "%"}</div></td></tr>`;
          }),
        )
      : empty(
          "A fresh perspective starts here.",
          "No open positions for this account and date. Submit a trade, or load an example portfolio below to explore the full flow.",
        );
  } else if (state.tab === "trades") {
    $("data-content").innerHTML = state.trades.length
      ? '<div class="table-note">Latest accepted trade versions across <strong>all dates</strong>. Open history to inspect earlier versions.</div>' +
        table(
          [
            ["External reference"],
            ["Instrument"],
            ["Side"],
            ["Quantity", true],
            ["Price", true],
            ["Trade date"],
            ["History"],
          ],
          state.trades
            .sort(
              (a, b) =>
                b.tradeDate.localeCompare(a.tradeDate) ||
                a.externalRef.localeCompare(b.externalRef),
            )
            .map(
              (t) =>
                `<tr><td>${escapeHtml(t.externalRef)}</td><td>${escapeHtml(state.instruments.find((i) => i.isin === t.instrumentId)?.symbol || t.instrumentId)}</td><td><span class="badge">${escapeHtml(t.side)}</span></td><td class="numeric">${number(t.quantity)}</td><td class="numeric">${money(t.price)}</td><td>${escapeHtml(t.tradeDate)}</td><td><button class="text-button history-button" data-ref="${escapeHtml(t.externalRef)}">View versions ↗</button></td></tr>`,
            ),
        )
      : empty("No trades yet.", "Accepted trade versions will appear here.");
  } else {
    $("data-content").innerHTML = state.events.length
      ? table(
          [
            ["Received (UTC)"],
            ["External reference"],
            ["Outcome"],
            ["Upstream version (UTC)"],
            ["Revision", true],
          ],
          state.events.map(
            (e) =>
              `<tr><td>${escapeHtml(utcTime(e.receivedAt))}</td><td>${escapeHtml(e.externalRef)}</td><td><span class="badge ${escapeHtml(e.outcome)}">${escapeHtml(e.outcome.replaceAll("_", " "))}</span></td><td>${escapeHtml(utcTime(e.asOf))}</td><td class="numeric">#${e.revisionId}</td></tr>`,
          ),
        )
      : empty(
          "Nothing lost in the resend.",
          "Accepted submissions, duplicates, stale events and version conflicts will appear here. Rejected requests are recorded in service logs.",
        );
  }
}
async function showHistory(reference) {
  $("revision-title").textContent = reference;
  $("revision-content").textContent = "Loading versions…";
  $("revision-dialog").showModal();
  try {
    const revisions = await api(
      `/api/trades/${encodeURIComponent(reference)}/revisions`,
    );
    $("revision-content").innerHTML = revisions
      .map(
        (r, i) =>
          `<article class="revision-item"><strong>${i === 0 ? "Latest accepted version" : "Earlier version"} <span class="badge">#${r.id}</span></strong><p>${escapeHtml(r.side)} ${number(r.quantity)} units at ${money(r.price)}<br>Trade date: ${escapeHtml(r.tradeDate)}<br>Version: ${escapeHtml(utcTime(r.asOf))}</p><small>Received ${escapeHtml(utcTime(r.receivedAt))}</small></article>`,
      )
      .join("");
  } catch (error) {
    $("revision-content").textContent = error.message;
  }
}
document.querySelectorAll("[data-tab]").forEach((button) => {
  button.addEventListener("click", () => {
    state.tab = button.dataset.tab;
    document.querySelectorAll("[data-tab]").forEach((b) => {
      const selected = b === button;
      b.classList.toggle("selected", selected);
      b.setAttribute("aria-selected", String(selected));
      b.tabIndex = selected ? 0 : -1;
    });
    render();
  });
  button.addEventListener("keydown", (event) => {
    const tabs = [...document.querySelectorAll("[data-tab]")];
    const index = tabs.indexOf(button);
    const target =
      event.key === "ArrowRight"
        ? tabs[(index + 1) % tabs.length]
        : event.key === "ArrowLeft"
          ? tabs[(index + tabs.length - 1) % tabs.length]
          : event.key === "Home"
            ? tabs[0]
            : event.key === "End"
              ? tabs.at(-1)
              : null;
    if (target) {
      event.preventDefault();
      target.focus();
      target.click();
    }
  });
});
$("data-content").addEventListener("click", (event) => {
  const button = event.target.closest(".history-button");
  if (button) showHistory(button.dataset.ref);
});
$("snapshot-form").addEventListener("submit", (event) => {
  event.preventDefault();
  $("notice").hidden = true;
  load();
});
$("new-trade").addEventListener("click", () => {
  $("trade-ref").value = "TRX-" + crypto.randomUUID().slice(0, 8).toUpperCase();
  $("trade-account").value = $("account").value;
  $("trade-date").value = $("valuation-date").value;
  $("trade-asof").value = new Date().toISOString().slice(0, 19);
  $("trade-error").hidden = true;
  $("trade-dialog").showModal();
});
$("close-trade").addEventListener("click", () => $("trade-dialog").close());
$("close-revisions").addEventListener("click", () =>
  $("revision-dialog").close(),
);
$("trade-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const values = Object.fromEntries(new FormData(event.target));
  values.quantity = Number(values.quantity);
  values.price = Number(values.price);
  values.asOf += "Z";
  setBusy($("submit-trade"), true, "Submitting…");
  $("trade-error").hidden = true;
  try {
    const result = await api("/api/trades", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(values),
    });
    $("trade-dialog").close();
    $("account").value = values.accountId;
    notice(
      `${values.externalRef}: ${result.outcome.replaceAll("_", " ")}. Accepted revision #${result.revisionId}.` +
        ($("cutoff").value
          ? " The selected historical cutoff may exclude this submission; clear it to see the latest view."
          : ""),
    );
    await load();
  } catch (error) {
    $("trade-error").textContent = error.message;
    $("trade-error").hidden = false;
  } finally {
    setBusy($("submit-trade"), false, "Submit trade →");
  }
});
$("run-demo").addEventListener("click", async () => {
  setBusy($("run-demo"), true, "Loading example…");
  try {
    const suffix = crypto.randomUUID().slice(0, 8).toUpperCase();
    const trade = {
      externalRef: `DEMO-AAPL-${suffix}`,
      accountId: `DEMO-${suffix}`,
      instrumentId: "US0378331005",
      side: "BUY",
      quantity: 120,
      price: 185.4,
      tradeDate: "2025-03-01",
      asOf: "2025-03-01T10:00:00Z",
    };
    const send = (body) =>
      api("/api/trades", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    await send(trade);
    await send(trade);
    await send({ ...trade, quantity: 100, asOf: "2025-03-01T11:00:00Z" });
    await send({
      ...trade,
      externalRef: `DEMO-MSFT-${suffix}`,
      instrumentId: "US5949181045",
      quantity: 12,
      price: 385,
      asOf: "2025-03-01T10:30:00Z",
    });
    $("account").value = trade.accountId;
    $("valuation-date").value = "2025-03-01";
    $("cutoff").value = "";
    notice(
      "Example loaded: AAPL corrected from 120 to 100 units; its resend was ignored. Together with 12 MSFT units, market value is $23,400 and unrealized P/L is $240. Explore ingestion activity or set the UTC cutoff to 2025-03-01 10:15:00 to see the original AAPL holding.",
    );
    await load();
  } catch (error) {
    notice(error.message, true);
  } finally {
    setBusy($("run-demo"), false, "Load example portfolio ↗");
  }
});
async function initialize() {
  const view = new URLSearchParams(location.search);
  if (view.has("account")) $("account").value = view.get("account");
  if (view.has("date")) $("valuation-date").value = view.get("date");
  if (view.has("asOf"))
    $("cutoff").value = view.get("asOf").replace(/(?:Z|\+00:00)$/, "");
  try {
    state.instruments = await api("/api/instruments");
    $("trade-instrument").innerHTML = state.instruments
      .map(
        (i) =>
          `<option value="${escapeHtml(i.isin)}">${escapeHtml(i.symbol)} · ${escapeHtml(i.name)}</option>`,
      )
      .join("");
    const status = await api("/api/status");
    $("service-status").textContent = status.ingestionEnabled
      ? `● SERVICE CONNECTED · ${status.correctionsEnabled ? "CORRECTIONS ENABLED" : "CORRECTIONS PAUSED"}`
      : "○ INGESTION PAUSED";
    $("new-trade").disabled = !status.ingestionEnabled;
    $("run-demo").disabled = !status.ingestionEnabled;
  } catch (error) {
    $("service-status").textContent = "○ SERVICE UNAVAILABLE";
    notice(error.message, true);
  }
  await load();
}
initialize();
