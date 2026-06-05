const http = require("node:http");
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");
const { spawn } = require("node:child_process");

const HOST = process.env.HOST || "127.0.0.1";
const PORT = Number.parseInt(process.env.PORT || "3000", 10);
const DATA_DIR = process.env.DATA_DIR || path.join(__dirname, "data");
const PUBLIC_DIR = path.join(__dirname, "public");
const DERIVED_DIR = path.join(DATA_DIR, "derived");
const SUMMARY_FILE = path.join(DERIVED_DIR, "summary.json");
const DERIVED_INDEX_FILE = path.join(DERIVED_DIR, "index.html");
const REBUILD_LOCK_FILE = path.join(DERIVED_DIR, ".summary-rebuild.lock");
const LABELS_FILE = path.join(__dirname, "labels.json");
const LATEST_VERSION_FILE = path.join(PUBLIC_DIR, "latest-version.json");
const RESULTS_FILE = path.join(DATA_DIR, "run_results.jsonl");
const MAX_BODY_BYTES = 256 * 1024;
const MIN_RUN_TIME_FOR_DEFAULT_STATS = 60;
const parsedDerivedRefreshDelayMs = Number.parseInt(process.env.DERIVED_REFRESH_DELAY_MS || "300000", 10);
const DERIVED_REFRESH_DELAY_MS = Number.isFinite(parsedDerivedRefreshDelayMs) ? parsedDerivedRefreshDelayMs : 60000;
const parsedSummaryRefreshIntervalMs = Number.parseInt(process.env.SUMMARY_REFRESH_INTERVAL_MS || "300000", 10);
const SUMMARY_REFRESH_INTERVAL_MS = Number.isFinite(parsedSummaryRefreshIntervalMs) ? parsedSummaryRefreshIntervalMs : 300000;
const DERIVED_FILE_NAMES = ["summary.json", "runs.csv", "player_runes.csv", "rune_choices.csv", "monster_hexes.csv"];

fs.mkdirSync(DATA_DIR, { recursive: true });
fs.mkdirSync(DERIVED_DIR, { recursive: true });

const LABELS = loadLabels();
const isRebuildProcess = process.argv.includes("--rebuild-derived");
const knownRunIds = isRebuildProcess ? new Set() : loadKnownRunIds();
const summaryCache = new Map();
const derivedState = {
  dirty: false,
  rebuilding: false,
  timer: null,
  interval: null,
  lastBuiltAtMs: 0,
  lastError: null
};

function loadLabels() {
  try {
    if (fs.existsSync(LABELS_FILE)) {
      return JSON.parse(fs.readFileSync(LABELS_FILE, "utf8"));
    }
  } catch (error) {
    console.warn(`failed to load labels: ${error.message}`);
  }
  return {};
}

function getLabel(category, id) {
  if (!id) {
    return "";
  }
  return LABELS?.[category]?.[id] || id;
}

function displayLabel(row) {
  if (!row || !row.name || row.name === row.id) {
    return row?.id || "";
  }
  return `${row.name} (${row.id})`;
}

function loadKnownRunIds() {
  const ids = new Set();
  forEachJsonlLine(RESULTS_FILE, (line) => {
    try {
      const record = JSON.parse(line);
      const runId = record?.payload?.run?.runId;
      if (typeof runId === "string" && runId.length > 0) {
        ids.add(runId);
      }
    } catch {
      // Bad historical lines are ignored here; readRecordSet records them for analytics.
    }
  });
  return ids;
}

function sendJson(res, status, value) {
  const body = JSON.stringify(value);
  res.writeHead(status, {
    "content-type": "application/json; charset=utf-8",
    "cache-control": "no-store",
    "access-control-allow-origin": "*",
    "content-length": Buffer.byteLength(body)
  });
  res.end(body);
}

function sendText(res, status, value) {
  res.writeHead(status, {
    "content-type": "text/plain; charset=utf-8",
    "cache-control": "no-store"
  });
  res.end(value);
}

function sendFile(res, filePath, contentType) {
  if (!fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
    return sendText(res, 404, "not found");
  }

  res.writeHead(200, {
    "content-type": contentType,
    "cache-control": "no-store"
  });
  fs.createReadStream(filePath).pipe(res);
}

function sendHtml(res, status, body) {
  res.writeHead(status, {
    "content-type": "text/html; charset=utf-8",
    "cache-control": "no-store",
    "content-length": Buffer.byteLength(body)
  });
  res.end(body);
}

function readLatestVersionInfo() {
  try {
    if (fs.existsSync(LATEST_VERSION_FILE)) {
      return JSON.parse(fs.readFileSync(LATEST_VERSION_FILE, "utf8"));
    }
  } catch (error) {
    console.warn(`failed to load latest version info: ${error.message}`);
  }
  return {
    modId: "HextechRunes",
    serverIdentity: "Natsuki.HextechRunes.official",
    name: "海克斯大乱斗",
    latestVersion: "0.5.5",
    officialBuilds: []
  };
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on("data", (chunk) => {
      size += chunk.length;
      if (size > MAX_BODY_BYTES) {
        reject(Object.assign(new Error("payload too large"), { statusCode: 413 }));
        req.destroy();
        return;
      }
      chunks.push(chunk);
    });
    req.on("end", () => resolve(Buffer.concat(chunks).toString("utf8")));
    req.on("error", reject);
  });
}

function validatePayload(payload) {
  if (!payload || typeof payload !== "object") {
    return "payload must be an object";
  }
  if (payload.schemaVersion !== 1) {
    return "unsupported schemaVersion";
  }
  if (payload.modId !== "HextechRunes") {
    return "modId must be HextechRunes";
  }
  if (!payload.run || typeof payload.run !== "object") {
    return "run is required";
  }
  if (typeof payload.run.runId !== "string" || payload.run.runId.length < 16) {
    return "run.runId is required";
  }
  if (typeof payload.run.seedHash !== "string" || payload.run.seedHash.length < 16) {
    return "run.seedHash is required";
  }
  if (typeof payload.run.isVictory !== "boolean") {
    return "run.isVictory must be boolean";
  }
  if (!Array.isArray(payload.players) || !Array.isArray(payload.runeChoices) || !Array.isArray(payload.monsterHexes)) {
    return "players, runeChoices, and monsterHexes must be arrays";
  }
  return null;
}

