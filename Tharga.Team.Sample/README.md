# Tharga.Team.Sample

A Blazor Server site that uses every Tharga.Team package, so each feature can be tried in one place. The menu on the
left and the cards on the home page list every page by area.

## Run it

- **MongoDB** on `mongodb://localhost:27017`. The sample creates its own database.
- **Sign-in** is Microsoft Entra ID, configured in `appsettings.json`. Put the client secret in user secrets:

  ```powershell
  dotnet user-secrets set "AzureAd:ClientSecret" "<secret>" --project Tharga.Team.Sample
  ```

- Start it with the `https` profile and open `https://localhost:7291`:

  ```powershell
  dotnet run --project Tharga.Team.Sample --launch-profile https
  ```

## Trying support

- **The side panel** on the right of every page is the member's side: the assistant, and support.
- **Support → AI assistant** chooses what the assistant answers the selected team with. Until a team chooses, it
  is the sample's built-in responder, a short script rather than an AI model, which says so in its answers.
- **System → Support desk** (Developer role) is the back office. Open it in a second window, write in the panel's
  Support tab, and answer from the desk: each side reads as a chat. It also lists what the assistant was asked and
  could not do; with the built-in responder, that is every question off its script.
- **Online, Away or Hidden** at the top of the desk is how you are shown to members. The panel's Support tab says
  "Support is online" while you are Online and have done something on the desk in the last ten minutes, and
  "Support will reply later" otherwise. Hidden lets you answer without being shown. A message to support that nobody
  answers within a minute is followed by a note that we will get back to them; while you are away, at once.

## Connect Slack

With Slack connected, a support case opens a thread in your support channel, and a reply typed in that thread
appears on the case: in the side panel, on the support page and on the desk. The full guide is
[Slack threads](https://team.tharga.net/articles/support-cases.html#slack-threads); these are the steps for this
sample.

### Settings

The sample reads these from user secrets:

| Key | What it is |
|---|---|
| `Slack:BotToken` | The app's Bot User OAuth Token (`xoxb-…`). Outbound needs nothing more. |
| `Slack:Channel` | Where notifications go, such as `#activity`. |
| `Slack:SupportChannel` | Where a case opens its thread, such as `#support`. |
| `Slack:SigningSecret` | The app's Signing Secret. Needed to receive replies typed in Slack. |
| `Slack:CaseUrl` | Where a notification links to a case: `https://localhost:7291/support/{caseId}`, or your tunnel's address. |

```powershell
dotnet user-secrets set "Slack:BotToken" "xoxb-…" --project Tharga.Team.Sample
dotnet user-secrets set "Slack:SupportChannel" "#support" --project Tharga.Team.Sample
dotnet user-secrets set "Slack:SigningSecret" "<signing secret>" --project Tharga.Team.Sample
```

### Steps

1. **Create a Slack app** from the repository's `slack-app-manifest.json` (Slack API → Your Apps → Create New App
   → From an app manifest), install it to your workspace, and copy its token and signing secret into the settings
   above.
2. **Invite the bot** to both channels: `/invite @Tharga Support`.
3. **Run the sample.** Raise a case on the Support tab of the side panel: a thread opens in the support channel.
   That is the outbound half working.
4. **Give Slack a way in.** Slack cannot reach `localhost`, so a reply typed in Slack reaches the sample only through
   a public address. Start a tunnel to port 7291, for example:

   ```powershell
   devtunnel host -p 7291 --allow-anonymous      # Microsoft Dev Tunnels
   ngrok http https://localhost:7291             # or ngrok
   ```

5. **Point the app at it.** Slack app → Event Subscriptions → enable, and set the Request URL to
   `https://<your tunnel>/_tharga/support/slack/events`. Slack checks the address as you save it, and the sample
   answers the check, so a green tick means the whole way in works. Under *Subscribe to bot events*, add
   `message.channels`, or `message.groups` for a private support channel.
6. **Check it.** Reply in a case's thread. The sample's log shows
   `HTTP POST /_tharga/support/slack/events responded 200`, and the reply appears in the side panel while that
   conversation is open, and on the support page and the desk.

**A Slack app has exactly one Request URL.** If yours already points at a deployed site, replies go there and never
reach your machine: create a second app for local testing rather than moving the first one back and forth. A free
tunnel's address changes each time it starts, so set the Request URL again after restarting it, or use a persistent
tunnel.
