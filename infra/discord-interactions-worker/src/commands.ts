// Slash-command definitions and global registration via the Discord REST API.
// registerCommands() is meant to be invoked manually (e.g. from a wrangler
// script or a one-off Worker route), not on every interaction.

interface RegisterEnv {
  DS_APP_ID: string;
  DS_BOT_TOKEN: string;
}

export interface CommandDefinition {
  name: string;
  description: string;
  options?: Array<{
    name: string;
    description: string;
    type: number; // 3 = STRING, 4 = INTEGER
    required: boolean;
  }>;
}

export const COMMAND_DEFINITIONS: CommandDefinition[] = [
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
      // STRING (type 3): Snowflake IDs exceed Number.MAX_SAFE_INTEGER and
      // would be rejected client-side / lose precision as INTEGER (type 4).
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

// Overwrite all global application commands with the definitions above.
export async function registerCommands(env: RegisterEnv): Promise<void> {
  const url = `https://discord.com/api/v10/applications/${env.DS_APP_ID}/commands`;
  const response = await fetch(url, {
    method: "PUT",
    headers: {
      Authorization: `Bot ${env.DS_BOT_TOKEN}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(COMMAND_DEFINITIONS),
  });

  // Log status only — never the token or response body (may echo app data).
  console.log("registerCommands: Discord API status", response.status);
  if (!response.ok) {
    console.error("registerCommands: failed to register commands, status", response.status);
  }
}