async function handleIngest(req, res) {
  let payload;
  try {
    payload = JSON.parse(await readBody(req));
  } catch (error) {
    return sendJson(res, error.statusCode || 400, { ok: false, error: error.message || "invalid json" });
  }

  const validationError = validatePayload(payload);
  if (validationError) {
    return sendJson(res, 400, { ok: false, error: validationError });
  }

  const runId = payload.run.runId;
  if (knownRunIds.has(runId)) {
    return sendJson(res, 202, { ok: true, duplicate: true, runId });
  }

  const record = {
    receivedAtUtc: new Date().toISOString(),
    payloadHash: sha256(JSON.stringify(payload)),
    payload
  };
  fs.appendFileSync(RESULTS_FILE, `${JSON.stringify(record)}\n`, "utf8");
  knownRunIds.add(runId);
  markDerivedDirty();
  scheduleDerivedRebuild();
  return sendJson(res, 202, { ok: true, duplicate: false, runId });
}

function sha256(value) {
  return crypto.createHash("sha256").update(value).digest("hex");
}

function forEachJsonlLine(filePath, callback) {
  if (!fs.existsSync(filePath)) {
    return 0;
  }

  const fd = fs.openSync(filePath, "r");
  const buffer = Buffer.allocUnsafe(1024 * 1024);
  let carry = Buffer.alloc(0);
  let carryOffset = 0;
  let lineCount = 0;
  try {
    while (true) {
      const bytesRead = fs.readSync(fd, buffer, 0, buffer.length, null);
      if (bytesRead <= 0) {
        break;
      }

      const chunk = carry.length > 0
        ? Buffer.concat([carry, buffer.subarray(0, bytesRead)])
        : buffer.subarray(0, bytesRead);
      let lineStart = 0;
      for (let i = 0; i < chunk.length; i++) {
        if (chunk[i] !== 10) {
          continue;
        }

        let line = chunk.subarray(lineStart, i);
        if (line.length > 0 && line[line.length - 1] === 13) {
          line = line.subarray(0, line.length - 1);
        }
        if (line.toString("utf8").trim().length > 0) {
          callback(line.toString("utf8"), carryOffset + lineStart);
          lineCount += 1;
        }
        lineStart = i + 1;
      }

      carry = Buffer.from(chunk.subarray(lineStart));
      carryOffset += chunk.length - carry.length;
    }

    if (carry.length > 0 && carry.toString("utf8").trim().length > 0) {
      callback(carry.toString("utf8"), carryOffset);
      lineCount += 1;
    }
  } finally {
    fs.closeSync(fd);
  }

  return lineCount;
}

function buildRecordIndex() {
  const latestOffsetsByRunId = new Map();
  let physicalLines = 0;
  let malformedLines = 0;
  forEachJsonlLine(RESULTS_FILE, (line, offset) => {
    physicalLines += 1;
    try {
      const record = JSON.parse(line);
      const runId = record?.payload?.run?.runId;
      if (typeof runId === "string" && runId.length > 0) {
        latestOffsetsByRunId.set(runId, offset);
      }
    } catch {
      malformedLines += 1;
    }
  });

  return {
    latestOffsets: new Set(latestOffsetsByRunId.values()),
    physicalLines,
    duplicateLines: Math.max(0, physicalLines - latestOffsetsByRunId.size - malformedLines),
    malformedLines,
    totalUniqueRuns: latestOffsetsByRunId.size
  };
}

function forEachLatestRecord(recordIndex, callback) {
  forEachJsonlLine(RESULTS_FILE, (line, offset) => {
    if (!recordIndex.latestOffsets.has(offset)) {
      return;
    }
    try {
      callback(JSON.parse(line));
    } catch {
      // Malformed latest lines are already counted during indexing.
    }
  });
}

function readRecordSet() {
  if (!fs.existsSync(RESULTS_FILE)) {
    return { records: [], physicalLines: 0, duplicateLines: 0, malformedLines: 0 };
  }

  const recordIndex = buildRecordIndex();
  const records = [];
  forEachLatestRecord(recordIndex, (record) => records.push(record));

  return {
    records,
    physicalLines: recordIndex.physicalLines,
    duplicateLines: recordIndex.duplicateLines,
    malformedLines: recordIndex.malformedLines
  };
}

function getRun(record) {
  return record?.payload?.run || {};
}

function getModVersion(record) {
  const version = record?.payload?.modVersion;
  return typeof version === "string" && version.trim().length > 0 ? version : "(unknown)";
}

function normalizeVersionFilter(value) {
  if (typeof value !== "string") {
    return null;
  }
  const trimmed = value.trim();
  if (!trimmed || trimmed === "all") {
    return null;
  }
  return trimmed;
}

function getRunTime(record) {
  const runTime = Number(getRun(record).runTime);
  return Number.isFinite(runTime) ? runTime : 0;
}

function getExcludeReasons(record) {
  const reasons = [];
  if (getRunTime(record) < MIN_RUN_TIME_FOR_DEFAULT_STATS) {
    reasons.push("short_run");
  }
  return reasons;
}

function isDefaultEligible(record) {
  return getExcludeReasons(record).length === 0;
}

function addCounter(map, key, isVictory) {
  if (!key) {
    return;
  }
  if (!map[key]) {
    map[key] = { runs: 0, wins: 0 };
  }
  map[key].runs += 1;
  if (isVictory) {
    map[key].wins += 1;
  }
}

function addMonsterCounter(map, key, isPlayerVictory) {
  if (!key) {
    return;
  }
  if (!map[key]) {
    map[key] = { runs: 0, wins: 0, playerWins: 0, monsterWins: 0 };
  }
  map[key].runs += 1;
  if (isPlayerVictory) {
    map[key].wins += 1;
    map[key].playerWins += 1;
  } else {
    map[key].monsterWins += 1;
  }
}

function addChoiceCounter(map, key, field, isVictory) {
  if (!key) {
    return;
  }
  if (!map[key]) {
    map[key] = { offered: 0, selected: 0, selectedWins: 0 };
  }
  map[key][field] += 1;
  if (field === "selected" && isVictory) {
    map[key].selectedWins += 1;
  }
}

function addSimpleCounter(map, key) {
  if (!key) {
    return;
  }
  map[key] = (map[key] || 0) + 1;
}

