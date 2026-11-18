"""EscapeBook Discord listener (Hugging Face Space, Docker).

Listens to a single Discord channel via the official discord.py bot client
(Message Content Intent required) and forwards every non-bot message to the
Cloudflare telegram-bridge-worker. The worker unifies delivery into one
Telegram group, forwards media and preserves reply threads. The Telegram bot
token NEVER touches this script — it stays in Cloudflare secrets.

Environment variables:
- DS_BOT_TOKEN        : Discord bot token (Message Content Intent enabled)
- DISCORD_CHANNEL_ID  : only messages from this channel are forwarded
- AUTH_WORKER_URL     : base URL of the telegram-bridge-worker
                        (e.g. https://escapebook-tg-bridge.<account>.workers.dev)
- BRIDGE_SECRET       : (optional) shared secret sent as x-bridge-secret header
                        when the worker has BRIDGE_SECRET configured
"""

import os

import discord
import requests
from dotenv import load_dotenv

load_dotenv()

intents = discord.Intents.default()
intents.message_content = True

client = discord.Client(intents=intents)

BRIDGE_PATH = "/bridge/ds-to-tg"
REQUEST_TIMEOUT_SECONDS = 15


def forward_to_worker(
    ds_id: str,
    ds_username: str,
    ds_nick: str,
    text: str,
    message_id: int,
    attachments: list,
    reference_message_id: "str | None",
) -> None:
    worker_url = (os.getenv("AUTH_WORKER_URL") or "").rstrip("/")
    if not worker_url:
        print("❌ AUTH_WORKER_URL is not set; message dropped")
        return

    payload = {
        "ds_id": ds_id,
        "ds_username": ds_username,
        "ds_nick": ds_nick,
        "text": text,
        "message_id": str(message_id),
        "attachments": attachments,
        "reference_message_id": reference_message_id,
    }

    headers = {"Content-Type": "application/json"}
    # Обязателен, если в воркере настроен BRIDGE_SECRET (иначе HTTP 401).
    bridge_secret = os.getenv("BRIDGE_SECRET")
    if bridge_secret:
        headers["x-bridge-secret"] = bridge_secret

    try:
        print(
            f"📤 Sending to worker: {ds_nick} -> {text[:30]}... "
            f"(attachments: {len(attachments)}, reply: {reference_message_id is not None})"
        )
        response = requests.post(
            worker_url + BRIDGE_PATH,
            json=payload,
            headers=headers,
            timeout=REQUEST_TIMEOUT_SECONDS,
        )
        if response.ok:
            print(f"✅ Bridge OK for message {message_id}")
        else:
            print(
                f"⚠️ Bridge HTTP {response.status_code} for message {message_id}: "
                f"{response.text[:200]}"
            )
    except requests.RequestException as exc:
        print(f"❌ Bridge request failed for message {message_id}: {exc.__class__.__name__}")


@client.event
async def on_ready() -> None:
    print("✅ Listener connected to Discord")
    print(f"📡 Monitoring channel ID: {os.getenv('DISCORD_CHANNEL_ID')}")


@client.event
async def on_message(message: discord.Message) -> None:
    if message.author.bot:
        return
    if str(message.channel.id) != os.getenv("DISCORD_CHANNEL_ID"):
        return

    ds_id = str(message.author.id)
    ds_username = message.author.name
    ds_nick = getattr(message.author, "display_name", None) or ds_username
    text = message.content or ""

    # Собираем вложения
    attachments = []
    for attachment in message.attachments:
        attachments.append(
            {
                "url": attachment.url,
                "filename": attachment.filename,
                "content_type": attachment.content_type or "",
            }
        )

    # Проверяем, является ли сообщение ответом
    reference_message_id = None
    if message.reference and message.reference.message_id:
        reference_message_id = str(message.reference.message_id)

    if not text and not attachments:
        return

    # Отправляем в фоне, чтобы не блокировать discord-события
    import threading

    threading.Thread(
        target=forward_to_worker,
        args=(
            ds_id,
            ds_username,
            ds_nick,
            text,
            message.id,
            attachments,
            reference_message_id,
        ),
        daemon=True,
    ).start()


def main() -> None:
    token = os.getenv("DS_BOT_TOKEN")
    if not token:
        raise SystemExit("❌ DS_BOT_TOKEN is not set in Secrets")
    if not os.getenv("DISCORD_CHANNEL_ID"):
        raise SystemExit("❌ DISCORD_CHANNEL_ID is not set in Secrets")
    if not os.getenv("AUTH_WORKER_URL"):
        raise SystemExit("❌ AUTH_WORKER_URL is not set in Secrets")

    print("🚀 Starting Discord client...")
    client.run(token)


if __name__ == "__main__":
    main()
