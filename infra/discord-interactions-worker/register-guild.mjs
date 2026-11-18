// Two-phase slash-command registration:
//   1) PUT [] to GLOBAL commands — removes stale globals that still use
//      INTEGER (type 4) options (Discord validates those client-side against
//      the safe-integer range, which rejects Snowflake IDs).
//   2) PUT the updated definitions to GUILD commands — instant propagation.
//
// Usage (PowerShell, from infra/discord-interactions-worker):
//   $env:DS_APP_ID="<application id>"
//   $env:DS_BOT_TOKEN="<bot token>"
//   $env:DS_GUILD_ID="<server id>"
//   node register-guild.mjs
//
// The token is read from the environment only and is never printed.
// Keep COMMANDS in sync with src/commands.ts (COMMAND_DEFINITIONS).
// tg_id/ds_id are STRING options (type 3): Snowflake IDs exceed
// Number.MAX_SAFE_INTEGER and would lose precision as INTEGER (type 4).

const COMMANDS = [
  {
    name: "add",
    description: "Add new player",
    options: [
      { name: "mc_nickname", description: "Minecraft nickname", type: 3, required: true },
    ],
  },
  {
    name: "link",
    description: "Link accounts",
    options: [
      { name: "mc_nickname", description: "Minecraft nickname", type: 3, required: true },
      { name: "tg_id", description: "Telegram ID (число, вставьте цифры)", type: 3, required: false },
      { name: "ds_id", description: "Discord ID (число, вставьте цифры)", type: 3, required: false },
    ],
  },
  {
    name: "ban",
    description: "Ban player",
    options: [
      { name: "mc_nickname", description: "Minecraft nickname", type: 3, required: true },
    ],
  },
  {
    name: "unban",
    description: "Unban player",
    options: [
      { name: "mc_nickname", description: "Minecraft nickname", type: 3, required: true },
    ],
  },
  {
    name: "info",
    description: "Show player info",
    options: [
      { name: "mc_nickname", description: "Minecraft nickname", type: 3, required: true },
    ],
  },
];

const { DS_APP_ID, DS_BOT_TOKEN, DS_GUILD_ID } = process.env;

if (!DS_APP_ID || !DS_BOT_TOKEN || !DS_GUILD_ID) {
  console.error("Required env vars: DS_APP_ID, DS_BOT_TOKEN, DS_GUILD_ID");
  process.exit(1);
}

async function put(label, url, body) {
  const response = await fetch(url, {
    method: "PUT",
    headers: {
      Authorization: `Bot ${DS_BOT_TOKEN}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  });

  // Status is safe to print; the token never is.
  console.log(`${label}: HTTP ${response.status}`);

  if (!response.ok) {
    // Print the full Discord error JSON to diagnose the failure.
    const errorText = await response.text();
    console.error(`${label} failed. Discord response:`, errorText);
    process.exit(1);
  }
  return response;
}

// 1) Clear stale GLOBAL commands (old INTEGER-typed options).
await put(
  "Clear global commands",
  `https://discord.com/api/v10/applications/${DS_APP_ID}/commands`,
  []
);

// 2) Register updated commands on the guild (instant effect).
await put(
  "Register guild commands",
  `https://discord.com/api/v10/applications/${DS_APP_ID}/guilds/${DS_GUILD_ID}/commands`,
  COMMANDS
);

console.log("Done: globals cleared, guild commands registered (STRING tg_id/ds_id).");