function buildDerivedData(options = {}) {
  const versionFilter = normalizeVersionFilter(options.version);
  const recordSet = readRecordSet();
  const records = recordSet.records;
  const versionRecords = versionFilter ? records.filter((record) => getModVersion(record) === versionFilter) : records;
  const eligibleRecords = versionRecords.filter(isDefaultEligible);
  const allEligibleRecords = records.filter(isDefaultEligible);
  const excludedShortRuns = versionRecords.length - eligibleRecords.length;
  const availableVersionCounts = {};
  for (const record of allEligibleRecords) {
    addSimpleCounter(availableVersionCounts, getModVersion(record));
  }

  const summary = {
    generatedAtUtc: new Date().toISOString(),
    filters: {
      minRunTimeForDefaultStats: MIN_RUN_TIME_FOR_DEFAULT_STATS,
      defaultExcludes: ["short_run"],
      version: versionFilter || "all"
    },
    versionFilter: versionFilter || "all",
    availableVersions: buildCountRows(availableVersionCounts),
    raw: {
      physicalLines: recordSet.physicalLines,
      uniqueRuns: versionRecords.length,
      totalUniqueRuns: records.length,
      duplicateLines: recordSet.duplicateLines,
      malformedLines: recordSet.malformedLines
    },
    runCount: eligibleRecords.length,
    winCount: 0,
    winRate: 0,
    excludedShortRuns,
    playerRuneRuns: {},
    playerRuneChoices: {},
    monsterHexRuns: {},
    versions: {},
    netModes: {},
    characters: {},
    tables: {
      playerRuneRuns: [],
      playerRuneChoices: [],
      monsterHexRuns: [],
      versions: [],
      netModes: [],
      characters: []
    }
  };

  const runsRows = [];
  const playerRuneRows = [];
  const runeChoiceRows = [];
  const monsterHexRows = [];

  for (const record of versionRecords) {
    const payload = record.payload || {};
    const run = payload.run || {};
    const isVictory = run.isVictory === true;
    const runId = run.runId || "";
    const excludeReasons = getExcludeReasons(record);
    const eligible = excludeReasons.length === 0;
    const runCommon = {
      receivedAtUtc: record.receivedAtUtc || "",
      uploadedAtUtc: payload.uploadedAtUtc || "",
      runId,
      seedHash: run.seedHash || "",
      modVersion: payload.modVersion || "",
      gameVersion: payload.gameVersion || "",
      netMode: run.netMode || "",
      netModeName: getLabel("netModes", run.netMode || ""),
      playerCount: run.playerCount || 0,
      ascension: run.ascension || 0,
      currentActIndex: run.currentActIndex || 0,
      totalFloor: run.totalFloor || 0,
      runTime: getRunTime(record),
      isVictory: isVictory ? 1 : 0,
      eligibleDefaultStats: eligible ? 1 : 0,
      excludeReasons: excludeReasons.join("|")
    };

    runsRows.push(runCommon);

    if (eligible) {
      if (isVictory) {
        summary.winCount += 1;
      }
      addSimpleCounter(summary.versions, getModVersion(record));
      addSimpleCounter(summary.netModes, run.netMode || "(unknown)");
    }

    for (const player of payload.players || []) {
      const character = player.character || "";
      const characterName = getLabel("characters", character);
      if (eligible) {
        addSimpleCounter(summary.characters, character || "(unknown)");
      }
      const hextechRunes = Array.isArray(player.hextechRunes) ? player.hextechRunes : [];
      for (const rune of hextechRunes) {
        playerRuneRows.push({
          ...runCommon,
          playerSlot: player.slot ?? "",
          character,
          characterName,
          runeName: getLabel("runes", rune),
          rune
        });
        if (eligible) {
          addCounter(summary.playerRuneRuns, rune, isVictory);
        }
      }
    }

    for (const choice of payload.runeChoices || []) {
      const options = Array.isArray(choice.options) ? choice.options : [];
      const selected = typeof choice.selected === "string" ? choice.selected : "";
      for (const option of options) {
        const isSelected = option === selected;
        runeChoiceRows.push({
          ...runCommon,
          actIndex: choice.actIndex ?? "",
          playerSlot: choice.playerSlot ?? "",
          rarity: choice.rarity || "",
          rarityName: getLabel("rarities", choice.rarity || ""),
          rerollCount: choice.rerollCount ?? 0,
          option,
          optionName: getLabel("runes", option),
          selectedRune: selected,
          selectedRuneName: getLabel("runes", selected),
          isSelected: isSelected ? 1 : 0
        });
        if (eligible) {
          addChoiceCounter(summary.playerRuneChoices, option, "offered", isVictory);
          if (isSelected) {
            addChoiceCounter(summary.playerRuneChoices, option, "selected", isVictory);
          }
        }
      }
      if (eligible && selected && !options.includes(selected)) {
        addChoiceCounter(summary.playerRuneChoices, selected, "offered", isVictory);
        addChoiceCounter(summary.playerRuneChoices, selected, "selected", isVictory);
      }
    }

    for (const monsterHex of payload.monsterHexes || []) {
      monsterHexRows.push({
        ...runCommon,
        actIndex: monsterHex.actIndex ?? "",
        rarity: monsterHex.rarity || "",
        rarityName: getLabel("rarities", monsterHex.rarity || ""),
        hex: monsterHex.hex || "",
        hexName: getLabel("monsterHexes", monsterHex.hex || "")
      });
      if (eligible) {
        addMonsterCounter(summary.monsterHexRuns, monsterHex.hex, isVictory);
      }
    }
  }

  summary.winRate = pctNumber(summary.winCount, summary.runCount);
  summary.tables.playerRuneRuns = buildRateRows(summary.playerRuneRuns, "runes");
  summary.tables.playerRuneChoices = buildChoiceRows(summary.playerRuneChoices, "runes");
  summary.tables.monsterHexRuns = buildMonsterRows(summary.monsterHexRuns, "monsterHexes");
  summary.tables.versions = buildCountRows(summary.versions);
  summary.tables.netModes = buildCountRows(summary.netModes, "netModes");
  summary.tables.characters = buildCountRows(summary.characters, "characters");

  return {
    summary,
    tables: {
      runs: runsRows,
      playerRunes: playerRuneRows,
      runeChoices: runeChoiceRows,
      monsterHexes: monsterHexRows
    }
  };
}

function buildSummaryData(options = {}) {
  const versionFilter = normalizeVersionFilter(options.version);
  const recordIndex = buildRecordIndex();
  const availableVersionCounts = {};
  const summary = {
    generatedAtUtc: new Date().toISOString(),
    filters: {
      minRunTimeForDefaultStats: MIN_RUN_TIME_FOR_DEFAULT_STATS,
      defaultExcludes: ["short_run"],
      version: versionFilter || "all"
    },
    versionFilter: versionFilter || "all",
    availableVersions: [],
    raw: {
      physicalLines: recordIndex.physicalLines,
      uniqueRuns: 0,
      totalUniqueRuns: recordIndex.totalUniqueRuns,
      duplicateLines: recordIndex.duplicateLines,
      malformedLines: recordIndex.malformedLines
    },
    runCount: 0,
    winCount: 0,
    winRate: 0,
    excludedShortRuns: 0,
    playerRuneRuns: {},
    playerRuneChoices: {},
    monsterHexRuns: {},
    versions: {},
    netModes: {},
    characters: {},
    tables: {
      playerRuneRuns: [],
      playerRuneChoices: [],
      monsterHexRuns: [],
      versions: [],
      netModes: [],
      characters: []
    }
  };

  forEachLatestRecord(recordIndex, (record) => {
    const recordVersion = getModVersion(record);
    const allEligible = isDefaultEligible(record);
    if (allEligible) {
      addSimpleCounter(availableVersionCounts, recordVersion);
    }
    if (versionFilter && recordVersion !== versionFilter) {
      return;
    }

    summary.raw.uniqueRuns += 1;
    if (!allEligible) {
      summary.excludedShortRuns += 1;
      return;
    }

    const payload = record.payload || {};
    const run = payload.run || {};
    const isVictory = run.isVictory === true;
    if (isVictory) {
      summary.winCount += 1;
    }
    summary.runCount += 1;
    addSimpleCounter(summary.versions, recordVersion);
    addSimpleCounter(summary.netModes, run.netMode || "(unknown)");

    for (const player of payload.players || []) {
      const character = player.character || "";
      addSimpleCounter(summary.characters, character || "(unknown)");
      for (const rune of Array.isArray(player.hextechRunes) ? player.hextechRunes : []) {
        addCounter(summary.playerRuneRuns, rune, isVictory);
      }
    }

    for (const choice of payload.runeChoices || []) {
      const options = Array.isArray(choice.options) ? choice.options : [];
      const selected = typeof choice.selected === "string" ? choice.selected : "";
      for (const option of options) {
        const isSelected = option === selected;
        addChoiceCounter(summary.playerRuneChoices, option, "offered", isVictory);
        if (isSelected) {
          addChoiceCounter(summary.playerRuneChoices, option, "selected", isVictory);
        }
      }
      if (selected && !options.includes(selected)) {
        addChoiceCounter(summary.playerRuneChoices, selected, "offered", isVictory);
        addChoiceCounter(summary.playerRuneChoices, selected, "selected", isVictory);
      }
    }

    for (const monsterHex of payload.monsterHexes || []) {
      addMonsterCounter(summary.monsterHexRuns, monsterHex.hex, isVictory);
    }
  });

  summary.winRate = pctNumber(summary.winCount, summary.runCount);
  summary.availableVersions = buildCountRows(availableVersionCounts);
  summary.tables.playerRuneRuns = buildRateRows(summary.playerRuneRuns, "runes");
  summary.tables.playerRuneChoices = buildChoiceRows(summary.playerRuneChoices, "runes");
  summary.tables.monsterHexRuns = buildMonsterRows(summary.monsterHexRuns, "monsterHexes");
  summary.tables.versions = buildCountRows(summary.versions);
  summary.tables.netModes = buildCountRows(summary.netModes, "netModes");
  summary.tables.characters = buildCountRows(summary.characters, "characters");
  return summary;
}

function createEmptySummary(versionFilter, recordIndex) {
  return {
    generatedAtUtc: new Date().toISOString(),
    filters: {
      minRunTimeForDefaultStats: MIN_RUN_TIME_FOR_DEFAULT_STATS,
      defaultExcludes: ["short_run"],
      version: versionFilter || "all"
    },
    versionFilter: versionFilter || "all",
    availableVersions: [],
    raw: {
      physicalLines: recordIndex.physicalLines,
      uniqueRuns: 0,
      totalUniqueRuns: recordIndex.totalUniqueRuns,
      duplicateLines: recordIndex.duplicateLines,
      malformedLines: recordIndex.malformedLines
    },
    runCount: 0,
    winCount: 0,
    winRate: 0,
    excludedShortRuns: 0,
    playerRuneRuns: {},
    playerRuneChoices: {},
    monsterHexRuns: {},
    versions: {},
    netModes: {},
    characters: {},
    tables: {
      playerRuneRuns: [],
      playerRuneChoices: [],
      monsterHexRuns: [],
      versions: [],
      netModes: [],
      characters: []
    }
  };
}

function createUnavailableSummary(versionFilter) {
  const summary = createEmptySummary(versionFilter, {
    physicalLines: 0,
    totalUniqueRuns: 0,
    duplicateLines: 0,
    malformedLines: 0
  });
  summary.generatedAtUtc = null;
  summary.pendingRefresh = true;
  return finalizeSummary(summary, []);
}

function addRecordToSummary(summary, record, recordVersion, isEligible) {
  summary.raw.uniqueRuns += 1;
  if (!isEligible) {
    summary.excludedShortRuns += 1;
    return;
  }

  const payload = record.payload || {};
  const run = payload.run || {};
  const isVictory = run.isVictory === true;
  if (isVictory) {
    summary.winCount += 1;
  }
  summary.runCount += 1;
  addSimpleCounter(summary.versions, recordVersion);
  addSimpleCounter(summary.netModes, run.netMode || "(unknown)");

  for (const player of payload.players || []) {
    const character = player.character || "";
    addSimpleCounter(summary.characters, character || "(unknown)");
    for (const rune of Array.isArray(player.hextechRunes) ? player.hextechRunes : []) {
      addCounter(summary.playerRuneRuns, rune, isVictory);
    }
  }

  for (const choice of payload.runeChoices || []) {
    const options = Array.isArray(choice.options) ? choice.options : [];
    const selected = typeof choice.selected === "string" ? choice.selected : "";
    for (const option of options) {
      const isSelected = option === selected;
      addChoiceCounter(summary.playerRuneChoices, option, "offered", isVictory);
      if (isSelected) {
        addChoiceCounter(summary.playerRuneChoices, option, "selected", isVictory);
      }
    }
    if (selected && !options.includes(selected)) {
      addChoiceCounter(summary.playerRuneChoices, selected, "offered", isVictory);
      addChoiceCounter(summary.playerRuneChoices, selected, "selected", isVictory);
    }
  }

  for (const monsterHex of payload.monsterHexes || []) {
    addMonsterCounter(summary.monsterHexRuns, monsterHex.hex, isVictory);
  }
}

function finalizeSummary(summary, availableVersions) {
  summary.winRate = pctNumber(summary.winCount, summary.runCount);
  summary.availableVersions = availableVersions;
  summary.tables.playerRuneRuns = buildRateRows(summary.playerRuneRuns, "runes");
  summary.tables.playerRuneChoices = buildChoiceRows(summary.playerRuneChoices, "runes");
  summary.tables.monsterHexRuns = buildMonsterRows(summary.monsterHexRuns, "monsterHexes");
  summary.tables.versions = buildCountRows(summary.versions);
  summary.tables.netModes = buildCountRows(summary.netModes, "netModes");
  summary.tables.characters = buildCountRows(summary.characters, "characters");
  return summary;
}

function buildSummaryBundle() {
  const recordIndex = buildRecordIndex();
  const allSummary = createEmptySummary(null, recordIndex);
  const byVersion = {};
  const availableVersionCounts = {};

  forEachLatestRecord(recordIndex, (record) => {
    const recordVersion = getModVersion(record);
    const isEligible = isDefaultEligible(record);
    if (!byVersion[recordVersion]) {
      byVersion[recordVersion] = createEmptySummary(recordVersion, recordIndex);
    }
    if (isEligible) {
      addSimpleCounter(availableVersionCounts, recordVersion);
    }
    addRecordToSummary(allSummary, record, recordVersion, isEligible);
    addRecordToSummary(byVersion[recordVersion], record, recordVersion, isEligible);
  });

  const availableVersions = buildCountRows(availableVersionCounts);
  finalizeSummary(allSummary, availableVersions);
  for (const summary of Object.values(byVersion)) {
    finalizeSummary(summary, availableVersions);
  }

  allSummary.versionSummaries = byVersion;
  return allSummary;
}

function pctNumber(part, total) {
  return total > 0 ? Number(((part / total) * 100).toFixed(1)) : 0;
}

function buildRateRows(map, labelCategory) {
  return Object.entries(map)
    .map(([id, stat]) => ({
      id,
      name: getLabel(labelCategory, id),
      runs: stat.runs,
      wins: stat.wins,
      winRate: pctNumber(stat.wins, stat.runs)
    }))
    .sort((a, b) => b.runs - a.runs || b.winRate - a.winRate || a.id.localeCompare(b.id));
}

function buildChoiceRows(map, labelCategory) {
  return Object.entries(map)
    .map(([id, stat]) => ({
      id,
      name: getLabel(labelCategory, id),
      offered: stat.offered,
      selected: stat.selected,
      pickRate: pctNumber(stat.selected, stat.offered),
      selectedWins: stat.selectedWins,
      selectedWinRate: pctNumber(stat.selectedWins, stat.selected)
    }))
    .sort((a, b) => b.selected - a.selected || b.pickRate - a.pickRate || b.offered - a.offered || a.id.localeCompare(b.id));
}

function buildMonsterRows(map, labelCategory) {
  return Object.entries(map)
    .map(([id, stat]) => ({
      id,
      name: getLabel(labelCategory, id),
      runs: stat.runs,
      playerWins: stat.playerWins,
      playerWinRate: pctNumber(stat.playerWins, stat.runs),
      monsterWins: stat.monsterWins,
      monsterWinRate: pctNumber(stat.monsterWins, stat.runs)
    }))
    .sort((a, b) => b.runs - a.runs || b.monsterWinRate - a.monsterWinRate || a.id.localeCompare(b.id));
}

function buildCountRows(map, labelCategory = null) {
  return Object.entries(map)
    .map(([id, count]) => ({ id, name: labelCategory ? getLabel(labelCategory, id) : id, count }))
    .sort((a, b) => b.count - a.count || a.id.localeCompare(b.id));
}

function writeDerivedTables() {
  const derived = buildDerivedData();
  writeFileAtomic(path.join(DERIVED_DIR, "summary.json"), `${JSON.stringify(derived.summary, null, 2)}\n`);
  writeCsv("runs.csv", derived.tables.runs, [
    "receivedAtUtc",
    "uploadedAtUtc",
    "runId",
    "seedHash",
    "modVersion",
    "gameVersion",
    "netMode",
    "netModeName",
    "playerCount",
    "ascension",
    "currentActIndex",
    "totalFloor",
    "runTime",
    "isVictory",
    "eligibleDefaultStats",
    "excludeReasons"
  ]);
  writeCsv("player_runes.csv", derived.tables.playerRunes, [
    "receivedAtUtc",
    "runId",
    "modVersion",
    "netMode",
    "netModeName",
    "playerCount",
    "ascension",
    "totalFloor",
    "runTime",
    "isVictory",
    "eligibleDefaultStats",
    "playerSlot",
    "character",
    "characterName",
    "runeName",
    "rune"
  ]);
  writeCsv("rune_choices.csv", derived.tables.runeChoices, [
    "receivedAtUtc",
    "runId",
    "modVersion",
    "netMode",
    "netModeName",
    "playerCount",
    "ascension",
    "totalFloor",
    "runTime",
    "isVictory",
    "eligibleDefaultStats",
    "actIndex",
    "playerSlot",
    "rarity",
    "rarityName",
    "rerollCount",
    "option",
    "optionName",
    "selectedRune",
    "selectedRuneName",
    "isSelected"
  ]);
  writeCsv("monster_hexes.csv", derived.tables.monsterHexes, [
    "receivedAtUtc",
    "runId",
    "modVersion",
    "netMode",
    "netModeName",
    "playerCount",
    "ascension",
    "totalFloor",
    "runTime",
    "isVictory",
    "eligibleDefaultStats",
    "actIndex",
    "rarity",
    "rarityName",
    "hex",
    "hexName"
  ]);
  return derived.summary;
}

function readDerivedSummary() {
  if (!fs.existsSync(SUMMARY_FILE)) {
    return null;
  }
  try {
    return JSON.parse(fs.readFileSync(SUMMARY_FILE, "utf8"));
  } catch {
    return null;
  }
}

function allDerivedFilesExist() {
  return DERIVED_FILE_NAMES.every((fileName) => fs.existsSync(path.join(DERIVED_DIR, fileName)));
}

function derivedFilesAreCurrent() {
  if (!fs.existsSync(SUMMARY_FILE)) {
    return false;
  }
  const resultsMtimeMs = fs.existsSync(RESULTS_FILE) ? fs.statSync(RESULTS_FILE).mtimeMs : 0;
  return fs.statSync(SUMMARY_FILE).mtimeMs >= resultsMtimeMs;
}

function markDerivedDirty() {
  derivedState.dirty = true;
}

function scheduleDerivedRebuild(delayMs = DERIVED_REFRESH_DELAY_MS) {
  if (derivedState.rebuilding || derivedState.timer) {
    return;
  }
  const safeDelayMs = Number.isFinite(delayMs) ? Math.max(0, delayMs) : DERIVED_REFRESH_DELAY_MS;
  derivedState.timer = setTimeout(() => {
    derivedState.timer = null;
    if (!derivedState.dirty && derivedFilesAreCurrent()) {
      return;
    }
    startDerivedRebuildChild();
  }, safeDelayMs);
  derivedState.timer.unref?.();
}

function startPeriodicDerivedRefresh() {
  if (derivedState.interval || SUMMARY_REFRESH_INTERVAL_MS <= 0) {
    return;
  }
  derivedState.interval = setInterval(() => {
    if (!derivedFilesAreCurrent()) {
      scheduleDerivedRebuild(0);
    }
  }, SUMMARY_REFRESH_INTERVAL_MS);
  derivedState.interval.unref?.();
}

function rebuildDerivedTablesNow() {
  if (derivedState.rebuilding) {
    return readDerivedSummary();
  }
  if (derivedState.timer) {
    clearTimeout(derivedState.timer);
    derivedState.timer = null;
  }
  derivedState.rebuilding = true;
  try {
    const summary = buildSummaryBundle();
    writeFileAtomic(SUMMARY_FILE, `${JSON.stringify(summary, null, 2)}\n`);
    writeFileAtomic(DERIVED_INDEX_FILE, renderIndexHtml(getDefaultDisplayVersion()));
    derivedState.dirty = false;
    derivedState.lastBuiltAtMs = Date.now();
    derivedState.lastError = null;
    return summary;
  } finally {
    derivedState.rebuilding = false;
  }
}

function lockLooksStale(lockPath) {
  try {
    const ageMs = Date.now() - fs.statSync(lockPath).mtimeMs;
    return ageMs > 30 * 60 * 1000;
  } catch {
    return false;
  }
}

function rebuildDerivedTablesWithLock() {
  fs.mkdirSync(DERIVED_DIR, { recursive: true });
  let fd = null;
  try {
    fd = fs.openSync(REBUILD_LOCK_FILE, "wx");
  } catch (error) {
    if (error?.code === "EEXIST" && lockLooksStale(REBUILD_LOCK_FILE)) {
      fs.rmSync(REBUILD_LOCK_FILE, { force: true });
      fd = fs.openSync(REBUILD_LOCK_FILE, "wx");
    } else {
      return readDerivedSummary();
    }
  }
  try {
    fs.writeFileSync(fd, `${process.pid}\n${new Date().toISOString()}\n`, "utf8");
    return rebuildDerivedTablesNow();
  } finally {
    if (fd != null) {
      fs.closeSync(fd);
    }
    fs.rmSync(REBUILD_LOCK_FILE, { force: true });
  }
}

function startDerivedRebuildChild() {
  if (derivedState.rebuilding) {
    return;
  }
  derivedState.rebuilding = true;
  const child = spawn(process.execPath, [__filename, "--rebuild-derived"], {
    env: process.env,
    stdio: "ignore"
  });
  child.on("exit", (code) => {
    derivedState.rebuilding = false;
    if (code === 0) {
      derivedState.dirty = false;
      derivedState.lastBuiltAtMs = Date.now();
      derivedState.lastError = null;
      summaryCache.clear();
    } else {
      derivedState.lastError = `rebuild child exited with code ${code}`;
      scheduleDerivedRebuild(Math.max(DERIVED_REFRESH_DELAY_MS, 30000));
    }
  });
  child.on("error", (error) => {
    derivedState.rebuilding = false;
    derivedState.lastError = error?.message || String(error);
    scheduleDerivedRebuild(Math.max(DERIVED_REFRESH_DELAY_MS, 30000));
  });
  child.unref?.();
}

function getSummaryFileMtimeMs() {
  try {
    return fs.statSync(SUMMARY_FILE).mtimeMs;
  } catch {
    return 0;
  }
}

function getDerivedStatus() {
  const summaryMtimeMs = getSummaryFileMtimeMs();
  const resultsMtimeMs = fs.existsSync(RESULTS_FILE) ? fs.statSync(RESULTS_FILE).mtimeMs : 0;
  return {
    summaryExists: summaryMtimeMs > 0,
    summaryCurrent: summaryMtimeMs >= resultsMtimeMs,
    summaryGeneratedAtUtc: summaryMtimeMs > 0 ? new Date(summaryMtimeMs).toISOString() : null,
    resultsUpdatedAtUtc: resultsMtimeMs > 0 ? new Date(resultsMtimeMs).toISOString() : null,
    refreshIntervalMs: SUMMARY_REFRESH_INTERVAL_MS,
    scheduled: Boolean(derivedState.timer),
    rebuilding: derivedState.rebuilding,
    lastBuiltAtUtc: derivedState.lastBuiltAtMs ? new Date(derivedState.lastBuiltAtMs).toISOString() : null,
    lastError: derivedState.lastError
  };
}

function makeEmptyDerivedVersionSummary(allSummary, versionFilter) {
  const empty = createEmptySummary(versionFilter, {
    physicalLines: allSummary.raw?.physicalLines || 0,
    totalUniqueRuns: allSummary.raw?.totalUniqueRuns || allSummary.raw?.uniqueRuns || 0,
    duplicateLines: allSummary.raw?.duplicateLines || 0,
    malformedLines: allSummary.raw?.malformedLines || 0
  });
  empty.generatedAtUtc = allSummary.generatedAtUtc;
  empty.availableVersions = allSummary.availableVersions || allSummary.tables?.versions || [];
  return finalizeSummary(empty, empty.availableVersions);
}

function getSummaryFromDerived(versionFilter = null) {
  const summary = readDerivedSummary();
  if (!summary) {
    return null;
  }
  const normalizedVersionFilter = normalizeVersionFilter(versionFilter);
  if (!normalizedVersionFilter) {
    const { versionSummaries, ...publicSummary } = summary;
    return publicSummary;
  }
  return summary.versionSummaries?.[normalizedVersionFilter] || makeEmptyDerivedVersionSummary(summary, normalizedVersionFilter);
}

function getSummaryForDisplay(versionFilter = null) {
  const normalizedVersionFilter = normalizeVersionFilter(versionFilter);
  const summaryMtimeMs = getSummaryFileMtimeMs();
  const cacheKey = normalizedVersionFilter || "all";
  const cached = summaryCache.get(cacheKey);
  if (cached && cached.summaryMtimeMs === summaryMtimeMs) {
    return cached.summary;
  }

  const derivedSummary = getSummaryFromDerived(normalizedVersionFilter);
  if (derivedSummary) {
    if (!derivedFilesAreCurrent()) {
      scheduleDerivedRebuild();
    }
    summaryCache.set(cacheKey, { summaryMtimeMs, summary: derivedSummary });
    return derivedSummary;
  }

  scheduleDerivedRebuild(0);
  const summary = createUnavailableSummary(normalizedVersionFilter);
  summaryCache.set(cacheKey, { summaryMtimeMs: 0, summary });
  return summary;
}

function writeCsv(fileName, rows, headers) {
  const lines = [headers.join(",")];
  for (const row of rows) {
    lines.push(headers.map((header) => csvCell(row[header])).join(","));
  }
  writeFileAtomic(path.join(DERIVED_DIR, fileName), `${lines.join("\n")}\n`);
}

function csvCell(value) {
  const raw = value == null ? "" : String(value);
  if (/[",\r\n]/.test(raw)) {
    return `"${raw.replaceAll('"', '""')}"`;
  }
  return raw;
}

function writeFileAtomic(filePath, body) {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  const tmpPath = `${filePath}.${process.pid}.tmp`;
  fs.writeFileSync(tmpPath, body, "utf8");
  fs.renameSync(tmpPath, filePath);
}

function serveDerived(req, res, pathname) {
  const fileName = path.basename(pathname);
  if (!DERIVED_FILE_NAMES.includes(fileName)) {
    return sendText(res, 404, "not found");
  }
  const filePath = path.join(DERIVED_DIR, fileName);
  if (!fs.existsSync(filePath)) {
    if (fileName === "summary.json") {
      return sendJson(res, 200, getSummaryForDisplay(null));
    }
    return sendText(res, 503, "derived file is not available; rebuild it offline");
  }
  const contentType = fileName.endsWith(".json") ? "application/json; charset=utf-8" : "text/csv; charset=utf-8";
  return sendFile(res, filePath, contentType);
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (ch) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#39;"
  })[ch]);
}

function fmtPct(value) {
  return `${Number(value || 0).toFixed(1)}%`;
}

function compareVersionsDesc(a, b) {
  return String(b).localeCompare(String(a), undefined, { numeric: true, sensitivity: "base" });
}

function getVersionIdsForDisplay(summary, latestVersion = null) {
  const versions = new Set();
  for (const row of summary?.availableVersions || []) {
    if (row?.id) {
      versions.add(row.id);
    }
  }
  const normalizedLatestVersion = normalizeVersionFilter(latestVersion);
  if (normalizedLatestVersion) {
    versions.add(normalizedLatestVersion);
  }
  return [...versions].sort(compareVersionsDesc);
}

function getDefaultDisplayVersion() {
  const latestVersion = normalizeVersionFilter(readLatestVersionInfo().latestVersion);
  const summary = getSummaryForDisplay(null);
  const availableVersions = new Set((summary.availableVersions || []).map((row) => row.id).filter(Boolean));
  if (latestVersion && availableVersions.has(latestVersion)) {
    return latestVersion;
  }
  const newestAvailableVersion = [...availableVersions].sort(compareVersionsDesc)[0];
  return newestAvailableVersion || latestVersion || null;
}

function buildFilterNote(summary) {
  const versionText = summary.versionFilter === "all" ? "全部版本" : `版本 ${summary.versionFilter}`;
  return `当前统计口径：${versionText}，排除 runTime < ${summary.filters.minRunTimeForDefaultStats} 秒的历史局；0.5.0 起客户端会直接跳过短局上传。`;
}

function renderTable(headers, rows) {
  if (!rows.length) {
    return [
      `<thead><tr>${headers.map((header) => `<th>${escapeHtml(header)}</th>`).join("")}</tr></thead>`,
      `<tbody><tr><td colspan="${headers.length}">暂无可显示数据</td></tr></tbody>`
    ].join("");
  }
  return [
    `<thead><tr>${headers.map((header) => `<th>${escapeHtml(header)}</th>`).join("")}</tr></thead>`,
    `<tbody>${rows.map((row) => `<tr>${row.map((cell) => `<td>${escapeHtml(cell)}</td>`).join("")}</tr>`).join("")}</tbody>`
  ].join("");
}

function renderVersionOptions(summary, selectedVersion, latestVersion) {
  const versionCounts = new Map((summary.availableVersions || []).map((row) => [row.id, row.count]));
  const normalizedSelectedVersion = selectedVersion || "all";
  const options = [
    `<option value="all"${normalizedSelectedVersion === "all" ? " selected" : ""}>全部版本</option>`
  ];
  for (const version of getVersionIdsForDisplay(summary, latestVersion)) {
    const count = versionCounts.get(version);
    const suffix = Number.isFinite(count) ? `（${count}局）` : "（暂无样本）";
    options.push(
      `<option value="${escapeHtml(version)}"${normalizedSelectedVersion === version ? " selected" : ""}>${escapeHtml(`${version}${suffix}`)}</option>`
    );
  }
  return options.join("");
}

function renderIndexHtml(versionFilter = null) {
  const summary = getSummaryForDisplay(versionFilter);
  const latestVersion = normalizeVersionFilter(readLatestVersionInfo().latestVersion);
  const indexPath = path.join(PUBLIC_DIR, "index.html");
  let html = fs.readFileSync(indexPath, "utf8");
  const note = buildFilterNote(summary);
  const generatedAtUtc = summary.generatedAtUtc || "";
  const updatedText = generatedAtUtc ? `更新时间：${generatedAtUtc}` : "等待生成统计数据";
  const replacements = [
    [
      /<div class="muted" id="updated"(?: data-generated-at-utc="[^"]*")?>.*?<\/div>/,
      `<div class="muted" id="updated" data-generated-at-utc="${escapeHtml(generatedAtUtc)}">${escapeHtml(updatedText)}</div>`
    ],
    [/<select id="versionFilter">\s*<option value="all">全部版本<\/option>\s*<\/select>/, `<select id="versionFilter">${renderVersionOptions(summary, summary.versionFilter, latestVersion)}</select>`],
    [/<b id="eligibleRuns">0<\/b>/, `<b id="eligibleRuns">${summary.runCount}</b>`],
    [/<b id="rawRuns">0<\/b>/, `<b id="rawRuns">${summary.raw.uniqueRuns}</b>`],
    [/<b id="shortRuns">0<\/b>/, `<b id="shortRuns">${summary.excludedShortRuns}</b>`],
    [/<b id="wins">0<\/b>/, `<b id="wins">${summary.winCount}</b>`],
    [/<b id="winRate">0%<\/b>/, `<b id="winRate">${fmtPct(summary.winRate)}</b>`],
    [/<div class="muted" id="filterNote"><\/div>/, `<div class="muted" id="filterNote">${escapeHtml(note)}</div>`],
    [/<table id="versions"><\/table>/, `<table id="versions">${renderTable(["版本", "局数"], summary.tables.versions.slice(0, 20).map((row) => [displayLabel(row), row.count]))}</table>`],
    [/<table id="netModes"><\/table>/, `<table id="netModes">${renderTable(["模式", "局数"], summary.tables.netModes.slice(0, 20).map((row) => [displayLabel(row), row.count]))}</table>`],
    [/<table id="characters"><\/table>/, `<table id="characters">${renderTable(["角色", "玩家样本"], summary.tables.characters.slice(0, 20).map((row) => [displayLabel(row), row.count]))}</table>`],
    [/<span class="panel-meta" id="choicesMeta">0 条<\/span>/, `<span class="panel-meta" id="choicesMeta">${summary.tables.playerRuneChoices.length} 条</span>`],
    [/<span class="panel-meta" id="monsterHexesMeta">0 条<\/span>/, `<span class="panel-meta" id="monsterHexesMeta">${summary.tables.monsterHexRuns.length} 条</span>`],
    [/<table id="choices"><\/table>/, `<table id="choices">${renderTable(["海克斯", "出现", "选择", "选择率", "选择后胜率"], summary.tables.playerRuneChoices.map((row) => [displayLabel(row), row.offered, row.selected, fmtPct(row.pickRate), fmtPct(row.selectedWinRate)]))}</table>`],
    [/<table id="monsterHexes"><\/table>/, `<table id="monsterHexes">${renderTable(["敌方海克斯", "出现局数", "敌方胜利", "敌方胜率", "玩家胜率"], summary.tables.monsterHexRuns.map((row) => [displayLabel(row), row.runs, row.monsterWins, fmtPct(row.monsterWinRate), fmtPct(row.playerWinRate)]))}</table>`]
  ];
  for (const [pattern, replacement] of replacements) {
    html = html.replace(pattern, replacement);
  }
  return html;
}

function serveStatic(req, res) {
  const url = new URL(req.url, "http://localhost");
  let pathname = decodeURIComponent(url.pathname);
  if (pathname === "/") {
    pathname = "/index.html";
  }

  if (pathname === "/index.html") {
    const versionFilter = url.searchParams.has("version")
      ? normalizeVersionFilter(url.searchParams.get("version"))
      : getDefaultDisplayVersion();
    return sendHtml(res, 200, renderIndexHtml(versionFilter));
  }

  const filePath = path.normalize(path.join(PUBLIC_DIR, pathname));
  const relativePath = path.relative(PUBLIC_DIR, filePath);
  if (relativePath.startsWith("..") || path.isAbsolute(relativePath)) {
    return sendText(res, 403, "forbidden");
  }

  if (!fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
    return sendText(res, 404, "not found");
  }

  const ext = path.extname(filePath);
  const contentType = {
    ".html": "text/html; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".js": "application/javascript; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".svg": "image/svg+xml"
  }[ext] || "application/octet-stream";

  res.writeHead(200, {
    "content-type": contentType,
    "cache-control": ext === ".html" ? "no-cache" : "public, max-age=3600"
  });
  fs.createReadStream(filePath).pipe(res);
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, "http://localhost");
  if (req.method === "GET" && url.pathname === "/health") {
    return sendJson(res, 200, { ok: true, service: "hextech-runes-telemetry", runs: knownRunIds.size, derived: getDerivedStatus() });
  }
  if (req.method === "GET" && url.pathname === "/api/hextech-runes/summary") {
    return sendJson(res, 200, getSummaryForDisplay(url.searchParams.get("version")));
  }
  if (req.method === "GET" && url.pathname === "/api/hextech-runes/latest-version") {
    return sendJson(res, 200, readLatestVersionInfo());
  }
  if (req.method === "GET" && url.pathname.startsWith("/api/hextech-runes/derived/")) {
    return serveDerived(req, res, url.pathname);
  }
  if (req.method === "POST" && url.pathname === "/api/hextech-runes/run-result") {
    return handleIngest(req, res);
  }
  if (req.method === "GET" || req.method === "HEAD") {
    return serveStatic(req, res);
  }
  return sendText(res, 405, "method not allowed");
});

if (isRebuildProcess) {
  try {
    rebuildDerivedTablesWithLock();
    process.exit(0);
  } catch (error) {
    console.error(error?.stack || error);
    process.exit(1);
  }
}

if (!derivedFilesAreCurrent()) {
  scheduleDerivedRebuild(0);
}
startPeriodicDerivedRefresh();

server.listen(PORT, HOST, () => {
  console.log(`hextech-runes-telemetry listening on ${HOST}:${PORT}`);
});
